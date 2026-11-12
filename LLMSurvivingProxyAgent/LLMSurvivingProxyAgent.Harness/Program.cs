using System.Globalization;
using LLMSurvivingProxyAgent.Core;
using LLMSurvivingProxyAgent.Harness;
using LLMSurvivingProxyAgent.Runtime;
using Microsoft.Data.Sqlite;

return await HarnessCli.RunAsync(args);

internal static class HarnessCli
{
    private const string DefaultPilotModel = "qwen3:1.7b";

    /// <summary>All learners share this playbook memory unless --memory-id says otherwise.</summary>
    private const string DefaultMemoryId = "veteran";

    private const string Usage = """
        LLMSurvivingProxyAgent.Harness — runs repeatable TradeWars learning episodes in MTC.

        Commands:
          run     --base-profile <path.json> --episodes <n> [--parallel 2] [--goal FullyUpgradedShip]
                  [--wall-clock-minutes 480] [--map copy|empty] [--game-day-reset HH:mm]
                  [--pilot-model qwen3:1.7b]   Pilot model for every agent in the batch (default: agent default).
          loop    --base-profile <path.json> [--parallel 2] [--max-episodes <n>] [--hours <h>] [--stop-file <path>]
                  [and the run options]   Unattended rolling run: keeps --parallel episodes going with new learners,
                  resumes its own paused episodes after the game-day reset, rebuilds the report after every episode,
                  and waits 5 minutes after an error (stops after 5 errors in a row). Stops at --max-episodes,
                  after --hours, or when the stop file appears (default <data-dir>\harness.stop); running episodes finish.
          resume  [--batch <id>]      Restart paused episodes whose game-day reset has passed.
          status  [--batch <id>]      List episodes and their state.
          report  --batch <id>        Rebuild the batch report.
          cleanup [--batch <id>]      Remove finished learners' registry entries and profiles (logs are archived).

        Shared options:
          --mtc <path\MTC.exe>        Default: nearest LLMControlCenter\bin\Debug\net10.0\MTC.exe above the current directory.
          --data-dir <dir>            Default: %LOCALAPPDATA%\LLMSurvivingProxyAgent (holds trajectories.db).
          --report-dir <dir>          Default: .\reports\harness
          --grace-minutes <n>         Extra time past the cap before MTC is killed. Default: 15.
          --cleanup true|false        Clean up each learner when its episode finishes. Default: true.
          --memory-id <id>            Playbook memory shared by every learner, so lessons carry from life to life.
                                      Default: veteran. Use a new id to start a fresh lineage.
        """;

    public static async Task<int> RunAsync(string[] args)
    {
        if (args.Length == 0 || args[0] is "-h" or "--help" or "help")
        {
            Console.WriteLine(Usage);
            return args.Length == 0 ? 1 : 0;
        }

        try
        {
            var options = ParseOptions(args.Skip(1).ToArray());
            using var cancellation = new CancellationTokenSource();
            Console.CancelKeyPress += (_, eventArgs) =>
            {
                eventArgs.Cancel = true;
                cancellation.Cancel();
                Console.WriteLine("Stopping: running MTC sessions will be closed.");
            };

            string dataDirectory = Path.GetFullPath(Get(options, "data-dir") ?? Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "LLMSurvivingProxyAgent"));
            Directory.CreateDirectory(dataDirectory);
            string connectionString = new SqliteConnectionStringBuilder
            {
                DataSource = Path.Combine(dataDirectory, "trajectories.db"),
                ForeignKeys = true
            }.ToString();
            var harnessStore = new HarnessStore(connectionString);
            var trajectoryStore = new SqliteTrajectoryStore(connectionString);
            await trajectoryStore.InitializeAsync(cancellation.Token);
            await harnessStore.InitializeAsync(cancellation.Token);
            string reportDirectory = Path.GetFullPath(Get(options, "report-dir") ?? Path.Combine("reports", "harness"));

            return args[0].ToLowerInvariant() switch
            {
                "run" => await RunBatchAsync(options, dataDirectory, reportDirectory, harnessStore, trajectoryStore, cancellation.Token),
                "loop" => await LoopAsync(options, dataDirectory, reportDirectory, harnessStore, trajectoryStore, cancellation.Token),
                "resume" => await ResumeAsync(options, dataDirectory, reportDirectory, harnessStore, trajectoryStore, cancellation.Token),
                "status" => await StatusAsync(options, harnessStore, cancellation.Token),
                "report" => await ReportAsync(options, reportDirectory, harnessStore, trajectoryStore, cancellation.Token),
                "cleanup" => await CleanupAsync(options, dataDirectory, harnessStore, cancellation.Token),
                _ => throw new ArgumentException($"Unknown command '{args[0]}'.\n\n{Usage}")
            };
        }
        catch (Exception exception) when (exception is ArgumentException or InvalidOperationException or FileNotFoundException)
        {
            Console.Error.WriteLine(exception.Message);
            return 2;
        }
    }

    private sealed record PreparedBatch(HarnessBatch Batch, string BaseProfile, MapSeed Map, string Mtc, string Suffix);

    private static async Task<PreparedBatch> CreateBatchAsync(
        Dictionary<string, string> options,
        HarnessStore harnessStore,
        CancellationToken cancellationToken)
    {
        string baseProfile = Path.GetFullPath(Require(options, "base-profile"));
        if (!File.Exists(baseProfile))
        {
            throw new FileNotFoundException($"Base profile '{baseProfile}' was not found.");
        }
        int parallel = ParseInt(options, "parallel", 2, minimum: 1);
        EpisodeGoal goal = Get(options, "goal") is { } goalText
            ? Enum.Parse<EpisodeGoal>(goalText.Replace("-", string.Empty, StringComparison.Ordinal), ignoreCase: true)
            : EpisodeGoal.FullyUpgradedShip;
        double minutes = ParseDouble(options, "wall-clock-minutes", 480);
        MapSeed map = Get(options, "map")?.ToLowerInvariant() switch
        {
            null or "copy" => MapSeed.CopyBase,
            "empty" => MapSeed.Empty,
            var other => throw new ArgumentException($"--map must be 'copy' or 'empty', not '{other}'.")
        };
        string mtc = ResolveMtc(options);
        if (Get(options, "pilot-model") is { } pilotModel &&
            !EpisodeCommandLine.TryExtract(["--episode-id", "check", "--episode-pilot-model", pilotModel], out _, out _, out string? modelError))
        {
            throw new ArgumentException(modelError);
        }
        if (!ProfileCloner.BaseShipCatalogExists(baseProfile))
        {
            Console.WriteLine(
                "Warning: the base game has no Mombot ships.cfg, so ship limits are unknown. Max-holds/fighters/shields " +
                "milestones will only be awarded from a fresh Refurb quote. Start Mombot once on the base profile to capture it.");
        }

        DateTimeOffset now = DateTimeOffset.UtcNow;
        string suffix = Guid.NewGuid().ToString("N")[..6];
        string batchId = $"b{now.ToLocalTime():yyyyMMdd-HHmm}-{suffix}";
        var batch = new HarnessBatch(
            batchId, now, baseProfile, Path.GetDirectoryName(baseProfile)!, parallel, goal.ToString(), minutes,
            Get(options, "pilot-model") ?? DefaultPilotModel);
        await harnessStore.InsertBatchAsync(batch, cancellationToken);
        return new PreparedBatch(batch, baseProfile, map, mtc, suffix);
    }

    private static async Task<HarnessEpisode> PrepareEpisodeAsync(
        PreparedBatch prepared,
        int ordinal,
        HarnessStore harnessStore,
        CancellationToken cancellationToken)
    {
        DateTimeOffset now = DateTimeOffset.UtcNow;
        string agentId = $"learner-{prepared.Suffix}-{ordinal:D2}";
        ClonedProfile clone = ProfileCloner.Clone(prepared.BaseProfile, agentId, prepared.Map);
        var episode = new HarnessEpisode(
            $"{prepared.Batch.BatchId}-{ordinal:D2}", prepared.Batch.BatchId, ordinal, agentId, clone.ProfileFileName,
            HarnessEpisodeState.Pending, 0, null, null, null, now, now);
        await harnessStore.UpsertEpisodeAsync(episode, cancellationToken);
        Console.WriteLine($"Prepared {episode.EpisodeId}: agent {agentId}, profile '{clone.ProfileFileName}'" +
            $"{(clone.MapCopied ? ", map copied" : ", empty map")}{(clone.ShipCatalogCopied ? ", ship catalog copied" : string.Empty)}.");
        return episode;
    }

    private static async Task<int> RunBatchAsync(
        Dictionary<string, string> options,
        string dataDirectory,
        string reportDirectory,
        HarnessStore harnessStore,
        ITrajectoryStore trajectoryStore,
        CancellationToken cancellationToken)
    {
        int episodes = ParseInt(options, "episodes", null, minimum: 1);
        PreparedBatch prepared = await CreateBatchAsync(options, harnessStore, cancellationToken);
        var created = new List<HarnessEpisode>();
        for (int ordinal = 1; ordinal <= episodes; ordinal++)
        {
            created.Add(await PrepareEpisodeAsync(prepared, ordinal, harnessStore, cancellationToken));
        }

        HarnessScheduler scheduler = CreateScheduler(options, dataDirectory, prepared.Batch, prepared.Mtc, harnessStore, trajectoryStore);
        await scheduler.RunDueAsync(created, cancellationToken);
        return await FinishAsync(prepared.Batch, harnessStore, trajectoryStore, reportDirectory, cancellationToken);
    }

    private static async Task<int> LoopAsync(
        Dictionary<string, string> options,
        string dataDirectory,
        string reportDirectory,
        HarnessStore harnessStore,
        ITrajectoryStore trajectoryStore,
        CancellationToken cancellationToken)
    {
        int? maxEpisodes = Get(options, "max-episodes") is null
            ? null
            : ParseInt(options, "max-episodes", null, minimum: 1);
        DateTimeOffset? deadline = Get(options, "hours") is null
            ? null
            : DateTimeOffset.UtcNow.AddHours(ParseDouble(options, "hours", 1));
        string stopFile = Path.GetFullPath(Get(options, "stop-file") ?? Path.Combine(dataDirectory, "harness.stop"));
        if (File.Exists(stopFile))
        {
            File.Delete(stopFile);
            Console.WriteLine($"Removed a stop file left over from an earlier run: {stopFile}");
        }

        PreparedBatch prepared = await CreateBatchAsync(options, harnessStore, cancellationToken);
        Console.WriteLine(
            $"Rolling batch {prepared.Batch.BatchId}: {prepared.Batch.Parallel} at a time" +
            (maxEpisodes is { } max ? $", up to {max} new episode(s)" : ", no episode limit") +
            (deadline is { } until ? $", until {until.ToLocalTime():g}" : string.Empty) +
            $". To stop after the running episodes finish, create '{stopFile}'. Ctrl+C stops at once.");
        HarnessScheduler scheduler = CreateScheduler(options, dataDirectory, prepared.Batch, prepared.Mtc, harnessStore, trajectoryStore);
        await scheduler.RunRollingAsync(
            (ordinal, token) => PrepareEpisodeAsync(prepared, ordinal, harnessStore, token),
            new RollingLimits(maxEpisodes, deadline, stopFile),
            _ => WriteReportAsync(prepared.Batch, harnessStore, trajectoryStore, reportDirectory),
            cancellationToken);
        return await FinishAsync(prepared.Batch, harnessStore, trajectoryStore, reportDirectory, cancellationToken);
    }

    private static async Task WriteReportAsync(
        HarnessBatch batch,
        HarnessStore harnessStore,
        ITrajectoryStore trajectoryStore,
        string reportDirectory)
    {
        IReadOnlyList<HarnessEpisode> episodes = await harnessStore.GetEpisodesAsync(batch.BatchId, CancellationToken.None);
        BatchReport report = await BatchReportBuilder.BuildAsync(batch, episodes, trajectoryStore, DateTimeOffset.UtcNow, CancellationToken.None);
        await BatchReportBuilder.WriteAsync(report, reportDirectory, CancellationToken.None);
    }

    private static async Task<int> ResumeAsync(
        Dictionary<string, string> options,
        string dataDirectory,
        string reportDirectory,
        HarnessStore harnessStore,
        ITrajectoryStore trajectoryStore,
        CancellationToken cancellationToken)
    {
        string mtc = ResolveMtc(options);
        IReadOnlyList<HarnessBatch> batches = Get(options, "batch") is { } batchId
            ? [await harnessStore.GetBatchAsync(batchId, cancellationToken) ?? throw new ArgumentException($"Batch '{batchId}' was not found.")]
            : await harnessStore.GetBatchesAsync(cancellationToken);
        int resumed = 0;
        foreach (HarnessBatch batch in batches)
        {
            IReadOnlyList<HarnessEpisode> episodes = await harnessStore.GetEpisodesAsync(batch.BatchId, cancellationToken);
            HarnessScheduler scheduler = CreateScheduler(options, dataDirectory, batch, mtc, harnessStore, trajectoryStore);
            List<HarnessEpisode> due = episodes.Where(scheduler.IsDue).ToList();
            if (due.Count == 0)
            {
                continue;
            }

            resumed += due.Count;
            await scheduler.RunDueAsync(due, cancellationToken);
            await FinishAsync(batch, harnessStore, trajectoryStore, reportDirectory, cancellationToken);
        }

        Console.WriteLine(resumed == 0 ? "No episodes are due to resume." : $"Ran {resumed} due episode(s).");
        return 0;
    }

    private static async Task<int> StatusAsync(
        Dictionary<string, string> options,
        HarnessStore harnessStore,
        CancellationToken cancellationToken)
    {
        IReadOnlyList<HarnessEpisode> episodes = await harnessStore.GetEpisodesAsync(Get(options, "batch"), cancellationToken);
        if (episodes.Count == 0)
        {
            Console.WriteLine("No harness episodes found.");
            return 0;
        }

        foreach (HarnessEpisode episode in episodes)
        {
            string resume = episode.ResumeAfter is { } after ? $" resume after {after.ToLocalTime():g}" : string.Empty;
            Console.WriteLine($"{episode.EpisodeId,-32} {episode.AgentId,-22} {episode.State,-10} segments {episode.Segments}{resume}");
        }
        return 0;
    }

    private static async Task<int> ReportAsync(
        Dictionary<string, string> options,
        string reportDirectory,
        HarnessStore harnessStore,
        ITrajectoryStore trajectoryStore,
        CancellationToken cancellationToken)
    {
        string batchId = Require(options, "batch");
        HarnessBatch batch = await harnessStore.GetBatchAsync(batchId, cancellationToken)
            ?? throw new ArgumentException($"Batch '{batchId}' was not found.");
        return await FinishAsync(batch, harnessStore, trajectoryStore, reportDirectory, cancellationToken);
    }

    private static async Task<int> FinishAsync(
        HarnessBatch batch,
        HarnessStore harnessStore,
        ITrajectoryStore trajectoryStore,
        string reportDirectory,
        CancellationToken cancellationToken)
    {
        IReadOnlyList<HarnessEpisode> episodes = await harnessStore.GetEpisodesAsync(batch.BatchId, CancellationToken.None);
        BatchReport report = await BatchReportBuilder.BuildAsync(batch, episodes, trajectoryStore, DateTimeOffset.UtcNow, CancellationToken.None);
        await BatchReportBuilder.WriteAsync(report, reportDirectory, CancellationToken.None);
        Console.WriteLine($"Report: {Path.Combine(reportDirectory, batch.BatchId)}.md");
        foreach (HarnessEpisode paused in episodes.Where(episode => episode.State == HarnessEpisodeState.Paused))
        {
            Console.WriteLine($"{paused.EpisodeId} is paused until {paused.ResumeAfter?.ToLocalTime():g}; run 'resume' then.");
        }
        return cancellationToken.IsCancellationRequested ? 3 : 0;
    }

    private static HarnessScheduler CreateScheduler(
        Dictionary<string, string> options,
        string dataDirectory,
        HarnessBatch batch,
        string mtc,
        HarnessStore harnessStore,
        ITrajectoryStore trajectoryStore)
    {
        TimeOnly reset = Get(options, "game-day-reset") is { } resetText
            ? TimeOnly.Parse(resetText, CultureInfo.InvariantCulture)
            : TimeOnly.MinValue;
        var settings = new SchedulerSettings(
            ParseInt(options, "parallel", batch.Parallel, minimum: 1),
            Enum.Parse<EpisodeGoal>(batch.Goal),
            TimeSpan.FromMinutes(batch.WallClockMinutes),
            TimeSpan.FromMinutes(ParseDouble(options, "grace-minutes", 15)),
            reset,
            Path.Combine(dataDirectory, "harness", batch.BatchId),
            PilotModel: batch.PilotModel ?? DefaultPilotModel,
            MemoryId: Get(options, "memory-id") ?? DefaultMemoryId);
        if (!PlaybookMemory.IsValidMemoryId(settings.MemoryId))
        {
            throw new ArgumentException("--memory-id must be 1-64 characters using letters, digits, '.', '_' or '-'.");
        }
        bool cleanup = !string.Equals(Get(options, "cleanup"), "false", StringComparison.OrdinalIgnoreCase);
        LearnerCleanup learnerCleanup = CreateCleanup(dataDirectory);
        return new HarnessScheduler(
            harnessStore,
            trajectoryStore,
            new MtcProcessLauncher(mtc),
            settings,
            Console.Out,
            episodeFinished: cleanup ? episode => learnerCleanup.CleanAsync(episode, batch.GamesDirectory) : null);
    }

    private static LearnerCleanup CreateCleanup(string dataDirectory) =>
        new(
            new SqliteConnectionStringBuilder
            {
                DataSource = Path.Combine(dataDirectory, "agents.db"),
                ForeignKeys = true
            }.ToString(),
            Path.Combine(dataDirectory, "harness"),
            Console.Out);

    private static async Task<int> CleanupAsync(
        Dictionary<string, string> options,
        string dataDirectory,
        HarnessStore harnessStore,
        CancellationToken cancellationToken)
    {
        LearnerCleanup learnerCleanup = CreateCleanup(dataDirectory);
        IReadOnlyList<HarnessBatch> batches = Get(options, "batch") is { } batchId
            ? [await harnessStore.GetBatchAsync(batchId, cancellationToken) ?? throw new ArgumentException($"Batch '{batchId}' was not found.")]
            : await harnessStore.GetBatchesAsync(cancellationToken);
        int cleaned = 0;
        int alreadyClean = 0;
        int kept = 0;
        foreach (HarnessBatch batch in batches)
        {
            foreach (HarnessEpisode episode in await harnessStore.GetEpisodesAsync(batch.BatchId, cancellationToken))
            {
                switch (await learnerCleanup.CleanAsync(episode, batch.GamesDirectory, cancellationToken))
                {
                    case LearnerCleanupResult.Cleaned:
                        cleaned++;
                        break;
                    case LearnerCleanupResult.AlreadyClean:
                        alreadyClean++;
                        break;
                    default:
                        kept++;
                        break;
                }
            }
        }

        Console.WriteLine(
            $"Cleaned up {cleaned} finished learner(s); {alreadyClean} already clean; kept {kept} paused, running, or open learner(s).");
        return 0;
    }

    private static string ResolveMtc(Dictionary<string, string> options)
    {
        if (Get(options, "mtc") is { } configured)
        {
            string path = Path.GetFullPath(configured);
            return File.Exists(path) ? path : throw new FileNotFoundException($"MTC executable '{path}' was not found.");
        }

        for (DirectoryInfo? directory = new(Environment.CurrentDirectory); directory is not null; directory = directory.Parent)
        {
            string candidate = Path.Combine(directory.FullName, "LLMControlCenter", "bin", "Debug", "net10.0", "MTC.exe");
            if (File.Exists(candidate))
            {
                return candidate;
            }
        }
        throw new FileNotFoundException("MTC.exe was not found; build LLMControlCenter or pass --mtc.");
    }

    private static Dictionary<string, string> ParseOptions(string[] args)
    {
        var options = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        for (int index = 0; index < args.Length; index++)
        {
            if (!args[index].StartsWith("--", StringComparison.Ordinal))
            {
                throw new ArgumentException($"Unexpected argument '{args[index]}'.");
            }
            string name = args[index][2..];
            if (index + 1 >= args.Length || args[index + 1].StartsWith("--", StringComparison.Ordinal))
            {
                throw new ArgumentException($"--{name} requires a value.");
            }
            options[name] = args[++index];
        }
        return options;
    }

    private static string? Get(Dictionary<string, string> options, string name) =>
        options.TryGetValue(name, out string? value) ? value : null;

    private static string Require(Dictionary<string, string> options, string name) =>
        Get(options, name) ?? throw new ArgumentException($"--{name} is required.");

    private static int ParseInt(Dictionary<string, string> options, string name, int? fallback, int minimum)
    {
        string? text = Get(options, name);
        if (text is null)
        {
            return fallback ?? throw new ArgumentException($"--{name} is required.");
        }
        return int.TryParse(text, NumberStyles.Integer, CultureInfo.InvariantCulture, out int value) && value >= minimum
            ? value
            : throw new ArgumentException($"--{name} must be an integer of at least {minimum}.");
    }

    private static double ParseDouble(Dictionary<string, string> options, string name, double fallback)
    {
        string? text = Get(options, name);
        if (text is null)
        {
            return fallback;
        }
        return double.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out double value) && value > 0
            ? value
            : throw new ArgumentException($"--{name} must be a positive number.");
    }
}
