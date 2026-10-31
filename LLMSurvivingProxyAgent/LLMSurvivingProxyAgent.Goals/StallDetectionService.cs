using LLMSurvivingProxyAgent.Core;

namespace LLMSurvivingProxyAgent.Goals;

public static class StallDetectionService
{
    public const int RequiredUnchangedCycles = 3;

    private static readonly string[] UnavailableSignals =
    [
        "New-sector and new-route counts are not present in review snapshots.",
        "Route profitability and macro outcomes are not correlated with snapshots.",
        "Unchanged sector alone cannot establish that exploration has stalled."
    ];

    public static FailureDetectionSummary Evaluate(
        GameSnapshot snapshot,
        IReadOnlyList<StrategicReviewRecord> previousReviews,
        string? activeObjectiveProposalId,
        DateTimeOffset? activeObjectiveCreatedAt)
    {
        ArgumentNullException.ThrowIfNull(snapshot);
        ArgumentNullException.ThrowIfNull(previousReviews);

        var latestPriorSnapshotAt = previousReviews
            .Select(review => (DateTimeOffset?)review.Context.Snapshot.ObservedAt)
            .Max();
        var snapshotPredatesHistory = latestPriorSnapshotAt is { } latest &&
                                     snapshot.ObservedAt < latest;
        var indicators = new List<StallIndicator>
        {
            EvaluateCredits(snapshot, previousReviews, snapshotPredatesHistory),
            EvaluateObjective(
                previousReviews,
                activeObjectiveProposalId,
                activeObjectiveCreatedAt,
                snapshot.ObservedAt,
                snapshotPredatesHistory)
        };
        return new FailureDetectionSummary(
            RequiredUnchangedCycles,
            snapshotPredatesHistory,
            latestPriorSnapshotAt,
            indicators.Any(indicator => indicator.IsStalled),
            indicators,
            UnavailableSignals);
    }

    private static StallIndicator EvaluateCredits(
        GameSnapshot snapshot,
        IReadOnlyList<StrategicReviewRecord> previousReviews,
        bool snapshotPredatesHistory)
    {
        if (snapshotPredatesHistory)
        {
            return new StallIndicator(
                "Synchronized credits",
                false,
                0,
                null,
                null,
                "Not evaluated because this snapshot predates saved review history.");
        }
        if (snapshot.SynchronizedCredits is not { } credits)
        {
            return new StallIndicator(
                "Synchronized credits",
                false,
                0,
                null,
                null,
                "Not observable: synchronized credits are missing from this snapshot.");
        }

        var matchingReviews = new List<StrategicReviewRecord>();
        foreach (var review in previousReviews.OrderByDescending(
                     item => item.Context.Snapshot.ObservedAt))
        {
            if (review.Context.Snapshot.ObservedAt > snapshot.ObservedAt)
            {
                continue;
            }
            if (review.Context.Snapshot.SynchronizedCredits != credits)
            {
                break;
            }
            matchingReviews.Add(review);
        }

        var unchangedCycles = matchingReviews.Count;
        var isStalled = unchangedCycles >= RequiredUnchangedCycles;
        var since = matchingReviews.LastOrDefault()?.Context.Snapshot.ObservedAt;
        TimeSpan? duration = since is { } start ? snapshot.ObservedAt - start : null;
        var evidence = unchangedCycles == 0
            ? "No consecutive earlier snapshot has the same synchronized credit value."
            : $"Credits remained at {credits:N0} across the current snapshot and {unchangedCycles} earlier review(s).";
        return new StallIndicator(
            "Synchronized credits",
            isStalled,
            unchangedCycles,
            since,
            duration,
            evidence);
    }

    private static StallIndicator EvaluateObjective(
        IReadOnlyList<StrategicReviewRecord> previousReviews,
        string? activeObjectiveProposalId,
        DateTimeOffset? activeObjectiveCreatedAt,
        DateTimeOffset currentObservedAt,
        bool snapshotPredatesHistory)
    {
        if (snapshotPredatesHistory)
        {
            return new StallIndicator(
                "Active objective",
                false,
                0,
                null,
                null,
                "Not evaluated because this snapshot predates saved review history.");
        }
        if (activeObjectiveProposalId is null)
        {
            return new StallIndicator(
                "Active objective",
                false,
                0,
                null,
                null,
                "Not applicable: there is no active objective to track.");
        }

        var unchangedCycles = 0;
        var matchingReviews = new List<StrategicReviewRecord>();
        foreach (var review in previousReviews.OrderByDescending(item => item.ReviewedAt))
        {
            if (review.Context.ActiveObjective?.ProposalId != activeObjectiveProposalId)
            {
                break;
            }
            unchangedCycles++;
            matchingReviews.Add(review);
        }

        var since = activeObjectiveCreatedAt
            ?? (unchangedCycles == 0 ? null : matchingReviews[^1].ReviewedAt);
        TimeSpan? duration = since is { } start && currentObservedAt >= start
            ? currentObservedAt - start
            : null;
        var isStalled = unchangedCycles >= RequiredUnchangedCycles;
        var evidence = unchangedCycles == 0
            ? "No earlier review confirms this same objective was still active."
            : $"The same objective remained active across {unchangedCycles} consecutive earlier review(s).";
        return new StallIndicator("Active objective", isStalled, unchangedCycles, since, duration, evidence);
    }
}
