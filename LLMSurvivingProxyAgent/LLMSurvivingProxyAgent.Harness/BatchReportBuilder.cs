using System.Globalization;
using System.Text;
using System.Text.Json;
using LLMSurvivingProxyAgent.Core;
using LLMSurvivingProxyAgent.Runtime;

namespace LLMSurvivingProxyAgent.Harness;

public sealed record MilestoneFunnelRow(
    EpisodeMilestone Milestone,
    int EpisodesReached,
    double ReachedRate,
    double? MedianTurnsUsed,
    double? MedianMinutes);

public sealed record EpisodeReportRow(
    string EpisodeId,
    string AgentId,
    string ProfileFileName,
    HarnessEpisodeState State,
    int Segments,
    int Steps,
    int Milestones,
    double TotalReward,
    long? NetWorthDelta,
    int? TurnsUsed,
    double? NetWorthPerTurn,
    double? PilotShare,
    bool ShipLost,
    string? EndReason);

public sealed record BatchReport(
    string BatchId,
    DateTimeOffset GeneratedAt,
    string BaseProfile,
    string Goal,
    int Parallel,
    double WallClockMinutes,
    int Episodes,
    IReadOnlyDictionary<string, int> StateCounts,
    double CompletionRate,
    double ShipLossRate,
    double? MedianNetWorthPerTurn,
    double? MeanReward,
    IReadOnlyDictionary<string, int> DecisionSources,
    double? VerifiedStepRate,
    IReadOnlyList<MilestoneFunnelRow> Funnel,
    IReadOnlyList<EpisodeReportRow> Rows);

public static class BatchReportBuilder
{
    private static readonly JsonSerializerOptions JsonOptions = EpisodeResultFileWriter.JsonOptions;

    public static async Task<BatchReport> BuildAsync(
        HarnessBatch batch,
        IReadOnlyList<HarnessEpisode> episodes,
        ITrajectoryStore trajectoryStore,
        DateTimeOffset generatedAt,
        CancellationToken cancellationToken = default)
    {
        var rows = new List<EpisodeReportRow>();
        var sources = Enum.GetValues<TrajectoryDecisionSource>().ToDictionary(source => source.ToString(), _ => 0);
        var reachedByMilestone = Enum.GetValues<EpisodeMilestone>()
            .ToDictionary(milestone => milestone, _ => new List<(int? Turns, double Minutes)>());
        int totalSteps = 0;
        int verifiedSteps = 0;

        foreach (HarnessEpisode episode in episodes)
        {
            TrajectoryEpisode? trajectory = await trajectoryStore.GetEpisodeAsync(episode.EpisodeId, cancellationToken)
                .ConfigureAwait(false);
            IReadOnlyList<TrajectoryStep> steps = trajectory is null
                ? []
                : await trajectoryStore.GetStepsAsync(episode.EpisodeId, cancellationToken).ConfigureAwait(false);
            IReadOnlyList<TrajectoryMilestone> milestones = trajectory is null
                ? []
                : await trajectoryStore.GetMilestonesAsync(episode.EpisodeId, cancellationToken).ConfigureAwait(false);

            foreach (TrajectoryStep step in steps)
            {
                sources[step.Source.ToString()]++;
            }
            totalSteps += steps.Count;
            verifiedSteps += steps.Count(step => step.Reward?.Verified == true);
            foreach (TrajectoryMilestone milestone in milestones)
            {
                reachedByMilestone[milestone.Milestone].Add(
                    (milestone.TurnsUsed, (milestone.ReachedAt - trajectory!.StartedAt).TotalMinutes));
            }

            int pilot = steps.Count(step => step.Source == TrajectoryDecisionSource.Pilot);
            rows.Add(new EpisodeReportRow(
                episode.EpisodeId,
                episode.AgentId,
                episode.ProfileFileName,
                episode.State,
                episode.Segments,
                steps.Count,
                milestones.Count,
                trajectory?.TotalReward ?? 0,
                trajectory?.NetWorthDelta,
                trajectory?.TurnsUsed,
                trajectory?.NetWorthPerTurn,
                steps.Count == 0 ? null : Math.Round((double)pilot / steps.Count, 3),
                trajectory?.ShipLost == true || episode.State == HarnessEpisodeState.ShipLost,
                trajectory?.EndReason ?? episode.LastError));
        }

        int count = rows.Count;
        double Rate(int value) => count == 0 ? 0 : Math.Round((double)value / count, 3);
        var funnel = reachedByMilestone
            .Select(pair => new MilestoneFunnelRow(
                pair.Key,
                pair.Value.Count,
                Rate(pair.Value.Count),
                Median(pair.Value.Where(item => item.Turns is not null).Select(item => (double)item.Turns!.Value)),
                Median(pair.Value.Select(item => item.Minutes))))
            .ToList();

        return new BatchReport(
            batch.BatchId,
            generatedAt,
            Path.GetFileName(batch.BaseProfilePath),
            batch.Goal,
            batch.Parallel,
            batch.WallClockMinutes,
            count,
            rows.GroupBy(row => row.State.ToString()).ToDictionary(group => group.Key, group => group.Count()),
            Rate(rows.Count(row => row.State == HarnessEpisodeState.Completed)),
            Rate(rows.Count(row => row.ShipLost)),
            Median(rows.Where(row => row.NetWorthPerTurn is not null).Select(row => row.NetWorthPerTurn!.Value)),
            count == 0 ? null : Math.Round(rows.Average(row => row.TotalReward), 3),
            sources,
            totalSteps == 0 ? null : Math.Round((double)verifiedSteps / totalSteps, 3),
            funnel,
            rows);
    }

    public static async Task WriteAsync(BatchReport report, string directory, CancellationToken cancellationToken = default)
    {
        Directory.CreateDirectory(directory);
        string basePath = Path.Combine(directory, report.BatchId);
        await File.WriteAllTextAsync($"{basePath}.json", JsonSerializer.Serialize(report, JsonOptions), cancellationToken)
            .ConfigureAwait(false);
        await File.WriteAllTextAsync($"{basePath}.md", ToMarkdown(report), cancellationToken).ConfigureAwait(false);
    }

    public static string ToMarkdown(BatchReport report)
    {
        var text = new StringBuilder();
        CultureInfo culture = CultureInfo.InvariantCulture;
        text.AppendLine(culture, $"# Harness batch {report.BatchId}");
        text.AppendLine();
        text.AppendLine(culture, $"Generated {report.GeneratedAt:u}. Base profile `{report.BaseProfile}`, goal {report.Goal}, " +
            $"{report.Parallel} parallel, {report.WallClockMinutes:N0}-minute cap per segment.");
        text.AppendLine();
        text.AppendLine("## Summary");
        text.AppendLine();
        text.AppendLine("| Metric | Value |");
        text.AppendLine("|---|---:|");
        text.AppendLine(culture, $"| Episodes | {report.Episodes} |");
        text.AppendLine(culture, $"| Completion rate | {Percent(report.CompletionRate)} |");
        text.AppendLine(culture, $"| Ship-loss rate | {Percent(report.ShipLossRate)} |");
        text.AppendLine(culture, $"| Median net worth per turn | {Format(report.MedianNetWorthPerTurn)} |");
        text.AppendLine(culture, $"| Mean episode reward | {Format(report.MeanReward)} |");
        text.AppendLine(culture, $"| Verified step rate | {Percent(report.VerifiedStepRate)} |");
        text.AppendLine();
        text.AppendLine("End states: " + string.Join(", ", report.StateCounts.Select(pair => $"{pair.Key} {pair.Value}")) + ".");
        text.AppendLine();
        text.AppendLine("Decision sources: " + string.Join(", ", report.DecisionSources.Select(pair => $"{pair.Key} {pair.Value}")) + ".");
        text.AppendLine();
        text.AppendLine("## Milestone funnel");
        text.AppendLine();
        text.AppendLine("| Milestone | Episodes | Rate | Median turns used | Median minutes |");
        text.AppendLine("|---|---:|---:|---:|---:|");
        foreach (MilestoneFunnelRow row in report.Funnel)
        {
            text.AppendLine(culture,
                $"| {row.Milestone} | {row.EpisodesReached} | {Percent(row.ReachedRate)} | {Format(row.MedianTurnsUsed)} | {Format(row.MedianMinutes)} |");
        }
        text.AppendLine();
        text.AppendLine("## Episodes");
        text.AppendLine();
        text.AppendLine("| Episode | Agent | State | Segments | Steps | Milestones | Reward | Net worth Δ | Turns | Pilot share | End reason |");
        text.AppendLine("|---|---|---|---:|---:|---:|---:|---:|---:|---:|---|");
        foreach (EpisodeReportRow row in report.Rows)
        {
            text.AppendLine(culture,
                $"| {row.EpisodeId} | {row.AgentId} | {row.State} | {row.Segments} | {row.Steps} | {row.Milestones} | " +
                $"{row.TotalReward:N2} | {Format(row.NetWorthDelta)} | {Format(row.TurnsUsed)} | " +
                $"{Percent(row.PilotShare)} | {Escape(row.EndReason)} |");
        }
        text.AppendLine();
        text.AppendLine("## Characters created");
        text.AppendLine();
        text.AppendLine("Each episode created a new character on the server. Profiles for cleanup:");
        text.AppendLine();
        foreach (EpisodeReportRow row in report.Rows)
        {
            text.AppendLine(culture, $"- `{row.AgentId}` — `{row.ProfileFileName}`");
        }
        return text.ToString();
    }

    private static double? Median(IEnumerable<double> values)
    {
        double[] sorted = [.. values.Order()];
        if (sorted.Length == 0)
        {
            return null;
        }
        int middle = sorted.Length / 2;
        return Math.Round(sorted.Length % 2 == 1 ? sorted[middle] : (sorted[middle - 1] + sorted[middle]) / 2, 2);
    }

    private static string Percent(double? value) => value is { } rate ? $"{Math.Round(rate * 100):0}%" : "n/a";

    private static string Format(double? value) => value is { } number ? number.ToString("N2", CultureInfo.InvariantCulture) : "n/a";

    private static string Format(long? value) => value is { } number ? number.ToString("N0", CultureInfo.InvariantCulture) : "n/a";

    private static string Format(int? value) => value is { } number ? number.ToString("N0", CultureInfo.InvariantCulture) : "n/a";

    private static string Escape(string? value) =>
        string.IsNullOrWhiteSpace(value) ? string.Empty : value.Replace("|", "\\|", StringComparison.Ordinal).ReplaceLineEndings(" ");
}
