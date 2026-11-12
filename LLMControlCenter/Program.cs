using Avalonia;
using TWXProxy.Core;

internal static class Program
{
    private const string DefaultAgentId = "meridian";

    private static bool TryPrepareStartupArguments(
        string[] args,
        out string[] applicationArgs,
        out string mutexName,
        out string agentId,
        out string? teamId,
        out string? gameProfileFileName,
        out bool createAccountIfNeeded,
        out bool dockedSession,
        out string? error)
    {
        var remainingArgs = new List<string>(args.Length);
        string? instanceId = null;
        string? requestedAgentId = null;
        createAccountIfNeeded = false;
        dockedSession = false;
        teamId = null;
        gameProfileFileName = null;
        error = null;

        for (int index = 0; index < args.Length; index++)
        {
            string argument = args[index];
            if (string.Equals(
                    argument,
                    "--create-account-if-needed",
                    StringComparison.OrdinalIgnoreCase))
            {
                if (createAccountIfNeeded)
                {
                    error = "--create-account-if-needed may only be specified once.";
                    applicationArgs = [];
                    mutexName = string.Empty;
                    agentId = string.Empty;
                    return false;
                }

                createAccountIfNeeded = true;
                continue;
            }
            if (string.Equals(argument, "--docked-session", StringComparison.OrdinalIgnoreCase))
            {
                if (dockedSession)
                {
                    error = "--docked-session may only be specified once.";
                    applicationArgs = [];
                    mutexName = string.Empty;
                    agentId = string.Empty;
                    return false;
                }

                dockedSession = true;
                continue;
            }

            string? option = null;
            string? value = null;
            if (string.Equals(argument, "--instance-id", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(argument, "--agent-id", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(argument, "--team-id", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(argument, "--game-profile", StringComparison.OrdinalIgnoreCase))
            {
                option = argument.Equals("--instance-id", StringComparison.OrdinalIgnoreCase)
                    ? "--instance-id"
                    : argument.Equals("--agent-id", StringComparison.OrdinalIgnoreCase)
                        ? "--agent-id"
                        : argument.Equals("--team-id", StringComparison.OrdinalIgnoreCase)
                            ? "--team-id"
                            : "--game-profile";
                if (index + 1 >= args.Length)
                {
                    error = $"{option} requires an identifier.";
                    applicationArgs = [];
                    mutexName = string.Empty;
                    agentId = string.Empty;
                    return false;
                }

                value = args[++index];
            }
            else if (argument.StartsWith("--instance-id=", StringComparison.OrdinalIgnoreCase))
            {
                option = "--instance-id";
                value = argument["--instance-id=".Length..];
            }
            else if (argument.StartsWith("--agent-id=", StringComparison.OrdinalIgnoreCase))
            {
                option = "--agent-id";
                value = argument["--agent-id=".Length..];
            }
            else if (argument.StartsWith("--team-id=", StringComparison.OrdinalIgnoreCase))
            {
                option = "--team-id";
                value = argument["--team-id=".Length..];
            }
            else if (argument.StartsWith("--game-profile=", StringComparison.OrdinalIgnoreCase))
            {
                option = "--game-profile";
                value = argument["--game-profile=".Length..];
            }
            else
            {
                remainingArgs.Add(argument);
                continue;
            }

            if (option == "--game-profile"
                    ? !IsValidGameProfileFileName(value)
                    : !IsValidIdentity(value))
            {
                error = option == "--game-profile"
                    ? "--game-profile must be a .json filename in the MTC games directory."
                    : $"{option} must be 1-64 characters using letters, digits, '.', '_' or '-'.";
                applicationArgs = [];
                mutexName = string.Empty;
                agentId = string.Empty;
                return false;
            }

            if (option == "--instance-id")
            {
                if (instanceId is not null)
                {
                    error = "--instance-id may only be specified once.";
                    applicationArgs = [];
                    mutexName = string.Empty;
                    agentId = string.Empty;
                    return false;
                }

                instanceId = value;
            }
            else if (option == "--agent-id")
            {
                if (requestedAgentId is not null)
                {
                    error = "--agent-id may only be specified once.";
                    applicationArgs = [];
                    mutexName = string.Empty;
                    agentId = string.Empty;
                    return false;
                }

                requestedAgentId = value;
            }
            else if (option == "--team-id")
            {
                if (teamId is not null)
                {
                    error = "--team-id may only be specified once.";
                    applicationArgs = [];
                    mutexName = string.Empty;
                    agentId = string.Empty;
                    teamId = null;
                    return false;
                }

                teamId = value;
            }
            else
            {
                if (gameProfileFileName is not null)
                {
                    error = "--game-profile may only be specified once.";
                    applicationArgs = [];
                    mutexName = string.Empty;
                    agentId = string.Empty;
                    teamId = null;
                    gameProfileFileName = null;
                    return false;
                }

                gameProfileFileName = value;
            }
        }

        agentId = requestedAgentId ?? instanceId ?? DefaultAgentId;
        string? lockIdentity = instanceId ?? (requestedAgentId is null ? null : agentId);
        mutexName = lockIdentity is null
            ? "LLMControlCenter.SingleInstance"
            : $"LLMControlCenter.SingleInstance.{lockIdentity}";
        applicationArgs = remainingArgs.ToArray();
        return true;
    }

    private static bool IsValidIdentity(string? value) =>
        !string.IsNullOrWhiteSpace(value) &&
        !value.StartsWith("--", StringComparison.Ordinal) &&
        value.Length <= 64 &&
        value.All(character =>
            character is >= 'a' and <= 'z' or
                >= 'A' and <= 'Z' or
                >= '0' and <= '9' or
                '.' or '_' or '-');

    private static bool IsValidGameProfileFileName(string? value) =>
        !string.IsNullOrWhiteSpace(value) &&
        value.Length <= 200 &&
        string.Equals(Path.GetFileName(value), value, StringComparison.Ordinal) &&
        string.Equals(Path.GetExtension(value), ".json", StringComparison.OrdinalIgnoreCase) &&
        !value.Any(character => Path.GetInvalidFileNameChars().Contains(character));

    [STAThread]
    private static void Main(string[] args)
    {
        if (MTC.UnixAutoDetach.TryRelaunchDetached(args))
            return;

        if (!LLMSurvivingProxyAgent.Runtime.EpisodeCommandLine.TryExtract(
                args,
                out string[] nonEpisodeArgs,
                out IReadOnlyDictionary<string, string> episodeValues,
                out string? episodeError))
        {
            System.Diagnostics.Trace.WriteLine(episodeError);
            Environment.ExitCode = 2;
            return;
        }

        if (!TryPrepareStartupArguments(
                nonEpisodeArgs,
                out var applicationArgs,
                out var mutexName,
                out var agentId,
                out var teamId,
                out var gameProfileFileName,
                out var createAccountIfNeeded,
                out var dockedSession,
                out var argumentError))
        {
            System.Diagnostics.Trace.WriteLine(argumentError);
            Environment.ExitCode = 2;
            return;
        }

        args = applicationArgs;
        using var instanceMutex = new Mutex(
            initiallyOwned: true,
            name: mutexName,
            createdNew: out bool isFirstInstance);
        if (!isFirstInstance)
        {
            System.Diagnostics.Trace.WriteLine(
                $"LLMControlCenter instance '{mutexName}' is already running; refusing to create a competing game session.");
            return;
        }

        string agentMutexName = $"LLMControlCenter.Agent.{agentId.ToUpperInvariant()}";
        using var agentMutex = new Mutex(
            initiallyOwned: true,
            name: agentMutexName,
            createdNew: out bool isFirstAgentInstance);
        if (!isFirstAgentInstance)
        {
            System.Diagnostics.Trace.WriteLine(
                $"Agent '{agentId}' already has an MTC process; refusing to share its persistent state.");
            return;
        }

        Environment.SetEnvironmentVariable("LLMCONTROL_AGENT_ID", agentId);
        Environment.SetEnvironmentVariable("LLMCONTROL_TEAM_ID", teamId);
        Environment.SetEnvironmentVariable("LLMCONTROL_GAME_PROFILE", gameProfileFileName);
        Environment.SetEnvironmentVariable(
            "LLMCONTROL_CREATE_ACCOUNT_IF_NEEDED",
            createAccountIfNeeded ? "true" : null);
        Environment.SetEnvironmentVariable(
            "LLMCONTROL_DOCKED_SESSION",
            dockedSession ? "true" : null);
        LLMSurvivingProxyAgent.Runtime.EpisodeCommandLine.SetEnvironment(episodeValues);

        // MTC is a GUI application — suppress all Console output so diagnostic
        // Console.WriteLine calls in Core do not leak to the terminal.
        Console.SetOut(TextWriter.Null);

        var prefs = MTC.AppPreferences.Load();
        MTC.AppPaths.SetConfiguredProgramDir(prefs.ProgramDirectory);
        GlobalModules.ProgramDir = MTC.AppPaths.ProgramDir;
        GlobalModules.PreferPreparedVm = prefs.PreparedVmEnabled;
        GlobalModules.EnableVmMetrics = prefs.VmMetricsEnabled;
        MTC.AppPaths.EnsureDebugLogDir();
        var defaultDebug = new MTC.EmbeddedMtcDebugConfig();
        GlobalModules.ConfigureDebugLogging(
            MTC.AppPaths.GetDebugLogPath(),
            defaultDebug.DebugLoggingEnabled,
            defaultDebug.VerboseDebugLogging,
            defaultDebug.TriggerDebugLogging,
            defaultDebug.ScriptTraceDebugLogging,
            defaultDebug.AutoRecorderDebugLogging);
        GlobalModules.ConfigureHaggleDebugLogging(
            MTC.AppPaths.GetPortHaggleDebugLogPath(),
            defaultDebug.DebugPortHaggleEnabled,
            MTC.AppPaths.GetPlanetHaggleDebugLogPath(),
            defaultDebug.DebugPlanetHaggleEnabled);
        GlobalModules.ConfigureDatabaseCorrectionLogging(
            MTC.AppPaths.GetDatabaseCorrectionLogPath(),
            defaultDebug.DebugLoggingEnabled && defaultDebug.DebugDatabaseChanges);

        AppDomain.CurrentDomain.UnhandledException += (_, e) =>
        {
            try
            {
                GlobalModules.DebugLog($"[UnhandledException] {e.ExceptionObject}\n");
            }
            catch
            {
            }
        };

        TaskScheduler.UnobservedTaskException += (_, e) =>
        {
            try
            {
                GlobalModules.DebugLog($"[UnobservedTaskException] {e.Exception}\n");
            }
            catch
            {
            }
        };

        AppBuilder.Configure<MTC.App>()
            .UsePlatformDetect()
            .WithInterFont()
            .LogToTrace()
            .StartWithClassicDesktopLifetime(args);
    }
}
