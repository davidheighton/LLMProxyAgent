using System.Diagnostics;
using System.Reflection;
using LLMSurvivingProxyAgent.Core;

namespace MTC;

internal sealed class MtcAgentSessionLauncher(
    IAgentStore agentStore,
    MtcSessionWindowHost? sessionWindowHost = null) : IAgentSessionLauncher
{
    private const string GameProfileConfigurationKey = "Session:GameProfile";
    private const string TeamConfigurationKey = "Session:TeamId";
    private const string CreateAccountConfigurationKey = "Session:CreateAccountIfNeeded";
    private static readonly TimeSpan StartupConfirmationTimeout = TimeSpan.FromSeconds(10);

    public bool IsAvailable => true;

    public async Task<IReadOnlyList<AgentGameProfile>> GetGameProfilesAsync(
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (!Directory.Exists(AppPaths.TwxproxyGamesDir))
        {
            return [];
        }

        var profiles = new List<AgentGameProfile>();
        foreach (string path in Directory.EnumerateFiles(
                     AppPaths.TwxproxyGamesDir,
                     "*.json",
                     SearchOption.TopDirectoryOnly))
        {
            cancellationToken.ThrowIfCancellationRequested();
            EmbeddedGameConfig? config = await GameConfigService.LoadConfigAsync(path)
                .ConfigureAwait(false);
            if (config is null)
            {
                TWXProxy.Core.GlobalModules.DebugLog(
                    $"[AgentSessions] skipped unreadable game profile '{Path.GetFileName(path)}'.\n");
                continue;
            }

            if (string.IsNullOrWhiteSpace(config.Host) || config.Port is <= 0 or > 65535)
            {
                continue;
            }

            profiles.Add(new AgentGameProfile(
                Path.GetFileName(path),
                config.Name,
                config.Host,
                config.Port,
                TWXProxy.Core.GameFileLock.IsLockedByAnotherProcess(path)));
        }

        return profiles
            .OrderBy(profile => profile.Name, StringComparer.OrdinalIgnoreCase)
            .ThenBy(profile => profile.Id, StringComparer.OrdinalIgnoreCase)
            .ToArray();
    }

    public async Task<AgentSessionLaunchStatus> GetStatusAsync(
        string agentId,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(agentId);
        var agent = await agentStore.GetAgentAsync(agentId, cancellationToken).ConfigureAwait(false);
        if (agent is null)
        {
            throw new InvalidOperationException($"Agent '{agentId}' is not configured.");
        }

        string? profileId = agent.Configuration.GetValueOrDefault(GameProfileConfigurationKey);
        IReadOnlyList<AgentGameProfile> profiles = await GetGameProfilesAsync(cancellationToken)
            .ConfigureAwait(false);
        string? profileName = profiles
            .FirstOrDefault(profile =>
                string.Equals(profile.Id, profileId, StringComparison.OrdinalIgnoreCase))
            ?.Name;
        return new AgentSessionLaunchStatus(
            IsAgentProcessRunning(agentId),
            profileName ?? profileId,
            agent.Configuration.GetValueOrDefault(TeamConfigurationKey));
    }

    public async Task SaveAssignmentAsync(
        string agentId,
        string gameProfileId,
        string? teamId,
        bool createAccountIfNeeded = false,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(agentId);
        ArgumentException.ThrowIfNullOrWhiteSpace(gameProfileId);

        AgentGameProfile? profile = (await GetGameProfilesAsync(cancellationToken)
                .ConfigureAwait(false))
            .FirstOrDefault(candidate =>
                string.Equals(candidate.Id, gameProfileId, StringComparison.OrdinalIgnoreCase));
        if (profile is null)
        {
            throw new InvalidOperationException(
                "The selected game profile is no longer available. Refresh the profile list and try again.");
        }

        string normalizedTeamId = NormalizeTeamId(teamId);
        var agent = await agentStore.GetAgentAsync(agentId, cancellationToken).ConfigureAwait(false)
            ?? throw new InvalidOperationException($"Agent '{agentId}' is not configured.");
        SaveAssignment(agent, profile.Id, normalizedTeamId, createAccountIfNeeded);
        await agentStore.SaveAgentAsync(agent, cancellationToken).ConfigureAwait(false);
    }

    public async Task StartAsync(
        string agentId,
        string gameProfileId,
        string? teamId,
        bool createAccountIfNeeded = false,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(agentId);
        ArgumentException.ThrowIfNullOrWhiteSpace(gameProfileId);

        AgentGameProfile? profile = (await GetGameProfilesAsync(cancellationToken)
                .ConfigureAwait(false))
            .FirstOrDefault(candidate =>
                string.Equals(candidate.Id, gameProfileId, StringComparison.OrdinalIgnoreCase));
        if (profile is null)
        {
            throw new InvalidOperationException(
                "The selected game profile is no longer available. Refresh the profile list and try again.");
        }
        if (profile.IsInUse)
        {
            throw new InvalidOperationException(
                $"Game profile '{profile.Name}' is already open in an MTC process. " +
                "Choose a different profile or close the process that currently owns it.");
        }

        string normalizedTeamId = NormalizeTeamId(teamId);

        var agent = await agentStore.GetAgentAsync(agentId, cancellationToken).ConfigureAwait(false)
            ?? throw new InvalidOperationException($"Agent '{agentId}' is not configured.");
        if (IsAgentProcessRunning(agentId))
        {
            throw new InvalidOperationException(
                $"Agent '{agent.DisplayName}' already has a running MTC process.");
        }

        SaveAssignment(agent, profile.Id, normalizedTeamId, createAccountIfNeeded);
        await agentStore.SaveAgentAsync(agent, cancellationToken).ConfigureAwait(false);

        using Process process = Process.Start(BuildStartInfo(
                agentId,
                profile.Id,
                normalizedTeamId,
                createAccountIfNeeded))
            ?? throw new InvalidOperationException("Windows did not start the MTC agent process.");

        DateTimeOffset deadline = DateTimeOffset.UtcNow + StartupConfirmationTimeout;
        while (DateTimeOffset.UtcNow < deadline)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (IsAgentProcessRunning(agentId))
            {
                sessionWindowHost?.Register(agentId, profile.Name, process.Id);
                return;
            }

            if (process.HasExited)
            {
                throw new InvalidOperationException(
                    $"The MTC process for '{agent.DisplayName}' exited before claiming its session " +
                    $"(exit code {process.ExitCode}). Check the MTC debug log for startup errors.");
            }

            await Task.Delay(TimeSpan.FromMilliseconds(200), cancellationToken)
                .ConfigureAwait(false);
        }

        throw new InvalidOperationException(
            $"MTC started for '{agent.DisplayName}', but its agent session did not become ready " +
            $"within {StartupConfirmationTimeout.TotalSeconds:0} seconds. The process was left running; " +
            "check its window and debug log before retrying.");
    }

    private static ProcessStartInfo BuildStartInfo(
        string agentId,
        string gameProfileId,
        string teamId,
        bool createAccountIfNeeded)
    {
        string executablePath = Environment.ProcessPath
            ?? throw new InvalidOperationException("The current MTC executable path is unavailable.");
        var startInfo = new ProcessStartInfo
        {
            FileName = executablePath,
            WorkingDirectory = AppContext.BaseDirectory,
            UseShellExecute = true
        };

        if (string.Equals(
                Path.GetFileNameWithoutExtension(executablePath),
                "dotnet",
                StringComparison.OrdinalIgnoreCase))
        {
            string entryAssembly = Assembly.GetEntryAssembly()?.Location
                ?? throw new InvalidOperationException("The MTC entry assembly path is unavailable.");
            startInfo.ArgumentList.Add(entryAssembly);
        }

        startInfo.ArgumentList.Add("--instance-id");
        startInfo.ArgumentList.Add(agentId);
        startInfo.ArgumentList.Add("--docked-session");
        startInfo.ArgumentList.Add("--agent-id");
        startInfo.ArgumentList.Add(agentId);
        startInfo.ArgumentList.Add("--game-profile");
        startInfo.ArgumentList.Add(gameProfileId);
        if (!string.IsNullOrWhiteSpace(teamId))
        {
            startInfo.ArgumentList.Add("--team-id");
            startInfo.ArgumentList.Add(teamId);
        }
        if (createAccountIfNeeded)
        {
            startInfo.ArgumentList.Add("--create-account-if-needed");
        }

        return startInfo;
    }

    private static bool IsAgentProcessRunning(string agentId)
    {
        try
        {
            using Mutex mutex = Mutex.OpenExisting(
                $"LLMControlCenter.Agent.{agentId.ToUpperInvariant()}");
            return true;
        }
        catch (WaitHandleCannotBeOpenedException)
        {
            return false;
        }
    }

    private static bool IsValidTeamId(string value) =>
        value.Length <= 64 &&
        (value.Length == 0 || value.All(character =>
            character is >= 'a' and <= 'z' or
                >= 'A' and <= 'Z' or
                >= '0' and <= '9' or
                '.' or '_' or '-'));

    private static string NormalizeTeamId(string? teamId)
    {
        string normalizedTeamId = (teamId ?? string.Empty).Trim();
        if (!IsValidTeamId(normalizedTeamId))
        {
            throw new ArgumentException(
                "Team ID must be empty or 1-64 characters using letters, digits, '.', '_' or '-'.",
                nameof(teamId));
        }

        return normalizedTeamId;
    }

    private static void SaveAssignment(
        AgentContainer agent,
        string gameProfileId,
        string teamId,
        bool createAccountIfNeeded)
    {
        agent.Configuration[GameProfileConfigurationKey] = gameProfileId;
        agent.Configuration[TeamConfigurationKey] = teamId;
        agent.Configuration[CreateAccountConfigurationKey] =
            createAccountIfNeeded.ToString(System.Globalization.CultureInfo.InvariantCulture);
    }
}
