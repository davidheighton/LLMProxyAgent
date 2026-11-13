using System.Globalization;

namespace LLMSurvivingProxyAgent.Core;

/// <summary>Who authored a playbook entry.</summary>
public enum PlaybookSource
{
    /// <summary>A human coach (veteran player) wrote it.</summary>
    Coach,

    /// <summary>The agent distilled it from its own past lives.</summary>
    Distilled
}

/// <summary>How binding a playbook entry is.</summary>
public enum PlaybookTier
{
    /// <summary>Strong advice. The agent may deviate only with a stated, logged reason.</summary>
    Tip,

    /// <summary>A hard constraint enforced by the worker before any action is sent to the game.</summary>
    Rule
}

/// <summary>Whether an entry is portable veteran knowledge or specific to one game universe.</summary>
public enum PlaybookScope
{
    Universal,
    Game
}

public enum PlaybookStatus
{
    Active,
    Retired
}

/// <summary>
/// One unit of persistent playing knowledge: a coach tip or rule, or a lesson the agent learned.
/// Entries belong to an agent identity, not a game character, so they survive across lives.
/// </summary>
public sealed record PlaybookEntry(
    string EntryId,
    string AgentId,
    PlaybookSource Source,
    PlaybookTier Tier,
    PlaybookScope Scope,
    string? GameKey,
    string Title,
    string Guidance,
    string Rationale,
    IReadOnlyList<string> AppliesWhen,
    DateTimeOffset CreatedAt)
{
    /// <summary>Agent id that makes an entry apply to every agent.</summary>
    public const string AllAgents = "*";

    public PlaybookStatus Status { get; init; } = PlaybookStatus.Active;

    /// <summary>For rules: the action the worker refuses while <see cref="AppliesWhen"/> matches.</summary>
    public AutonomousTestAction? ForbiddenAction { get; init; }

    /// <summary>The coach reviewed and endorsed an agent-distilled lesson.</summary>
    public bool CoachEndorsed { get; init; }

    public string? CoachNote { get; init; }
    public DateTimeOffset UpdatedAt { get; init; }
    public int Applications { get; init; }
    public int Successes { get; init; }
    public int Failures { get; init; }
    public double RewardSum { get; init; }
    public DateTimeOffset? LastAppliedAt { get; init; }
    public int Overrides { get; init; }
    public double OverrideRewardSum { get; init; }
    public int? LearnedInLife { get; init; }
    public IReadOnlyList<string> EvidenceEpisodeIds { get; init; } = [];

    public double Confidence => PlaybookConfidence.Compute(this);

    /// <summary>Evidence from play disagrees with a coach entry; surfaced for the coach to review.</summary>
    public bool Contested => PlaybookConfidence.IsContested(this);

    /// <summary>Stable id used when an entry is offered to a model as a citable knowledge item.</summary>
    public string CitationId => PlaybookCitation.ToCitationId(EntryId);
}

/// <summary>Host-level playbook configuration: the shared memory lineage (if any) and the current game key.</summary>
public sealed record PlaybookSettings(string? MemoryId, string? GameKey)
{
    public string ResolveMemoryId(string agentId) => PlaybookMemory.Resolve(MemoryId, agentId);
}

/// <summary>Resolves which cross-life memory an agent reads and writes.</summary>
public static class PlaybookMemory
{
    public static string Resolve(string? configuredMemoryId, string agentId)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(agentId);
        return string.IsNullOrWhiteSpace(configuredMemoryId) ? agentId : configuredMemoryId.Trim();
    }

    public static bool IsValidMemoryId(string? value) =>
        !string.IsNullOrWhiteSpace(value) &&
        value.Length <= 64 &&
        value.All(character => char.IsAsciiLetterOrDigit(character) || character is '-' or '_' or '.');
}

public static class PlaybookCitation
{
    public const string Prefix = "playbook:";

    /// <summary>Knowledge-item layer labels used when playbook entries are offered to a model.</summary>
    public const string CoachRuleLayer = "CoachRule";
    public const string CoachTipLayer = "CoachTip";
    public const string LessonLayer = "Lesson";

    public static string LayerFor(PlaybookEntry entry) => entry switch
    {
        { Tier: PlaybookTier.Rule } => CoachRuleLayer,
        { Source: PlaybookSource.Coach } => CoachTipLayer,
        _ => LessonLayer
    };

    /// <summary>Presents an entry as a citable knowledge item, with its track record as evidence.</summary>
    public static AutonomousActionKnowledgeItem ToKnowledgeItem(PlaybookEntry entry)
    {
        ArgumentNullException.ThrowIfNull(entry);
        var evidence = new List<string>
        {
            $"applies when: {PlaybookSituation.Format(entry.AppliesWhen)}"
        };
        if (entry.ForbiddenAction is { } forbidden)
        {
            evidence.Add($"forbidden action: {forbidden}");
        }
        if (!string.IsNullOrWhiteSpace(entry.Rationale))
        {
            evidence.Add($"why: {Truncate(entry.Rationale, 300)}");
        }
        evidence.Add(FormattableString.Invariant(
                $"track record: relied on {entry.Applications}x, helped {entry.Successes}, hurt {entry.Failures}, overridden {entry.Overrides}x") +
            (entry.LearnedInLife is { } life ? FormattableString.Invariant($"; learned in life {life}") : string.Empty) +
            (entry.CoachEndorsed ? "; endorsed by coach" : string.Empty));
        return new AutonomousActionKnowledgeItem(
            entry.CitationId,
            LayerFor(entry),
            Truncate(entry.Title, 300),
            Truncate(entry.Guidance, 800),
            (decimal)entry.Confidence,
            entry.Source.ToString(),
            evidence);
    }

    private static string Truncate(string value, int maximumLength) =>
        value.Length <= maximumLength ? value : value[..maximumLength];

    public static string ToCitationId(string entryId) => Prefix + entryId;

    public static bool TryGetEntryId(string? citationId, out string entryId)
    {
        if (!string.IsNullOrWhiteSpace(citationId) &&
            citationId.StartsWith(Prefix, StringComparison.Ordinal) &&
            citationId.Length > Prefix.Length)
        {
            entryId = citationId[Prefix.Length..];
            return true;
        }
        entryId = string.Empty;
        return false;
    }
}

/// <summary>
/// Beta-style confidence: a source-dependent prior, updated by verified outcomes when the entry was relied on.
/// Coach entries start high; distilled lessons start low and must earn trust in play.
/// </summary>
public static class PlaybookConfidence
{
    public const int MinimumApplicationsForVerdict = 8;
    public const double RetireBelow = 0.25;
    public const double ContestedBelow = 0.4;

    /// <summary>Verified step rewards above this count as the entry helping.</summary>
    public const double SuccessRewardThreshold = 0.01;

    /// <summary>Verified step rewards below this count as the entry hurting. Ordinary move costs stay neutral.</summary>
    public const double FailureRewardThreshold = -0.05;

    public static (double Alpha, double Beta) Prior(PlaybookEntry entry) => entry switch
    {
        { Source: PlaybookSource.Coach } => (8, 2),
        { CoachEndorsed: true } => (8, 3),
        _ => (2, 3)
    };

    public static double Compute(PlaybookEntry entry)
    {
        (double alpha, double beta) = Prior(entry);
        return Math.Round(
            (alpha + entry.Successes) / (alpha + beta + entry.Successes + entry.Failures),
            3);
    }

    public static bool IsContested(PlaybookEntry entry) =>
        entry.Source == PlaybookSource.Coach &&
        entry.Successes + entry.Failures >= MinimumApplicationsForVerdict &&
        Compute(entry) < ContestedBelow;

    public static bool ShouldRetire(PlaybookEntry entry) =>
        entry is { Source: PlaybookSource.Distilled, CoachEndorsed: false } &&
        entry.Successes + entry.Failures >= MinimumApplicationsForVerdict &&
        Compute(entry) < RetireBelow;

    public static int Classify(GameplayReward reward) =>
        !reward.Verified ? 0
        : reward.Total > SuccessRewardThreshold ? 1
        : reward.Total < FailureRewardThreshold ? -1
        : 0;
}

/// <summary>
/// Turns a synchronized snapshot into coarse, universe-independent situation tags. Tags let lessons
/// learned in one game apply in another (sector numbers differ; "no scanner, under 1k credits" does not).
/// Entries match when every tag in <see cref="PlaybookEntry.AppliesWhen"/> is present; a leading '!' negates.
/// </summary>
public static class PlaybookSituation
{
    public static readonly IReadOnlyList<string> Vocabulary =
    [
        "credits:<1k", "credits:1k-10k", "credits:10k-100k", "credits:100k-1m", "credits:1m+",
        "holds:<30", "holds:30-59", "holds:60-99", "holds:100+",
        "turns:<50", "turns:50-199", "turns:200+",
        "scanner:none", "scanner:density", "scanner:holo",
        "cargo:empty", "cargo:loaded",
        "fighters:none", "fighters:some",
        "ship:escape-pod",
        "location:stardock", "location:fedspace", "location:port",
        "worldtrade:running"
    ];

    public sealed record Extras(
        int? StarDockSector = null,
        bool? FederationSpace = null,
        bool AtPort = false,
        bool WorldTradeRunning = false);

    public static IReadOnlySet<string> Describe(GameSnapshot? snapshot, Extras? extras = null)
    {
        var tags = new SortedSet<string>(StringComparer.Ordinal);
        if (snapshot is null)
        {
            return tags;
        }

        if (snapshot.SynchronizedCredits is { } credits)
        {
            tags.Add(credits switch
            {
                < 1_000 => "credits:<1k",
                < 10_000 => "credits:1k-10k",
                < 100_000 => "credits:10k-100k",
                < 1_000_000 => "credits:100k-1m",
                _ => "credits:1m+"
            });
        }
        if (snapshot.HoldsTotal is { } holds)
        {
            tags.Add(holds switch
            {
                < 30 => "holds:<30",
                < 60 => "holds:30-59",
                < 100 => "holds:60-99",
                _ => "holds:100+"
            });
        }
        if (snapshot.Turns is { } turns)
        {
            tags.Add(turns switch
            {
                < 50 => "turns:<50",
                < 200 => "turns:50-199",
                _ => "turns:200+"
            });
        }
        if (snapshot.HoloScannerInstalled is true)
        {
            tags.Add("scanner:holo");
        }
        else if (snapshot.DensityScannerInstalled is true)
        {
            tags.Add("scanner:density");
        }
        else if (snapshot.HoloScannerInstalled is false && snapshot.DensityScannerInstalled is false)
        {
            tags.Add("scanner:none");
        }
        if (snapshot.FuelOre is { } ore && snapshot.Organics is { } organics && snapshot.Equipment is { } equipment)
        {
            tags.Add(ore + organics + equipment > 0 ? "cargo:loaded" : "cargo:empty");
        }
        if (snapshot.Ship is { } ship)
        {
            tags.Add(ship.Fighters > 0 ? "fighters:some" : "fighters:none");
        }
        if (GameplayRewardCalculator.IsEscapePod(snapshot))
        {
            tags.Add("ship:escape-pod");
        }
        if (extras is not null)
        {
            if (extras.StarDockSector is > 0 and var dock && snapshot.CurrentSector == dock)
            {
                tags.Add("location:stardock");
            }
            if (extras.FederationSpace is true)
            {
                tags.Add("location:fedspace");
            }
            if (extras.AtPort)
            {
                tags.Add("location:port");
            }
            if (extras.WorldTradeRunning)
            {
                tags.Add("worldtrade:running");
            }
        }
        return tags;
    }

    public static bool Matches(IReadOnlyList<string> appliesWhen, IReadOnlySet<string> situation)
    {
        foreach (string condition in appliesWhen)
        {
            bool negated = condition.StartsWith('!');
            string tag = negated ? condition[1..] : condition;
            if (situation.Contains(tag) == negated)
            {
                return false;
            }
        }
        return true;
    }

    /// <summary>Normalizes and validates tag conditions; returns the unknown ones in <paramref name="invalid"/>.</summary>
    public static IReadOnlyList<string> Normalize(IEnumerable<string> conditions, out IReadOnlyList<string> invalid)
    {
        var valid = new List<string>();
        var rejected = new List<string>();
        foreach (string raw in conditions)
        {
            string condition = raw.Trim().ToLowerInvariant();
            if (condition.Length == 0)
            {
                continue;
            }
            string tag = condition.TrimStart('!');
            if (Vocabulary.Contains(tag, StringComparer.Ordinal) && condition.Length - tag.Length <= 1)
            {
                if (!valid.Contains(condition, StringComparer.Ordinal))
                {
                    valid.Add(condition);
                }
            }
            else
            {
                rejected.Add(raw.Trim());
            }
        }
        invalid = rejected;
        return valid;
    }

    public static string Format(IEnumerable<string> tags) =>
        string.Join(", ", tags.DefaultIfEmpty("any situation"));
}

/// <summary>The playbook slice selected for one decision.</summary>
public sealed record PlaybookGuidance(
    IReadOnlySet<string> SituationTags,
    IReadOnlyList<PlaybookEntry> Rules,
    IReadOnlyList<PlaybookEntry> Tips,
    IReadOnlyList<PlaybookEntry> Lessons)
{
    public static PlaybookGuidance Empty { get; } =
        new(new HashSet<string>(), [], [], []);

    public IEnumerable<PlaybookEntry> All => Rules.Concat(Tips).Concat(Lessons);
}

/// <summary>A completed life (episode) as remembered by the agent across characters and games.</summary>
public sealed record AgentLifeRecord(
    string EpisodeId,
    string AgentId,
    string? GameKey,
    int LifeNumber,
    string Model,
    EpisodeStatus Status,
    string? EndReason,
    DateTimeOffset StartedAt,
    DateTimeOffset EndedAt,
    double TotalReward,
    long? NetWorthDelta,
    int? TurnsUsed,
    int StepCount,
    IReadOnlyList<LifeMilestone> Milestones,
    string Summary)
{
    public IReadOnlyList<string> LessonIdsLearned { get; init; } = [];

    /// <summary>The agent that lived this life; <see cref="AgentId"/> is the memory lineage it belongs to.</summary>
    public string? PlayerAgentId { get; init; }
}

public sealed record LifeMilestone(EpisodeMilestone Milestone, int? TurnsUsed, int StepIndex);

/// <summary>
/// How quickly past lives reached a milestone. Comparing lives is how the agent discovers an efficient
/// early-game progression without a hard-coded curriculum.
/// </summary>
public sealed record ProgressionBenchmark(
    EpisodeMilestone Milestone,
    int LivesReached,
    int LivesTotal,
    int? BestTurns,
    int? MedianTurns,
    string? BestEpisodeId)
{
    public static IReadOnlyList<ProgressionBenchmark> Compute(IReadOnlyList<AgentLifeRecord> lives)
    {
        ArgumentNullException.ThrowIfNull(lives);
        var benchmarks = new List<ProgressionBenchmark>();
        foreach (EpisodeMilestone milestone in Enum.GetValues<EpisodeMilestone>())
        {
            var reached = lives
                .Select(life => (life, hit: life.Milestones.FirstOrDefault(item => item.Milestone == milestone)))
                .Where(pair => pair.hit is not null)
                .ToArray();
            if (reached.Length == 0)
            {
                continue;
            }

            var timed = reached
                .Where(pair => pair.hit!.TurnsUsed is not null)
                .OrderBy(pair => pair.hit!.TurnsUsed)
                .ToArray();
            int? median = timed.Length == 0 ? null : timed[(timed.Length - 1) / 2].hit!.TurnsUsed;
            benchmarks.Add(new ProgressionBenchmark(
                milestone,
                reached.Length,
                lives.Count,
                timed.FirstOrDefault().hit?.TurnsUsed,
                median,
                timed.FirstOrDefault().life?.EpisodeId ?? reached[0].life.EpisodeId));
        }
        return benchmarks;
    }

    public string Describe() =>
        $"{Milestone}: reached in {LivesReached}/{LivesTotal} lives" +
        (BestTurns is { } best
            ? $", best {best.ToString(CultureInfo.InvariantCulture)} turns, median {MedianTurns?.ToString(CultureInfo.InvariantCulture) ?? "?"} turns"
            : string.Empty);
}

/// <summary>Persistent, cross-life memory: playbook entries, life records, and override history.</summary>
public interface IPlaybookStore
{
    Task InitializeAsync(CancellationToken cancellationToken = default);
    Task UpsertEntryAsync(PlaybookEntry entry, CancellationToken cancellationToken = default);
    Task<PlaybookEntry?> GetEntryAsync(string entryId, CancellationToken cancellationToken = default);

    /// <summary>Entries owned by <paramref name="agentId"/> plus entries for all agents.</summary>
    Task<IReadOnlyList<PlaybookEntry>> GetEntriesAsync(
        string agentId,
        bool includeRetired = false,
        CancellationToken cancellationToken = default);

    /// <summary>Atomically records one verified outcome for an entry the agent relied on (or overrode).</summary>
    Task<PlaybookEntry?> RecordOutcomeAsync(
        string entryId,
        GameplayReward reward,
        bool overridden,
        DateTimeOffset observedAt,
        CancellationToken cancellationToken = default);

    Task UpsertLifeAsync(AgentLifeRecord life, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<AgentLifeRecord>> GetLivesAsync(
        string agentId,
        int limit = 50,
        CancellationToken cancellationToken = default);
}

/// <summary>Selects the playbook entries relevant to the current situation.</summary>
public interface IPlaybookGuidanceProvider
{
    Task<PlaybookGuidance> GetGuidanceAsync(
        string agentId,
        string? gameKey,
        IReadOnlySet<string> situationTags,
        CancellationToken cancellationToken = default);
}

/// <summary>Refuses actions forbidden by an applicable coach rule.</summary>
public static class PlaybookRuleGate
{
    public static PlaybookEntry? FindViolation(
        IEnumerable<PlaybookEntry> rules,
        IReadOnlySet<string> situationTags,
        AutonomousTestAction action)
    {
        ArgumentNullException.ThrowIfNull(rules);
        ArgumentNullException.ThrowIfNull(situationTags);
        if (action == AutonomousTestAction.None)
        {
            return null;
        }

        return rules.FirstOrDefault(rule =>
            rule is { Tier: PlaybookTier.Rule, Status: PlaybookStatus.Active } &&
            rule.ForbiddenAction == action &&
            PlaybookSituation.Matches(rule.AppliesWhen, situationTags));
    }
}

/// <summary>A decision from a finished life, offered to the distiller as evidence.</summary>
public sealed record LessonEvidenceStep(
    string StepId,
    int StepIndex,
    string Source,
    string Action,
    string Rationale,
    string? Objective,
    IReadOnlyList<string> SituationTags,
    double Reward,
    long? CreditsDelta,
    int? TurnsUsed,
    string? CitedEntryId);

public sealed record LessonDistillationRequest(
    string AgentId,
    AgentLifeRecord Life,
    IReadOnlyList<LessonEvidenceStep> BestSteps,
    IReadOnlyList<LessonEvidenceStep> WorstSteps,
    IReadOnlyList<ProgressionBenchmark> Benchmarks,
    IReadOnlyList<AgentLifeRecord> PreviousLives,
    IReadOnlyList<PlaybookEntry> ExistingEntries);

/// <summary>A lesson proposed by the distiller; validated before it is stored.</summary>
public sealed record DistilledLessonProposal(
    string Title,
    string Guidance,
    string Rationale,
    IReadOnlyList<string> AppliesWhen,
    PlaybookScope Scope,
    IReadOnlyList<string> EvidenceStepIds)
{
    /// <summary>When set, the proposal restates an existing entry, which is reinforced instead of duplicated.</summary>
    public string? ReinforcesEntryId { get; init; }
}

public sealed record LessonDistillationResult(
    string LifeSummary,
    IReadOnlyList<DistilledLessonProposal> Lessons);

/// <summary>Reflects on a finished life and proposes reusable lessons (usually a language model).</summary>
public interface ILessonDistiller
{
    Task<LessonDistillationResult> DistillAsync(
        ModelConfiguration configuration,
        LessonDistillationRequest request,
        CancellationToken cancellationToken = default);
}
