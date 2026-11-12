using System.Globalization;
using LLMSurvivingProxyAgent.Agent;
using LLMSurvivingProxyAgent.Core;
using LLMSurvivingProxyAgent.Knowledge;

namespace LLMSurvivingProxyAgent.Goals;

public sealed class StrategicAssessmentValidationException(string message) : Exception(message)
{
}

public sealed class StrategicReviewService(
    IAgentStore agentStore,
    IKnowledgeStore knowledgeStore,
    IMacroTelemetryStore macroTelemetryStore,
    IReactiveEventStore reactiveEventStore,
    IGoalProposalStore goalStore,
    GoalProposalService proposalService,
    IStrategicReviewStore reviewStore,
    IStrategicReasoningProvider reasoningProvider,
    Uri modelEndpoint,
    ModelConfiguration? strategicModel = null,
    ILlmInteractionHistoryStore? interactionHistory = null,
    IPlaybookStore? playbookStore = null,
    string? gameKey = null,
    string? playbookMemoryId = null)
{
    private const int RecentLivesForReview = 5;
    private const int KnowledgeItemsPerLayer = 4;
    private static readonly KnowledgeLayer[] ReviewKnowledgeLayers =
    [
        KnowledgeLayer.Strategic,
        KnowledgeLayer.Tactical,
        KnowledgeLayer.Cooperative,
        KnowledgeLayer.Communication,
        KnowledgeLayer.Procedural,
        KnowledgeLayer.Facts,
        KnowledgeLayer.Structural,
        KnowledgeLayer.Experience
    ];

    public async Task<StrategicReviewRecord> ReviewAsync(
        string agentId,
        GameSnapshot snapshot,
        CancellationToken cancellationToken = default)
    {
        return await ReviewCoreAsync(
            agentId,
            snapshot,
            null,
            "Operator-requested review.",
            null,
            cancellationToken).ConfigureAwait(false);
    }

    public Task<StrategicReviewRecord> ReviewWithModelAsync(
        string agentId,
        GameSnapshot snapshot,
        ModelConfiguration model,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(model);
        return ReviewCoreAsync(
            agentId,
            snapshot,
            model,
            "Autonomous objective review.",
            null,
            cancellationToken);
    }

    public Task<StrategicReviewRecord> ReviewWithModelAsync(
        string agentId,
        GameSnapshot snapshot,
        ModelConfiguration model,
        string reviewReason,
        CancellationToken cancellationToken = default)
        => ReviewWithModelAsync(
            agentId,
            snapshot,
            model,
            reviewReason,
            integratedGameClient: null,
            cancellationToken);

    public Task<StrategicReviewRecord> ReviewWithModelAsync(
        string agentId,
        GameSnapshot snapshot,
        ModelConfiguration model,
        string reviewReason,
        IntegratedGameClientSnapshot? integratedGameClient,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(model);
        ArgumentException.ThrowIfNullOrWhiteSpace(reviewReason);
        if (reviewReason.Length > 500)
        {
            throw new ArgumentException("A strategic review reason cannot exceed 500 characters.", nameof(reviewReason));
        }
        return ReviewCoreAsync(
            agentId,
            snapshot,
            model,
            reviewReason,
            integratedGameClient,
            cancellationToken);
    }

    private async Task<StrategicReviewRecord> ReviewCoreAsync(
        string agentId,
        GameSnapshot snapshot,
        ModelConfiguration? modelOverride,
        string reviewReason,
        IntegratedGameClientSnapshot? integratedGameClient,
        CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(agentId);
        ValidateSnapshot(snapshot);

        var agent = (await agentStore.GetAgentsAsync(cancellationToken))
            .SingleOrDefault(item => item.AgentId == agentId)
            ?? throw new KeyNotFoundException($"Agent '{agentId}' was not found.");
        if (modelOverride is null &&
            strategicModel is null &&
            (string.IsNullOrWhiteSpace(agent.PreferredProvider) ||
             string.IsNullOrWhiteSpace(agent.PreferredModel)))
        {
            throw new InvalidOperationException($"Agent '{agentId}' has no preferred model configured.");
        }

        ModelConfiguration? agentStrategicModel = null;
        if (agent.Configuration.TryGetValue("Brains:Strategic:Model", out string? configuredStrategicModel) &&
            !string.IsNullOrWhiteSpace(configuredStrategicModel))
        {
            string provider = string.IsNullOrWhiteSpace(agent.PreferredProvider)
                ? strategicModel?.Provider ?? reasoningProvider.Id
                : agent.PreferredProvider;
            agentStrategicModel = new ModelConfiguration(
                provider,
                configuredStrategicModel.Trim(),
                modelEndpoint);
        }

        var model = agentStrategicModel ?? modelOverride ?? strategicModel ??
            new ModelConfiguration(agent.PreferredProvider!, agent.PreferredModel!, modelEndpoint);
        if (!string.Equals(model.Provider, reasoningProvider.Id, StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException(
                $"The configured reasoning provider '{model.Provider}' is unavailable; " +
                $"the registered provider is '{reasoningProvider.Id}'.");
        }
        if (modelOverride is not null &&
            (!Uri.TryCreate(model.Endpoint.ToString(), UriKind.Absolute, out var overrideEndpoint) ||
             (overrideEndpoint.Scheme != Uri.UriSchemeHttp && overrideEndpoint.Scheme != Uri.UriSchemeHttps) ||
             !overrideEndpoint.IsLoopback))
        {
            throw new InvalidOperationException(
                "Autonomous strategic reviews require an absolute loopback HTTP or HTTPS model endpoint.");
        }

        var activeObjectives = await goalStore.GetObjectivesAsync(
            agentId,
            ObjectiveStatus.Active,
            limit: 2,
            cancellationToken);
        if (activeObjectives.Count > 1)
        {
            throw new InvalidDataException($"Agent '{agentId}' has more than one active objective.");
        }
        var pendingProposals = await goalStore.GetProposalsAsync(
            agentId,
            GoalProposalStatus.PendingSelection,
            limit: 25,
            cancellationToken);
        var previousReviews = await reviewStore.GetRecentAsync(agentId, limit: 10, cancellationToken);
        var macroActivity = await macroTelemetryStore.GetRecentAsync(agentId, limit: 100, cancellationToken);
        var recentReactiveEvents = await reactiveEventStore.GetRecentAsync(agentId, limit: 100, cancellationToken);
        var query = activeObjectives.SingleOrDefault()?.Goal ?? "TradeWars strategy and objectives";
        var knowledgeNodes = new List<KnowledgeNode>();
        foreach (var layer in ReviewKnowledgeLayers)
        {
            knowledgeNodes.AddRange(await knowledgeStore.SearchLayerForAgentAsync(
                agentId,
                layer == KnowledgeLayer.Experience ? null : query,
                layer,
                limit: KnowledgeItemsPerLayer,
                cancellationToken));
        }
        (IReadOnlyList<ReviewKnowledgeItem> playbookKnowledge, StrategicVeteranMemory? veteranMemory) =
            await LoadVeteranMemoryAsync(agentId, snapshot, cancellationToken).ConfigureAwait(false);
        var activeObjective = activeObjectives.SingleOrDefault();
        var now = DateTimeOffset.UtcNow;
        var failureDetection = StallDetectionService.Evaluate(
            snapshot,
            previousReviews,
            activeObjective?.SourceProposalId,
            activeObjective?.CreatedAt);
        var reviewHistory = ReviewHistoryAnalyzer.Analyze(previousReviews, macroActivity, now);

        var context = new StrategicReviewContext(
            snapshot,
            previousReviews.Reverse().Select(review => review.Context.Snapshot).ToArray(),
            activeObjective is { } active
                ? new ReviewObjective(active.SourceProposalId, active.Goal, active.Rationale, null)
                {
                    ObjectiveId = active.ObjectiveId
                }
                : null,
            pendingProposals.Select(proposal => new ReviewObjective(
                proposal.ProposalId,
                proposal.Goal,
                proposal.Rationale,
                proposal.Confidence)).ToArray(),
            agent.Journal.TakeLast(20)
                .Select(entry => entry with { Message = Truncate(entry.Message, 1_000) })
                .ToArray(),
            failureDetection,
            reviewHistory,
            knowledgeNodes.Select(node => new ReviewKnowledgeItem(
                node.Id,
                node.DocumentId,
                Truncate(node.Title, 300),
                Truncate(node.Summary, 800),
                node.Category.ToString(),
                node.Confidence,
                node.Basis.ToString(),
                node.Evidence.Take(3).Select(evidence =>
                    $"line {evidence.LineNumber}: {FormatKnowledgeEvidence(node, evidence)}").ToArray())
            {
                Layer = KnowledgeLayers.GetLayer(node.Category).ToString()
            }).Concat(playbookKnowledge).ToArray())
        {
            VeteranMemory = veteranMemory,
            AgentId = agentId,
            ReactiveActivity = AnalyzeReactiveActivity(recentReactiveEvents, previousReviews, now),
            ReviewReason = reviewReason,
            IntegratedGameClient = integratedGameClient
        };

        var assessment = await reasoningProvider.AssessAsync(model, context, cancellationToken);
        string outcome;
        try
        {
            ValidateAssessment(assessment, context);
            outcome = await ApplyObjectiveDecisionAsync(
                agentId,
                snapshot,
                assessment,
                knowledgeNodes,
                activeObjective?.ObjectiveId,
                cancellationToken);
        }
        catch (Exception exception)
        {
            if (assessment.InteractionId is { Length: > 0 } interactionId)
            {
                interactionHistory?.RecordAppliedOutcome(
                    interactionId,
                    $"Assessment was not applied: {exception.Message}");
            }

            throw;
        }
        if (assessment.InteractionId is { Length: > 0 } appliedInteractionId)
        {
            interactionHistory?.RecordAppliedOutcome(appliedInteractionId, outcome);
        }
        var record = new StrategicReviewRecord(
            Guid.NewGuid().ToString("N"),
            agentId,
            now,
            model,
            context,
            assessment,
            outcome);
        await reviewStore.SaveAsync(record, cancellationToken);
        await agentStore.AppendJournalEntryAsync(
            agentId,
            new AgentJournalEntry(
                record.ReviewedAt,
                "StrategicAssessment",
                $"{assessment.Progress}: {assessment.Summary} Recommendation: {assessment.Recommendation} " +
                $"Objective decision: {outcome}"),
            cancellationToken);
        return record;
    }

    private async Task<string> ApplyObjectiveDecisionAsync(
        string agentId,
        GameSnapshot snapshot,
        StrategicAssessment assessment,
        IReadOnlyList<KnowledgeNode> knowledgeNodes,
        string? activeObjectiveId,
        CancellationToken cancellationToken)
    {
        switch (assessment.ObjectiveDecision)
        {
            case ObjectiveDecision.Continue:
                return "Continued the current objective; no objective transition was needed.";
            case ObjectiveDecision.Hold:
                return "Held objective state unchanged.";
            case ObjectiveDecision.SelectProposal:
            {
                var selected = await proposalService.SelectProposalAsync(
                    agentId,
                    assessment.SelectedProposalId!,
                    assessment.Recommendation,
                    cancellationToken);
                return $"Selected objective '{selected.ObjectiveId}' from proposal '{selected.SourceProposalId}'.";
            }
            case ObjectiveDecision.ProposeAndSelect:
            {
                var proposedGoal = assessment.NewObjective!;
                var citedNodes = knowledgeNodes
                    .Where(node => proposedGoal.SupportingKnowledgeNodeIds.Contains(node.Id, StringComparer.Ordinal))
                    .ToDictionary(node => node.Id, StringComparer.Ordinal);
                var evidence = proposedGoal.SupportingKnowledgeNodeIds
                    .Distinct(StringComparer.Ordinal)
                    .Select(id => new GoalEvidence(citedNodes[id], []))
                    .ToArray();
                var proposal = await proposalService.ProposeAsync(new GoalProposalDraft(
                    agentId,
                    proposedGoal.Goal,
                    proposedGoal.Rationale,
                    proposedGoal.Confidence,
                    proposedGoal.Assumptions,
                    snapshot,
                    evidence), cancellationToken);
                var objective = await proposalService.SelectProposalAsync(
                    agentId,
                    proposal.ProposalId,
                    assessment.Recommendation,
                    cancellationToken);
                return $"Created proposal '{proposal.ProposalId}' and selected objective '{objective.ObjectiveId}'.";
            }
            case ObjectiveDecision.Complete:
            {
                if (activeObjectiveId is null)
                {
                    throw new InvalidDataException("A completed objective could not be identified.");
                }

                var completed = await proposalService.CompleteObjectiveAsync(
                    agentId,
                    activeObjectiveId,
                    cancellationToken);
                return $"Completed objective '{completed.ObjectiveId}'.";
            }
            default:
                throw new InvalidDataException("The reasoning provider returned an unsupported objective decision.");
        }
    }

    private static ReactiveActivityAnalysis AnalyzeReactiveActivity(
        IReadOnlyList<ReactiveEventRecord> records,
        IReadOnlyList<StrategicReviewRecord> previousReviews,
        DateTimeOffset now)
    {
        var activityWindowStart = previousReviews
            .OrderByDescending(review => review.ReviewedAt)
            .FirstOrDefault()?.ReviewedAt;
        var activity = records
            .Where(record =>
                record.ProcessedAt <= now &&
                (activityWindowStart is null || record.ProcessedAt > activityWindowStart))
            .OrderByDescending(record => record.ProcessedAt)
            .ToArray();
        var mayBeTruncated = records.Count == 100;
        var limitations = activityWindowStart is null
            ? "No prior review defines an activity window. Counts summarize up to the 100 most recent stored reactive events."
            : "Counts cover events since the prior review among up to the 100 most recent stored events; a full batch may be incomplete.";

        return new ReactiveActivityAnalysis(
            activityWindowStart,
            activity.Length,
            activity.GroupBy(record => record.Event.Type.ToString(), StringComparer.Ordinal)
                .OrderByDescending(group => group.Count())
                .ThenBy(group => group.Key, StringComparer.Ordinal)
                .Select(group => new ReactiveEventCount(group.Key, group.Count()))
                .ToArray(),
            activity.GroupBy(record => record.Decision.Mode.ToString(), StringComparer.Ordinal)
                .OrderByDescending(group => group.Count())
                .ThenBy(group => group.Key, StringComparer.Ordinal)
                .Select(group => new ReactiveEventCount(group.Key, group.Count()))
                .ToArray(),
            activity.Take(20)
                .Select(record => new ReactiveReviewEventSummary(
                    record.Event.EventId,
                    record.Event.Type,
                    record.Event.Source,
                    record.Event.OccurredAt,
                    record.Event.SectorId,
                    record.Event.ActorName is null ? null : Truncate(record.Event.ActorName, 200),
                    record.Event.SenderTrustAtReceipt,
                    record.Event.Message is null ? null : Truncate(record.Event.Message, 500),
                    record.Decision.Mode,
                    record.Decision.RuleId,
                    Truncate(record.Decision.Summary, 500),
                    record.Decision.RequestsStrategicReview))
                .ToArray(),
            mayBeTruncated,
            limitations);
    }

    private static void ValidateSnapshot(GameSnapshot snapshot)
    {
        if (!GameSnapshotValidation.IsValid(snapshot))
        {
            throw new ArgumentException(
                "The review snapshot contains invalid or future-dated values.",
                nameof(snapshot));
        }
    }

    private static void ValidateAssessment(
        StrategicAssessment assessment,
        StrategicReviewContext context)
    {
        if (assessment is null ||
            !Enum.IsDefined(assessment.Progress) ||
            assessment.Progress == ProgressAssessment.Unknown ||
            !Enum.IsDefined(assessment.ObjectiveDecision) ||
            assessment.ObjectiveDecision == ObjectiveDecision.Unspecified ||
            string.IsNullOrWhiteSpace(assessment.Summary) ||
            assessment.Summary.Length > 4_000 ||
            string.IsNullOrWhiteSpace(assessment.Recommendation) ||
            assessment.Recommendation.Length > 2_000)
        {
            throw new StrategicAssessmentValidationException(
                "The reasoning provider returned an incomplete or invalid assessment.");
        }
        ValidateList(assessment.EvidenceForProgress, "progress evidence", 20);
        ValidateList(assessment.EvidenceAgainstProgress, "counter-evidence", 20);
        ValidateList(assessment.StuckSignals, "stuck signals", 20);
        ValidateList(assessment.UncertainAssumptions, "assumptions", 20);
        ValidateList(assessment.OverlookedOpportunities, "opportunities", 20);
        ValidateList(assessment.AlternativeObjectives, "alternative objectives", 20);
        if (assessment.PlannedScripts is null ||
            assessment.PlannedScripts.Count > 4 ||
            assessment.PlannedScripts.Any(plan =>
                plan is null ||
                !Enum.IsDefined(plan.Script) ||
                string.IsNullOrWhiteSpace(plan.Purpose) ||
                plan.Purpose.Length > 500) ||
            assessment.PlannedScripts.Select(plan => plan.Script).Distinct().Count() !=
                assessment.PlannedScripts.Count)
        {
            throw new StrategicAssessmentValidationException(
                "The assessment returned an invalid or duplicate approved script plan.");
        }
        if (assessment.CitedKnowledgeNodeIds is null || assessment.CitedKnowledgeNodeIds.Count > 50)
        {
            throw new StrategicAssessmentValidationException("The assessment has an invalid knowledge citation list.");
        }
        var availableKnowledgeIds = context.Knowledge.Select(item => item.NodeId).ToHashSet(StringComparer.Ordinal);
        if (assessment.CitedKnowledgeNodeIds.Any(id => !availableKnowledgeIds.Contains(id)))
        {
            throw new StrategicAssessmentValidationException(
                "The assessment cited knowledge that was not supplied to the reasoning provider.");
        }

        switch (assessment.ObjectiveDecision)
        {
            case ObjectiveDecision.Continue when context.ActiveObjective is null:
                throw new StrategicAssessmentValidationException(
                    "The assessment chose to continue, but no active objective exists.");
            case ObjectiveDecision.Complete when
                context.ActiveObjective is null ||
                string.IsNullOrWhiteSpace(context.ActiveObjective.ObjectiveId):
                throw new StrategicAssessmentValidationException(
                    "The assessment completed an objective that is not active.");
            case ObjectiveDecision.SelectProposal:
                if (string.IsNullOrWhiteSpace(assessment.SelectedProposalId) ||
                    !context.PendingObjectives.Any(item => item.ProposalId == assessment.SelectedProposalId) ||
                    assessment.NewObjective is not null)
                {
                    throw new StrategicAssessmentValidationException(
                        "The assessment selected a proposal that was not supplied as pending.");
                }
                break;
            case ObjectiveDecision.ProposeAndSelect:
                if (assessment.SelectedProposalId is not null)
                {
                    throw new StrategicAssessmentValidationException(
                        "The assessment included objective data inconsistent with its decision.");
                }
                ValidateNewObjective(assessment.NewObjective, availableKnowledgeIds);
                break;
            default:
                if (assessment.SelectedProposalId is not null || assessment.NewObjective is not null)
                {
                    throw new StrategicAssessmentValidationException(
                        "The assessment included objective data inconsistent with its decision.");
                }
                break;
        }
    }

    private static void ValidateNewObjective(
        NewObjectiveProposal? proposal,
        IReadOnlySet<string> availableKnowledgeIds)
    {
        if (proposal is null ||
            string.IsNullOrWhiteSpace(proposal.Goal) ||
            proposal.Goal.Length > 200 ||
            string.IsNullOrWhiteSpace(proposal.Rationale) ||
            proposal.Rationale.Length > 2_000 ||
            proposal.Confidence is < 0 or > 1 ||
            proposal.Assumptions is null ||
            proposal.Assumptions.Count > 20 ||
            proposal.Assumptions.Any(string.IsNullOrWhiteSpace) ||
            proposal.Assumptions.Any(item => item.Length > 500) ||
            proposal.SupportingKnowledgeNodeIds is null ||
            proposal.SupportingKnowledgeNodeIds.Count > 50 ||
            proposal.SupportingKnowledgeNodeIds.Any(id => !availableKnowledgeIds.Contains(id)))
        {
            throw new StrategicAssessmentValidationException(
                "The reasoning provider returned an invalid new-objective proposal.");
        }
    }

    private static void ValidateList(IReadOnlyList<string> values, string name, int maximum)
    {
        if (values is null || values.Count > maximum ||
            values.Any(value => string.IsNullOrWhiteSpace(value) || value.Length > 1_000))
        {
            throw new StrategicAssessmentValidationException(
                $"The reasoning provider returned an invalid {name} list.");
        }
    }

    private async Task<(IReadOnlyList<ReviewKnowledgeItem> Knowledge, StrategicVeteranMemory? Memory)> LoadVeteranMemoryAsync(
        string agentId,
        GameSnapshot snapshot,
        CancellationToken cancellationToken)
    {
        if (playbookStore is null)
        {
            return ([], null);
        }

        string memoryId = PlaybookMemory.Resolve(playbookMemoryId, agentId);
        IReadOnlySet<string> situationTags = PlaybookSituation.Describe(snapshot);
        IReadOnlyList<PlaybookEntry> entries = await playbookStore
            .GetEntriesAsync(memoryId, includeRetired: false, cancellationToken)
            .ConfigureAwait(false);
        PlaybookGuidance guidance = PlaybookGuidanceProvider.Select(entries, gameKey, situationTags);
        IReadOnlyList<AgentLifeRecord> lives = await playbookStore
            .GetLivesAsync(memoryId, 200, cancellationToken)
            .ConfigureAwait(false);
        var knowledge = guidance.All
            .Select(PlaybookCitation.ToKnowledgeItem)
            .Select(item => new ReviewKnowledgeItem(
                item.NodeId,
                "playbook",
                item.Title,
                item.Summary,
                item.Layer,
                item.Confidence,
                item.ExtractionBasis,
                item.Evidence)
            {
                Layer = item.Layer
            })
            .ToArray();
        StrategicVeteranMemory? memory = lives.Count == 0 && knowledge.Length == 0
            ? null
            : new StrategicVeteranMemory(
                [.. situationTags],
                lives.Count,
                ProgressionBenchmark.Compute(lives),
                lives.Take(RecentLivesForReview)
                    .Select(life => new StrategicLifeBrief(
                        life.LifeNumber,
                        life.Status,
                        life.TurnsUsed,
                        life.NetWorthDelta,
                        life.Milestones,
                        Truncate(life.Summary, 1_200)))
                    .ToArray());
        return (knowledge, memory);
    }

    private static string Truncate(string value, int maximum) =>
        value.Length <= maximum ? value : value[..maximum];

    private static string FormatKnowledgeEvidence(KnowledgeNode node, KnowledgeEvidence evidence)
    {
        var text = Truncate(evidence.Text, 400);
        if (node.Category != KnowledgeCategory.AgentExperience ||
            evidence.Details?.AgentExperience is not { } experience)
        {
            return text;
        }

        return Truncate(
            $"{text} Before snapshot: {FormatSnapshot(experience.BeforeSnapshot)}. " +
            $"After snapshot: {FormatSnapshot(experience.AfterSnapshot)}.",
            1_000);
    }

    private static string FormatSnapshot(GameSnapshot? snapshot)
    {
        if (snapshot is null)
        {
            return "not captured";
        }

        var ship = snapshot.Ship is { } observedShip
            ? $"{Truncate(observedShip.ShipType, 100)} at sector {observedShip.SectorId}, " +
              $"{observedShip.Fighters} fighters"
            : "unknown";
        return $"observedAt={snapshot.ObservedAt.ToString("O", CultureInfo.InvariantCulture)}; " +
            $"sector={FormatValue(snapshot.CurrentSector)}; " +
            $"synchronizedCredits={FormatValue(snapshot.SynchronizedCredits)}; " +
            $"turns={FormatValue(snapshot.Turns)}; ship={ship}";
    }

    private static string FormatValue<T>(T? value) where T : struct =>
        Convert.ToString(value, CultureInfo.InvariantCulture) ?? "unknown";
}
