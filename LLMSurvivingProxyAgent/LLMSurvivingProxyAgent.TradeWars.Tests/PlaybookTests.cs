using System.Text;
using LLMSurvivingProxyAgent.Core;
using LLMSurvivingProxyAgent.Knowledge;
using LLMSurvivingProxyAgent.Runtime;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Logging.Abstractions;

namespace LLMSurvivingProxyAgent.TradeWars.Tests;

public sealed class PlaybookTests
{
    private static readonly DateTimeOffset BaseTime = DateTimeOffset.UtcNow.AddMinutes(-10);

    [Fact]
    public void CoachCommandParsesPlainTip()
    {
        CoachCommand command = CoachCommandParser.Parse("!coach Sell cargo before docking at StarDock.");

        Assert.Equal(CoachCommandKind.AddEntry, command.Kind);
        Assert.Equal(PlaybookTier.Tip, command.Tier);
        Assert.Equal(PlaybookScope.Universal, command.Scope);
        Assert.Empty(command.AppliesWhen!);
        Assert.Equal("Sell cargo before docking at StarDock.", command.Guidance);
    }

    [Fact]
    public void CoachCommandParsesRuleWithTagsContainingColons()
    {
        CoachCommand command = CoachCommandParser.Parse(
            "!coach rule game never move when holds:<30, !location:fedspace : Stay in FedSpace until you have 30 holds.");

        Assert.Equal(CoachCommandKind.AddEntry, command.Kind);
        Assert.Equal(PlaybookTier.Rule, command.Tier);
        Assert.Equal(PlaybookScope.Game, command.Scope);
        Assert.Equal(AutonomousTestAction.MoveToAdjacentSector, command.ForbiddenAction);
        Assert.Equal(["holds:<30", "!location:fedspace"], command.AppliesWhen);
        Assert.Equal("Stay in FedSpace until you have 30 holds.", command.Guidance);
    }

    [Fact]
    public void CoachCommandTreatsUnmodifiedColonTextAsGuidance()
    {
        CoachCommand command = CoachCommandParser.Parse("!coach Remember: holds first, then fighters.");

        Assert.Equal(CoachCommandKind.AddEntry, command.Kind);
        Assert.Equal("Remember: holds first, then fighters.", command.Guidance);
    }

    [Theory]
    [InlineData("!coach rule : no action named")]
    [InlineData("!coach never fly : unknown action")]
    [InlineData("!coach when credits:lots : unknown tag")]
    [InlineData("!coach tip :")]
    public void CoachCommandRejectsInvalidInput(string text)
    {
        Assert.Equal(CoachCommandKind.Error, CoachCommandParser.Parse(text).Kind);
    }

    [Fact]
    public void CoachCommandRecognizesHelpAndRejectsLookalikes()
    {
        Assert.Equal(CoachCommandKind.Help, CoachCommandParser.Parse("!coach").Kind);
        Assert.Equal(CoachCommandKind.Help, CoachCommandParser.Parse("  !COACH help").Kind);
        Assert.False(CoachCommandParser.IsCoachCommand("!coaches are great"));
        Assert.False(CoachCommandParser.IsCoachCommand("coach me"));
    }

    [Fact]
    public void SituationTagsDescribeEarlyGameAndMatchWithNegation()
    {
        GameSnapshot snapshot = Snapshot(1, credits: 800, turns: 40) with
        {
            DensityScannerInstalled = false,
            HoloScannerInstalled = false
        };

        IReadOnlySet<string> tags = PlaybookSituation.Describe(
            snapshot,
            new PlaybookSituation.Extras(StarDockSector: 1, FederationSpace: true));

        Assert.Contains("credits:<1k", tags);
        Assert.Contains("holds:<30", tags);
        Assert.Contains("turns:<50", tags);
        Assert.Contains("scanner:none", tags);
        Assert.Contains("cargo:empty", tags);
        Assert.Contains("location:stardock", tags);
        Assert.Contains("location:fedspace", tags);
        Assert.True(PlaybookSituation.Matches(["scanner:none", "credits:<1k"], tags));
        Assert.False(PlaybookSituation.Matches(["scanner:none", "!location:fedspace"], tags));
        Assert.True(PlaybookSituation.Matches([], tags));
    }

    [Fact]
    public void GuidanceSelectionFiltersByScopeAndSituationAndKeepsEveryRule()
    {
        var tags = new HashSet<string> { "scanner:none", "credits:<1k" };
        PlaybookEntry[] entries =
        [
            Coach("rule", PlaybookTier.Rule, ["scanner:none"]) with { ForbiddenAction = AutonomousTestAction.StartWorldTradeMode },
            Coach("tip-any", PlaybookTier.Tip, []),
            Coach("tip-other-situation", PlaybookTier.Tip, ["scanner:holo"]),
            Coach("tip-other-game", PlaybookTier.Tip, []) with { Scope = PlaybookScope.Game, GameKey = "Game E" },
            Coach("tip-this-game", PlaybookTier.Tip, []) with { Scope = PlaybookScope.Game, GameKey = "Game D" },
            Lesson("lesson", ["credits:<1k"]),
            Lesson("retired", []) with { Status = PlaybookStatus.Retired }
        ];

        PlaybookGuidance guidance = PlaybookGuidanceProvider.Select(entries, "game d", tags);

        Assert.Equal(["rule"], guidance.Rules.Select(entry => entry.EntryId));
        Assert.Equal(
            ["tip-any", "tip-this-game"],
            guidance.Tips.Select(entry => entry.EntryId).Order(StringComparer.Ordinal));
        Assert.Equal(["lesson"], guidance.Lessons.Select(entry => entry.EntryId));
    }

    [Fact]
    public void RuleGateRefusesOnlyTheForbiddenActionInItsSituation()
    {
        PlaybookEntry rule = Coach("rule", PlaybookTier.Rule, ["holds:<30"]) with
        {
            ForbiddenAction = AutonomousTestAction.MoveToAdjacentSector
        };

        Assert.Same(rule, PlaybookRuleGate.FindViolation([rule], new HashSet<string> { "holds:<30" }, AutonomousTestAction.MoveToAdjacentSector));
        Assert.Null(PlaybookRuleGate.FindViolation([rule], new HashSet<string> { "holds:30-59" }, AutonomousTestAction.MoveToAdjacentSector));
        Assert.Null(PlaybookRuleGate.FindViolation([rule], new HashSet<string> { "holds:<30" }, AutonomousTestAction.TradeAtCurrentPort));
        Assert.Null(PlaybookRuleGate.FindViolation([rule], new HashSet<string> { "holds:<30" }, AutonomousTestAction.None));
    }

    [Fact]
    public void ConfidenceStartsFromSourcePriorAndMovesWithEvidence()
    {
        PlaybookEntry coach = Coach("c", PlaybookTier.Tip, []);
        PlaybookEntry lesson = Lesson("l", []);

        Assert.Equal(0.8, coach.Confidence, precision: 3);
        Assert.Equal(0.4, lesson.Confidence, precision: 3);
        Assert.True((lesson with { Successes = 6 }).Confidence > lesson.Confidence);
        Assert.True(PlaybookConfidence.ShouldRetire(lesson with { Applications = 10, Failures = 10 }));
        Assert.False(PlaybookConfidence.ShouldRetire(lesson with { Applications = 10, Failures = 10, CoachEndorsed = true }));
        Assert.True((coach with { Applications = 20, Failures = 20 }).Contested);
    }

    [Fact]
    public async Task StoreRoundTripsEntriesAndRecordsOutcomes()
    {
        using var database = new TemporaryDatabase();
        var store = new SqlitePlaybookStore(database.ConnectionString);
        await store.InitializeAsync();
        await store.UpsertEntryAsync(Lesson("lesson", ["scanner:none"]) with { AgentId = "veteran" });
        await store.UpsertEntryAsync(Coach("global", PlaybookTier.Tip, []) with { AgentId = PlaybookEntry.AllAgents });
        await store.UpsertEntryAsync(Coach("other", PlaybookTier.Tip, []) with { AgentId = "someone-else" });

        Assert.Equal(
            ["global", "lesson"],
            (await store.GetEntriesAsync("veteran")).Select(entry => entry.EntryId).Order(StringComparer.Ordinal));

        PlaybookEntry? helped = await store.RecordOutcomeAsync("lesson", Reward(1.5), overridden: false, BaseTime);
        PlaybookEntry? hurt = await store.RecordOutcomeAsync("lesson", Reward(-2), overridden: false, BaseTime);
        PlaybookEntry? neutral = await store.RecordOutcomeAsync("lesson", Reward(-0.02), overridden: false, BaseTime);
        PlaybookEntry? overridden = await store.RecordOutcomeAsync("lesson", Reward(3), overridden: true, BaseTime);
        PlaybookEntry? unverified = await store.RecordOutcomeAsync(
            "lesson",
            GameplayReward.Unverified("no snapshot"),
            overridden: false,
            BaseTime);

        Assert.NotNull(helped);
        Assert.NotNull(hurt);
        Assert.NotNull(neutral);
        PlaybookEntry final = Assert.IsType<PlaybookEntry>(unverified);
        Assert.Equal(3, final.Applications);
        Assert.Equal(1, final.Successes);
        Assert.Equal(1, final.Failures);
        Assert.Equal(-0.52, final.RewardSum, precision: 4);
        Assert.Equal(1, final.Overrides);
        Assert.Equal(3, final.OverrideRewardSum, precision: 4);
        Assert.Equal(["scanner:none"], final.AppliesWhen);
        Assert.Equal(3, overridden!.Applications);
    }

    [Fact]
    public async Task StoreRetiresFailingLessonsButNotCoachTips()
    {
        using var database = new TemporaryDatabase();
        var store = new SqlitePlaybookStore(database.ConnectionString);
        await store.InitializeAsync();
        await store.UpsertEntryAsync(Lesson("lesson", []));
        await store.UpsertEntryAsync(Coach("tip", PlaybookTier.Tip, []));

        for (int index = 0; index < PlaybookConfidence.MinimumApplicationsForVerdict; index++)
        {
            await store.RecordOutcomeAsync("lesson", Reward(-1), overridden: false, BaseTime);
            await store.RecordOutcomeAsync("tip", Reward(-1), overridden: false, BaseTime);
        }

        Assert.Equal(PlaybookStatus.Retired, (await store.GetEntryAsync("lesson"))!.Status);
        PlaybookEntry tip = (await store.GetEntryAsync("tip"))!;
        Assert.Equal(PlaybookStatus.Active, tip.Status);
        Assert.False(tip.Contested);
        Assert.DoesNotContain(await store.GetEntriesAsync("meridian"), entry => entry.EntryId == "lesson");
    }

    [Fact]
    public async Task StoreRejectsRulesWithoutForbiddenActionAndUnknownTags()
    {
        using var database = new TemporaryDatabase();
        var store = new SqlitePlaybookStore(database.ConnectionString);
        await store.InitializeAsync();

        await Assert.ThrowsAsync<ArgumentException>(() => store.UpsertEntryAsync(Coach("rule", PlaybookTier.Rule, [])));
        await Assert.ThrowsAsync<ArgumentException>(() => store.UpsertEntryAsync(Coach("tip", PlaybookTier.Tip, ["credits:lots"])));
        await Assert.ThrowsAsync<ArgumentException>(() => store.UpsertEntryAsync(
            Lesson("lesson", []) with { Tier = PlaybookTier.Rule, ForbiddenAction = AutonomousTestAction.BuyFighters }));
    }

    [Fact]
    public async Task RecorderCreditsCitedPlaybookEntryAndOverriddenTip()
    {
        using var trajectories = new TemporaryDatabase();
        using var playbook = new TemporaryDatabase();
        var playbookStore = new SqlitePlaybookStore(playbook.ConnectionString);
        await playbookStore.InitializeAsync();
        await playbookStore.UpsertEntryAsync(Lesson("lesson", []));
        await playbookStore.UpsertEntryAsync(Coach("tip", PlaybookTier.Tip, []));
        var trajectoryStore = new SqliteTrajectoryStore(trajectories.ConnectionString);
        var learner = new PlaybookLearner(
            playbookStore,
            trajectoryStore,
            NullLogger<PlaybookLearner>.Instance,
            new PlaybookLearnerOptions { RunInBackground = false });
        var recorder = new TrajectoryRecorder(
            trajectoryStore,
            NullLogger<TrajectoryRecorder>.Instance,
            observers: [learner]);

        await recorder.RecordDecisionAsync(
            "meridian",
            "qwen3:8b",
            Situation(Snapshot(10, credits: 1_000, turns: 100, at: BaseTime)),
            new AutonomousTestActionDecision(AutonomousTestAction.TradeAtCurrentPort, "Trade per lesson.")
            {
                CitedKnowledgeNodeId = PlaybookCitation.ToCitationId("lesson"),
                OverriddenPlaybookEntryId = PlaybookCitation.ToCitationId("tip"),
                OverrideReason = "The tip's port is out of product."
            },
            TrajectoryDecisionSource.Pilot,
            "Earn credits",
            situationTags: ["credits:1k-10k"]);
        recorder.ObserveSnapshot("meridian", Snapshot(10, credits: 2_500, turns: 99, at: BaseTime.AddSeconds(10)));
        await recorder.EndEpisodeAsync("meridian", EpisodeStatus.Stopped, "done");

        PlaybookEntry lesson = (await playbookStore.GetEntryAsync("lesson"))!;
        PlaybookEntry tip = (await playbookStore.GetEntryAsync("tip"))!;
        Assert.Equal(1, lesson.Applications);
        Assert.Equal(1, lesson.Successes);
        Assert.Equal(0, tip.Applications);
        Assert.Equal(1, tip.Overrides);

        TrajectoryEpisode episode = Assert.Single(await trajectoryStore.GetRecentEpisodesAsync("meridian"));
        TrajectoryStep step = Assert.Single(await trajectoryStore.GetStepsAsync(episode.EpisodeId));
        Assert.Equal(PlaybookCitation.ToCitationId("lesson"), step.CitedKnowledgeNodeId);
        Assert.Equal("The tip's port is out of product.", step.OverrideReason);
        Assert.Equal(["credits:1k-10k"], step.SituationTags);

        AgentLifeRecord life = Assert.Single(await playbookStore.GetLivesAsync("meridian"));
        Assert.Equal(1, life.LifeNumber);
        Assert.Equal(episode.EpisodeId, life.EpisodeId);
        Assert.Contains("Life 1 ended Stopped", life.Summary);
    }

    [Fact]
    public async Task LearnerRemembersLivesUnderSharedMemoryAndStoresValidatedLessons()
    {
        using var trajectories = new TemporaryDatabase();
        using var playbook = new TemporaryDatabase();
        var playbookStore = new SqlitePlaybookStore(playbook.ConnectionString);
        await playbookStore.InitializeAsync();
        var trajectoryStore = new SqliteTrajectoryStore(trajectories.ConnectionString);
        var distiller = new ScriptedDistiller();
        var learner = new PlaybookLearner(
            playbookStore,
            trajectoryStore,
            NullLogger<PlaybookLearner>.Instance,
            new PlaybookLearnerOptions
            {
                RunInBackground = false,
                MemoryId = "veteran",
                GameKey = "Game D",
                DistillationModel = new ModelConfiguration("Ollama", "qwen3:8b", new Uri("http://localhost:11434/"))
            },
            distiller);

        // Life 1, played by one learner.
        string firstEpisode = await PlayLifeAsync(trajectoryStore, learner, "learner-a-01");
        Assert.NotNull(distiller.LastRequest);
        const string goodStep = ScriptedDistiller.FirstOfferedStep;
        distiller.Next = new LessonDistillationResult(
            "Early trades paid off.",
            [
                new DistilledLessonProposal("Trade early", "Trade at the first compatible port pair.", "Best reward.",
                    ["scanner:none", "bogus:tag"], PlaybookScope.Universal, [goodStep]),
                new DistilledLessonProposal("Unfounded", "Do something unproven.", "Hunch.", [], PlaybookScope.Universal, ["not-a-step"]),
                new DistilledLessonProposal("Map fact", "Port pair near dock is good.", "Seen.", [], PlaybookScope.Game, [goodStep])
            ]);

        // Life 2, played by a different learner sharing the memory lineage.
        await PlayLifeAsync(trajectoryStore, learner, "learner-a-02");

        IReadOnlyList<AgentLifeRecord> lives = await playbookStore.GetLivesAsync("veteran");
        Assert.Equal([2, 1], lives.Select(life => life.LifeNumber));
        Assert.Equal("learner-a-02", lives[0].PlayerAgentId);
        Assert.Equal(2, lives[0].LessonIdsLearned.Count);
        Assert.Contains("Early trades paid off.", lives[0].Summary);
        Assert.Contains(lives, life => life.EpisodeId == firstEpisode);

        IReadOnlyList<PlaybookEntry> lessons = await playbookStore.GetEntriesAsync("veteran");
        PlaybookEntry universal = Assert.Single(lessons, entry => entry.Title == "Trade early");
        Assert.Equal(PlaybookSource.Distilled, universal.Source);
        Assert.Equal(PlaybookTier.Tip, universal.Tier);
        Assert.Equal(["scanner:none"], universal.AppliesWhen);
        Assert.Equal(2, universal.LearnedInLife);
        PlaybookEntry gameLesson = Assert.Single(lessons, entry => entry.Title == "Map fact");
        Assert.Equal(PlaybookScope.Game, gameLesson.Scope);
        Assert.Equal("Game D", gameLesson.GameKey);
        Assert.DoesNotContain(lessons, entry => entry.Title == "Unfounded");

        // Life 3 restates the lesson: it is reinforced, not duplicated.
        distiller.Next = new LessonDistillationResult(
            string.Empty,
            [
                new DistilledLessonProposal("Trade early again", "trade at the first   compatible port pair.", "Again.",
                    [], PlaybookScope.Universal, [ScriptedDistiller.FirstOfferedStep])
            ]);
        await PlayLifeAsync(trajectoryStore, learner, "learner-a-03");
        lessons = await playbookStore.GetEntriesAsync("veteran");
        Assert.Single(lessons, entry => entry.Guidance.Contains("first", StringComparison.OrdinalIgnoreCase) &&
                                        entry.Scope == PlaybookScope.Universal);
        Assert.Equal(2, lessons.Single(entry => entry.Title == "Trade early").EvidenceEpisodeIds.Count);
        Assert.Equal(3, distiller.LastRequest!.Benchmarks.Single(b => b.Milestone == EpisodeMilestone.ReachedStarDock).LivesTotal);
    }

    [Fact]
    public void ProgressionBenchmarksCompareLives()
    {
        AgentLifeRecord Life(int number, params (EpisodeMilestone Milestone, int Turns)[] milestones) =>
            new($"e{number}", "veteran", null, number, "m", EpisodeStatus.Completed, null, BaseTime, BaseTime, 0, null, null, 1,
                milestones.Select(item => new LifeMilestone(item.Milestone, item.Turns, 0)).ToArray(), string.Empty);

        IReadOnlyList<ProgressionBenchmark> benchmarks = ProgressionBenchmark.Compute(
        [
            Life(1, (EpisodeMilestone.ReachedStarDock, 90)),
            Life(2, (EpisodeMilestone.ReachedStarDock, 40), (EpisodeMilestone.DensityScanner, 300)),
            Life(3, (EpisodeMilestone.ReachedStarDock, 60))
        ]);

        ProgressionBenchmark dock = benchmarks.Single(item => item.Milestone == EpisodeMilestone.ReachedStarDock);
        Assert.Equal(3, dock.LivesReached);
        Assert.Equal(40, dock.BestTurns);
        Assert.Equal(60, dock.MedianTurns);
        Assert.Equal("e2", dock.BestEpisodeId);
        Assert.Equal(1, benchmarks.Single(item => item.Milestone == EpisodeMilestone.DensityScanner).LivesReached);
    }

    [Fact]
    public async Task CoachingServiceStoresTerminalCommandsUnderMemoryLineage()
    {
        using var playbook = new TemporaryDatabase();
        var store = new SqlitePlaybookStore(playbook.ConnectionString);
        await store.InitializeAsync();
        GameSnapshot snapshot = Snapshot(5, credits: 500, turns: 300) with
        {
            DensityScannerInstalled = false,
            HoloScannerInstalled = false,
            ObservedAt = DateTimeOffset.UtcNow
        };
        var service = new PlaybookCoachingService(
            store,
            new PlaybookSettings("veteran", GameKey: null),
            new FixedSituationProvider(Situation(snapshot)));

        CoachingResult tip = await service.ApplyCommandAsync("learner-x", "!coach here : Buy holds before fighters.");
        CoachingResult gameScoped = await service.ApplyCommandAsync("learner-x", "!coach game : Port 12 is great.");
        CoachingResult rule = await service.ApplyCommandAsync(
            "learner-x",
            "!coach rule never worldtrade when scanner:none : World Trade needs a scanner.");

        Assert.True(tip.Succeeded, tip.Message);
        Assert.Equal("veteran", tip.Entry!.AgentId);
        Assert.Contains("credits:<1k", tip.Entry.AppliesWhen);
        Assert.Contains("scanner:none", tip.Entry.AppliesWhen);
        Assert.DoesNotContain(tip.Entry.AppliesWhen, tag => tag.StartsWith("turns:", StringComparison.Ordinal));
        Assert.False(gameScoped.Succeeded);
        Assert.True(rule.Succeeded, rule.Message);
        Assert.Equal(AutonomousTestAction.StartWorldTradeMode, rule.Entry!.ForbiddenAction);
        Assert.Equal(2, (await store.GetEntriesAsync("veteran")).Count);
    }

    [Fact]
    public async Task CoachingServiceAnnotatesDecisionAndNotesReliedOnLesson()
    {
        using var playbook = new TemporaryDatabase();
        var store = new SqlitePlaybookStore(playbook.ConnectionString);
        await store.InitializeAsync();
        await store.UpsertEntryAsync(Lesson("lesson", []));
        var service = new PlaybookCoachingService(store, new PlaybookSettings(null, null));
        var step = new TrajectoryStep(
            "s1", "e1", "meridian", 4, BaseTime, TrajectoryDecisionSource.Pilot,
            nameof(AutonomousTestAction.MoveToAdjacentSector), 12, "Explore.", null, null, null)
        {
            CitedKnowledgeNodeId = PlaybookCitation.ToCitationId("lesson"),
            SituationTags = ["scanner:none", "turns:200+", "location:fedspace"]
        };

        CoachingResult result = await service.AnnotateStepAsync(
            "meridian", step, wasGoodDecision: false, "Never explore blind without a scanner.");

        Assert.True(result.Succeeded, result.Message);
        Assert.Equal(["scanner:none", "location:fedspace"], result.Entry!.AppliesWhen);
        Assert.StartsWith("Poor decision", result.Entry.Rationale, StringComparison.Ordinal);
        Assert.Contains("Never explore blind", (await store.GetEntryAsync("lesson"))!.CoachNote);
    }

    [Fact]
    public void KnowledgeItemsCarryLayerAndTrackRecord()
    {
        AutonomousActionKnowledgeItem rule = PlaybookCitation.ToKnowledgeItem(
            Coach("r", PlaybookTier.Rule, ["holds:<30"]) with { ForbiddenAction = AutonomousTestAction.MoveToAdjacentSector });
        AutonomousActionKnowledgeItem lesson = PlaybookCitation.ToKnowledgeItem(Lesson("l", []) with { Applications = 3, Successes = 2 });

        Assert.Equal("playbook:r", rule.NodeId);
        Assert.Equal(PlaybookCitation.CoachRuleLayer, rule.Layer);
        Assert.Contains(rule.Evidence, line => line.Contains("MoveToAdjacentSector", StringComparison.Ordinal));
        Assert.Equal(PlaybookCitation.LessonLayer, lesson.Layer);
        Assert.Contains(lesson.Evidence, line => line.Contains("relied on 3x, helped 2", StringComparison.Ordinal));
        Assert.True(PlaybookCitation.TryGetEntryId("playbook:abc", out string id));
        Assert.Equal("abc", id);
        Assert.False(PlaybookCitation.TryGetEntryId("node-1", out _));
    }

    [Fact]
    public void TerminalInterceptorCapturesCoachLinesAndPassesEverythingElseThrough()
    {
        var echoed = new StringBuilder();
        var submitted = new List<string>();
        var interceptor = new MTC.CoachInputInterceptor(text => echoed.Append(text), submitted.Add);

        Assert.Equal("D"u8.ToArray(), interceptor.Process("D"u8.ToArray()));
        // "!" is held while it could still become "!coach", then released unchanged.
        Assert.Empty(interceptor.Process("!"u8.ToArray()));
        Assert.Equal("!x"u8.ToArray(), interceptor.Process("x"u8.ToArray()));
        Assert.Equal("hi!"u8.ToArray(), interceptor.Process("hi!!"u8.ToArray()));
        Assert.Equal("!q"u8.ToArray(), interceptor.Process("q"u8.ToArray()));

        Assert.Empty(interceptor.Process("!CoAcH buy holdz"u8.ToArray()));
        Assert.True(interceptor.IsCapturing);
        Assert.Empty(interceptor.Process([0x08, (byte)'s', (byte)'\r']));
        Assert.False(interceptor.IsCapturing);
        Assert.Equal(["!coach buy holds"], submitted);

        Assert.Empty(interceptor.Process("!coach never mind"u8.ToArray()));
        Assert.Empty(interceptor.Process([0x1b]));
        Assert.Single(submitted);
        Assert.Equal("M"u8.ToArray(), interceptor.Process("M"u8.ToArray()));
        Assert.Contains("coach cancelled", echoed.ToString());
    }

    private static async Task<string> PlayLifeAsync(
        SqliteTrajectoryStore trajectoryStore,
        PlaybookLearner learner,
        string agentId)
    {
        var recorder = new TrajectoryRecorder(
            trajectoryStore,
            NullLogger<TrajectoryRecorder>.Instance,
            observers: [learner]);
        DateTimeOffset start = DateTimeOffset.UtcNow.AddMinutes(-5);
        long credits = 1_000;
        int turns = 500;
        for (int index = 0; index < 4; index++)
        {
            await recorder.RecordDecisionAsync(
                agentId,
                "qwen3:1.7b",
                Situation(Snapshot(10 + index, credits, turns, at: start.AddSeconds(index * 10))),
                new AutonomousTestActionDecision(AutonomousTestAction.TradeAtCurrentPort, $"Trade {index}."),
                TrajectoryDecisionSource.Pilot,
                "Earn credits",
                situationTags: ["scanner:none"]);
            credits += index % 2 == 0 ? 1_500 : -500;
            turns -= 3;
            recorder.ObserveSnapshot(agentId, Snapshot(10 + index, credits, turns, at: start.AddSeconds(index * 10 + 5)));
        }
        TrajectoryEpisode episode = recorder.GetActiveEpisode(agentId)!;
        await trajectoryStore.UpsertMilestoneAsync(new TrajectoryMilestone(
            episode.EpisodeId, EpisodeMilestone.ReachedStarDock, start, 1, 1, 6, 2, "test"));
        await recorder.EndEpisodeAsync(agentId, EpisodeStatus.TimedOut, "cap");
        return episode.EpisodeId;
    }

    private sealed class ScriptedDistiller : ILessonDistiller
    {
        public LessonDistillationRequest? LastRequest { get; private set; }
        /// <summary>Placeholder evidence id replaced with the first step actually offered for the life.</summary>
        public const string FirstOfferedStep = "*";

        public LessonDistillationResult Next { get; set; } = new(string.Empty, []);

        public Task<LessonDistillationResult> DistillAsync(
            ModelConfiguration configuration,
            LessonDistillationRequest request,
            CancellationToken cancellationToken = default)
        {
            LastRequest = request;
            LessonDistillationResult result = Next;
            string stepId = request.BestSteps.Concat(request.WorstSteps).First().StepId;
            result = result with
            {
                Lessons = result.Lessons
                    .Select(lesson => lesson with
                    {
                        EvidenceStepIds = lesson.EvidenceStepIds
                            .Select(id => id == FirstOfferedStep ? stepId : id)
                            .ToArray()
                    })
                    .ToArray()
            };
            Next = new LessonDistillationResult(string.Empty, []);
            return Task.FromResult(result);
        }
    }

    private sealed class FixedSituationProvider(GameSituationReport situation) : IGameSituationProvider
    {
        public Task<GameSituationReport> CaptureSituationAsync(string agentId, CancellationToken cancellationToken = default) =>
            Task.FromResult(situation);
    }

    private static PlaybookEntry Coach(string id, PlaybookTier tier, IReadOnlyList<string> appliesWhen) =>
        new(id, "meridian", PlaybookSource.Coach, tier, PlaybookScope.Universal, null, id, $"Guidance {id}", string.Empty,
            appliesWhen, BaseTime)
        {
            UpdatedAt = BaseTime
        };

    private static PlaybookEntry Lesson(string id, IReadOnlyList<string> appliesWhen) =>
        new(id, "meridian", PlaybookSource.Distilled, PlaybookTier.Tip, PlaybookScope.Universal, null, id, $"Guidance {id}",
            string.Empty, appliesWhen, BaseTime)
        {
            UpdatedAt = BaseTime
        };

    private static GameplayReward Reward(double total) =>
        new(true, total, null, null, null, false, false, false, null);

    private static GameSituationReport Situation(GameSnapshot snapshot) =>
        new(
            snapshot.ObservedAt,
            GamePresenceState.Connected,
            GameAuthenticationState.Authenticated,
            GameSynchronizationState.Healthy,
            "Command",
            snapshot.ObservedAt,
            snapshot,
            []);

    private static GameSnapshot Snapshot(int sector, long credits, int turns, DateTimeOffset? at = null)
    {
        DateTimeOffset observedAt = at ?? BaseTime;
        return new GameSnapshot(sector, credits, turns, new Ship("Meridian One", "Meridian", "Merchant Cruiser", sector, 0, observedAt), observedAt)
        {
            HoldsTotal = 20,
            HoldsEmpty = 20,
            FuelOre = 0,
            Organics = 0,
            Equipment = 0
        };
    }

    private sealed class TemporaryDatabase : IDisposable
    {
        private readonly string _path = Path.Combine(Path.GetTempPath(), $"twbot-playbook-{Guid.NewGuid():N}.db");

        public string ConnectionString => new SqliteConnectionStringBuilder
        {
            DataSource = _path,
            ForeignKeys = true,
            Pooling = false
        }.ToString();

        public void Dispose()
        {
            if (File.Exists(_path))
            {
                File.Delete(_path);
            }
        }
    }
}
