using LLMSurvivingProxyAgent.Core;

namespace LLMSurvivingProxyAgent.Goals;

public static class ReviewHistoryAnalyzer
{
    public static ReviewHistoryAnalysis Analyze(
        IReadOnlyList<StrategicReviewRecord> previousReviews,
        IReadOnlyList<MacroExecutionRecord> recentMacroActivity,
        DateTimeOffset now)
    {
        ArgumentNullException.ThrowIfNull(previousReviews);
        ArgumentNullException.ThrowIfNull(recentMacroActivity);

        var reviews = previousReviews
            .OrderByDescending(review => review.ReviewedAt)
            .Take(10)
            .ToArray();
        var history = reviews.Select(review => new StrategicReviewHistoryItem(
            review.ReviewId,
            review.ReviewedAt,
            review.Context.ActiveObjective,
            review.Assessment.Progress,
            review.Assessment.Summary,
            review.Assessment.Recommendation,
            review.Assessment.ObjectiveDecision,
            review.DecisionOutcome,
            review.Context.FailureDetection.IsStalled)).ToArray();
        var summaries = FindRepeatedText(reviews.Select(review => review.Assessment.Summary));
        var recommendations = FindRepeatedText(reviews.Select(review => review.Assessment.Recommendation));
        var latestReview = reviews.FirstOrDefault();
        var activityWindowStart = latestReview?.ReviewedAt;
        var activity = activityWindowStart is { } start
            ? recentMacroActivity
                .Where(item => item.StartTime >= start && item.StartTime <= now)
                .OrderByDescending(item => item.StartTime)
                .Take(25)
                .ToArray()
            : [];
        var consecutiveStalledReviews = reviews
            .TakeWhile(review => review.Context.FailureDetection.IsStalled)
            .Count();
        var activityNote = latestReview is null
            ? "No earlier review defines an activity window. Local macro telemetry does not prove live in-game actions."
            : "Records cover local macro-engine activity only; they do not prove live in-game execution or link free-text recommendations to actions.";

        return new ReviewHistoryAnalysis(
            reviews.Length,
            history,
            summaries.Text,
            summaries.Count,
            recommendations.Text,
            recommendations.Count,
            consecutiveStalledReviews,
            activityWindowStart,
            activity,
            latestReview is null ? null : activity.Length == 0,
            activityNote);
    }

    private static (string? Text, int Count) FindRepeatedText(IEnumerable<string> values)
    {
        var groups = values
            .GroupBy(Normalize, StringComparer.Ordinal)
            .Where(group => group.Count() > 1)
            .OrderByDescending(group => group.Count())
            .ThenBy(group => group.Key, StringComparer.Ordinal)
            .ToArray();
        return groups.Length == 0
            ? (null, 0)
            : (groups[0].First(), groups[0].Count());
    }

    private static string Normalize(string value) =>
        string.Join(' ', value.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries))
            .ToUpperInvariant();
}
