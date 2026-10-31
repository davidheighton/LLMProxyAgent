using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;
using LLMSurvivingProxyAgent.Core;
using LLMSurvivingProxyAgent.Runtime;

namespace MTC;

internal sealed record MtcServerPrompt(
    DateTimeOffset Timestamp,
    string Text,
    bool IsPresenceHeartbeat = false,
    DateTimeOffset? PromptObservedAt = null,
    bool IsCurrentPromptConfirmed = false);

internal sealed class MtcGameCommandSender(
    string agentId,
    Func<string, bool, Task<MtcRpcActionResult>> submitCommand,
    Func<string, Task<MtcRpcActionResult>> runWorldTradeScript,
    Func<string, Task<MtcRpcActionResult>> runCimScript,
    Func<Task<bool>> isWorldTradeModeRunning,
    Func<bool> isAtCommandPrompt,
    Func<DateTime> getLastPortCim,
    Func<DateTimeOffset, CancellationToken, Task> waitForCimCompletion,
    Func<IReadOnlyList<MtcServerPrompt>> getRecentServerPrompts,
    Func<bool> isNativeHaggleEnabled,
    bool commandsApproved,
    Func<string, Task<MtcRpcActionResult>>? runDockShopperScript = null,
    Func<Task<bool>>? isDockShopperRunning = null,
    Func<string, Task<MtcRpcActionResult>>? stopScriptByName = null,
    Func<IReadOnlyList<MtcServerPrompt>>? getRecentServerLines = null,
    Func<GameScriptInputPrompt?>? getCurrentScriptInputPrompt = null) :
    IGameCommandSender,
    IAutonomousGameLoginInputSender,
    IAutonomousGameScriptRunner,
    IAutonomousGameCimRefresher,
    IAutonomousPortTradePromptRecovery
{
    private const string WorldTradeScriptReference = "Pack1/1_Trade.ts";
    private const string DockShopperScriptReference = "ship-upgrades/ls-dock-shopper-v33.ts";
    private static readonly TimeSpan MaximumPromptAge = TimeSpan.FromSeconds(45);
    private static readonly TimeSpan PortTradeCommandQuietPeriod = TimeSpan.FromSeconds(2);
    private static readonly TimeSpan StarDockPromptTimeout = TimeSpan.FromSeconds(30);
    private static readonly TimeSpan RefurbQuoteTimeout = TimeSpan.FromSeconds(20);
    private static readonly TimeSpan QuickStatsTimeout = TimeSpan.FromSeconds(15);
    private static readonly TimeSpan MovementPromptObservationWindow = TimeSpan.FromSeconds(5);
    private static readonly TimeSpan MovementPromptCompletionTimeout = TimeSpan.FromSeconds(15);
    private static readonly Regex PortTradeQuantityPrompt = new(
        @"How many holds of\s+(?<product>Fuel Ore|Organics|Equipment)\s+do you want to\s+(?<action>buy|sell)\s+\[(?<quantity>[\d,]+)\]\s*\?",
        RegexOptions.Compiled | RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
    private static readonly Regex PortChoicePrompt = new(
        @"Enter your choice\s*\[T\]\s*\?",
        RegexOptions.Compiled | RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
    private static readonly Regex MoveSectorPrompt = new(
        @"Which\s+Sector\s*\[\s*\d+\s*\]\s*\?",
        RegexOptions.Compiled | RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
    private static readonly Regex CommandSectorPrompt = new(
        @"Command\s*\[\s*TL\s*=[^\]]*\]\s*:\s*\[\s*(?<sector>\d+)\s*\]",
        RegexOptions.Compiled | RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
    private static readonly Regex SectorDisplayLine = new(
        @"^\s*Sector\s*:\s*(?<sector>\d+)\s+in\s+(?<constellation>.+?)\s*\.?\s*$",
        RegexOptions.Compiled | RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
    private static readonly Regex BeaconDisplayLine = new(
        @"^\s*Beacon\s*:\s*(?<beacon>.*)$",
        RegexOptions.Compiled | RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
    private static readonly Regex FighterDeploymentQuantityPrompt = new(
        @"How many fighters do you want defending this sector\?",
        RegexOptions.Compiled | RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
    private static readonly Regex ShipFighterCountLine = new(
        @"^\s*Fighters\s*:\s*(?<count>[\d,]+)\s*$",
        RegexOptions.Compiled | RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
    private int _commandsApproved = commandsApproved ? 1 : 0;
    private int _loginInputApproved;
    private Func<string?>? _scriptPasswordProvider;
    private Action<Func<string?>?>? _setScriptPasswordProvider;

    public void SetCommandsApproved(bool approved) =>
        Volatile.Write(ref _commandsApproved, approved ? 1 : 0);

    public void ConfigureScriptPasswordProvider(
        Func<string?> passwordProvider,
        Action<Func<string?>?> setPasswordProvider)
    {
        ArgumentNullException.ThrowIfNull(passwordProvider);
        ArgumentNullException.ThrowIfNull(setPasswordProvider);
        _scriptPasswordProvider = passwordProvider;
        _setScriptPasswordProvider = setPasswordProvider;
    }

    public void SetLoginInputApproved(bool approved)
    {
        Volatile.Write(ref _loginInputApproved, approved ? 1 : 0);
        _setScriptPasswordProvider?.Invoke(approved ? _scriptPasswordProvider : null);
    }

    public async Task RecoverUntrackedPortTradeAsync(
        string currentAgentId,
        DateTimeOffset promptObservedAt,
        TradeProduct? followUpBuyProduct = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(currentAgentId);
        cancellationToken.ThrowIfCancellationRequested();

        if (!string.Equals(currentAgentId, agentId, StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException(
                $"Agent '{currentAgentId}' is not attached to the active MTC session.");
        }
        if (Volatile.Read(ref _commandsApproved) == 0)
        {
            throw new UnauthorizedAccessException(
                "Untracked port-trade recovery is disabled until MTC action approval is enabled.");
        }

        DateTimeOffset now = DateTimeOffset.UtcNow;
        MtcServerPrompt? currentPrompt = GetRecentPromptObservations()
            .Where(prompt => prompt.Timestamp <= now)
            .OrderByDescending(prompt => prompt.Timestamp)
            .FirstOrDefault();
        bool isQuantityPrompt = currentPrompt is not null &&
            TryParsePortTradeQuantityPrompt(currentPrompt.Text, out _, out _, out _);
        bool isOfferPrompt = currentPrompt?.Text.StartsWith(
            "Your offer",
            StringComparison.OrdinalIgnoreCase) == true;
        if (promptObservedAt > now ||
            now - promptObservedAt > MaximumPromptAge ||
            currentPrompt is null ||
            now - currentPrompt.Timestamp > MaximumPromptAge ||
            isAtCommandPrompt() ||
            (!isQuantityPrompt && !isOfferPrompt))
        {
            throw new InvalidOperationException(
                "MTC could not confirm a fresh port-trade prompt for safe recovery; no response was sent.");
        }

        await CompletePortTradeAsync(
            currentPrompt.Timestamp,
            requestedProduct: null,
            sellingExistingCargo: false,
            followUpBuyProduct,
            cancellationToken).ConfigureAwait(false);
    }

    public async Task SendLoginInputAsync(
        string currentAgentId,
        ReadOnlyMemory<byte> input,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(currentAgentId);
        cancellationToken.ThrowIfCancellationRequested();

        if (!string.Equals(currentAgentId, agentId, StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException(
                $"Agent '{currentAgentId}' is not attached to the active MTC session.");
        }
        if (Volatile.Read(ref _loginInputApproved) == 0)
        {
            throw new UnauthorizedAccessException(
                "Autonomous login input is disabled until the Meridian session is started.");
        }
        if (input.Length is < 1 or > 129)
        {
            throw new ArgumentOutOfRangeException(nameof(input), "Login input must be one short line.");
        }

        byte[] bytes = input.ToArray();
        for (int index = 0; index < bytes.Length; index++)
        {
            byte value = bytes[index];
            bool isTrailingEnter = value == (byte)'\r' && index == bytes.Length - 1;
            if (!isTrailingEnter && value is < 0x20 or > 0x7E)
            {
                throw new ArgumentException(
                    "Autonomous login input must contain printable ASCII and at most one trailing Enter.",
                    nameof(input));
            }
        }

        string line = Encoding.ASCII.GetString(bytes);
        MtcRpcActionResult result = await submitCommand(line, false)
            .WaitAsync(cancellationToken)
            .ConfigureAwait(false);
        if (!result.Success)
        {
            throw new InvalidOperationException(
                $"MTC rejected autonomous login input: {result.Message}");
        }
    }

    public Task SendAsync(
        string currentAgentId,
        GameCommand command,
        CancellationToken cancellationToken = default) =>
        SendAsync(
            currentAgentId,
            new GameCommandRequest(command),
            cancellationToken);

    public async Task SendAsync(
        string currentAgentId,
        GameCommandRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(currentAgentId);
        ArgumentNullException.ThrowIfNull(request);
        cancellationToken.ThrowIfCancellationRequested();

        if (!string.Equals(currentAgentId, agentId, StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException(
                $"Agent '{currentAgentId}' is not attached to the active MTC session.");
        }
        if (Volatile.Read(ref _commandsApproved) == 0)
        {
            throw new UnauthorizedAccessException(
                "LLM-issued game commands are disabled until MTC action approval is enabled.");
        }
        if (!Enum.IsDefined(request.Command))
        {
            throw new ArgumentOutOfRangeException(nameof(request));
        }

        if (request.Command == GameCommand.DeclineAutopilotEngagement)
        {
            EnsureCurrentAutopilotConfirmationPrompt();
        }

        if (request.Command == GameCommand.ExitNavPointSelection)
        {
            if (request.TargetSector is not null || request.Product is not null)
            {
                throw new ArgumentOutOfRangeException(nameof(request));
            }

            await ExitNavPointSelectionAsync(cancellationToken).ConfigureAwait(false);
            return;
        }

        if (request.Command == GameCommand.MoveToSector)
        {
            if (request.TargetSector is null ||
                request.TargetSector.Value is <= 0 or > 65535 ||
                request.Product is not null)
            {
                throw new ArgumentOutOfRangeException(nameof(request));
            }

            await MoveToSectorAsync(request.TargetSector.Value, cancellationToken)
                .ConfigureAwait(false);
            return;
        }

        if (request.Command == GameCommand.TradeAtCurrentPort)
        {
            if (request.TargetSector is not null || request.Product is not { } product)
            {
                throw new ArgumentOutOfRangeException(nameof(request));
            }

            await StartPortTradeAsync(
                product,
                request.SellingExistingCargo,
                request.FollowUpBuyProduct,
                cancellationToken).ConfigureAwait(false);
            return;
        }

        if (request.Command == GameCommand.RequestShipInformation &&
            request.TargetSector is null &&
            request.Product is null)
        {
            await RefreshShipInformationAsync(cancellationToken).ConfigureAwait(false);
            return;
        }

        if (request.Command == GameCommand.DeployOneFighterAtCurrentSector)
        {
            if (request.TargetSector is not > 0 ||
                request.Product is not null ||
                request.ExpectedShipFighterCount is not >= 2)
            {
                throw new ArgumentOutOfRangeException(nameof(request));
            }
            if (request.CurrentSectorIsFederationSpace is not false)
            {
                throw new InvalidOperationException(
                    "A prerequisite fighter cannot be deployed unless fresh sector data confirms the current sector is outside FedSpace.");
            }

            await DeployOneFighterAtCurrentSectorAsync(
                request.TargetSector.Value,
                request.ExpectedShipFighterCount.Value,
                cancellationToken).ConfigureAwait(false);
            return;
        }

        DateTimeOffset? autopilotDeclineSubmittedAt =
            request.Command == GameCommand.DeclineAutopilotEngagement
                ? DateTimeOffset.UtcNow
                : null;
        string command = request.Command switch
        {
            GameCommand.DisplayCurrentSector when request.TargetSector is null && request.Product is null => "D",
            GameCommand.FindStarDockSector when request.TargetSector is null && request.Product is null => "V",
            GameCommand.StopAtCurrentSector when request.TargetSector is null && request.Product is null => "Y",
            GameCommand.DeclineAutopilotEngagement when request.TargetSector is null && request.Product is null => "N",
            _ => throw new ArgumentOutOfRangeException(nameof(request)),
        };

        bool appendEnter = request.Command is not GameCommand.RequestShipInformation and
            not GameCommand.DisplayCurrentSector and
            not GameCommand.FindStarDockSector and
            not GameCommand.StopAtCurrentSector and
            not GameCommand.DeclineAutopilotEngagement;
        MtcRpcActionResult result = await submitCommand(command, appendEnter)
            .WaitAsync(cancellationToken)
            .ConfigureAwait(false);
        if (!result.Success)
        {
            throw new InvalidOperationException(
                $"MTC rejected the agent command: {result.Message}");
        }

        if (autopilotDeclineSubmittedAt is { } declineSubmittedAt)
        {
            await WaitForAutopilotDeclineCompletionAsync(
                declineSubmittedAt,
                cancellationToken).ConfigureAwait(false);
        }

    }

    public async Task<bool> RefreshShipInformationIfAtCommandPromptAsync(
        string currentAgentId,
        CancellationToken cancellationToken = default)
    {
        ValidateAgentId(currentAgentId);
        cancellationToken.ThrowIfCancellationRequested();
        if (!isAtCommandPrompt())
        {
            return false;
        }
        if (Volatile.Read(ref _commandsApproved) == 0)
        {
            throw new UnauthorizedAccessException(
                "Ship-status refresh is disabled until MTC action approval is enabled.");
        }

        await RefreshShipInformationAsync(cancellationToken).ConfigureAwait(false);
        return true;
    }

    private async Task RefreshShipInformationAsync(CancellationToken cancellationToken)
    {
        if (!isAtCommandPrompt())
        {
            throw new InvalidOperationException(
                "A ship-status refresh requires the TradeWars Command prompt.");
        }
        Func<IReadOnlyList<MtcServerPrompt>> getServerLines = getRecentServerLines ??
            throw new InvalidOperationException(
                "MTC does not provide recent server lines required to verify quick-status completion.");

        await SubmitAndWaitForCommandAsync(
            "I",
            "ship-information refresh",
            cancellationToken).ConfigureAwait(false);

        DateTimeOffset submittedAt = DateTimeOffset.UtcNow;
        MtcRpcActionResult quickStatsResult = await submitCommand("\x11/", false)
            .WaitAsync(cancellationToken)
            .ConfigureAwait(false);
        if (!quickStatsResult.Success)
        {
            throw new InvalidOperationException(
                $"MTC rejected the fixed quick-status request: {quickStatsResult.Message}");
        }

        await WaitForQuickStatsCompletionAsync(submittedAt, getServerLines, cancellationToken)
            .ConfigureAwait(false);

        await SubmitAndWaitForCommandAsync(
            "I",
            "final ship-information refresh",
            cancellationToken).ConfigureAwait(false);
    }

    private async Task DeployOneFighterAtCurrentSectorAsync(
        int expectedSector,
        int expectedShipFighterCount,
        CancellationToken cancellationToken)
    {
        if (!isAtCommandPrompt())
        {
            throw new InvalidOperationException(
                "A StarDock prerequisite fighter deployment requires the TradeWars Command prompt.");
        }

        Func<IReadOnlyList<MtcServerPrompt>> getServerLines = getRecentServerLines ??
            throw new InvalidOperationException(
                "MTC cannot verify a prerequisite fighter deployment without recent server lines.");
        DateTimeOffset now = DateTimeOffset.UtcNow;
        IReadOnlyList<MtcServerPrompt> recentPrompts = GetRecentPromptObservations()
            .Where(prompt => prompt.Timestamp <= now)
            .OrderByDescending(prompt => prompt.Timestamp)
            .ToArray();
        MtcServerPrompt? latestPrompt = recentPrompts.FirstOrDefault();
        MtcServerPrompt? latestSectorPrompt = recentPrompts.FirstOrDefault(prompt =>
            CommandSectorPrompt.IsMatch(prompt.Text));
        Match currentCommand = latestSectorPrompt is not null
            ? CommandSectorPrompt.Match(latestSectorPrompt.Text)
            : Match.Empty;
        bool latestPromptIsCommand =
            latestPrompt is not null &&
            TradeWarsTelnetTelemetryDecoder.TryGetPromptSurface(
                latestPrompt.Text,
                out string latestPromptSurface) &&
            string.Equals(latestPromptSurface, "Command", StringComparison.OrdinalIgnoreCase);
        IReadOnlyList<MtcServerPrompt> recentServerLines = getServerLines();
        if (latestPrompt is null ||
            now - latestPrompt.Timestamp > MaximumPromptAge ||
            !latestPromptIsCommand ||
            latestSectorPrompt is null ||
            now - latestSectorPrompt.Timestamp > MaximumPromptAge ||
            !currentCommand.Success ||
            !int.TryParse(
                currentCommand.Groups["sector"].Value,
                NumberStyles.None,
                CultureInfo.InvariantCulture,
                out int currentSector) ||
            currentSector != expectedSector ||
            expectedShipFighterCount < 2)
        {
            throw new InvalidOperationException(
                "A one-fighter deployment requires a fresh Command prompt in the expected StarDock sector and at least two verified ship fighters.");
        }

        if (!IsFreshNonFederationSectorDisplay(
                recentServerLines,
                expectedSector,
                latestSectorPrompt!.Timestamp,
                now))
        {
            throw new InvalidOperationException(
                "A one-fighter deployment requires fresh sector-display evidence proving the sector is outside FedSpace; no input was sent.");
        }

        DateTimeOffset menuSubmittedAt = DateTimeOffset.UtcNow;
        MtcRpcActionResult menuResult = await submitCommand("F", false)
            .WaitAsync(cancellationToken)
            .ConfigureAwait(false);
        if (!menuResult.Success)
        {
            throw new InvalidOperationException(
                $"MTC rejected the fixed StarDock fighter-menu request: {menuResult.Message}");
        }

        await WaitForFighterDeploymentQuantityPromptAsync(
            menuSubmittedAt,
            getServerLines,
            cancellationToken).ConfigureAwait(false);

        DateTimeOffset deploymentSubmittedAt = DateTimeOffset.UtcNow;
        MtcRpcActionResult deploymentResult = await submitCommand("1", true)
            .WaitAsync(cancellationToken)
            .ConfigureAwait(false);
        if (!deploymentResult.Success)
        {
            throw new InvalidOperationException(
                $"MTC rejected the one-fighter StarDock deployment: {deploymentResult.Message}");
        }

        await WaitForCommandAfterMovementResponseAsync(
            deploymentSubmittedAt,
            "one-fighter StarDock deployment",
            cancellationToken).ConfigureAwait(false);

        DateTimeOffset statusRefreshAt = DateTimeOffset.UtcNow;
        await RefreshShipInformationAsync(cancellationToken).ConfigureAwait(false);
        int? refreshedFighterCount = getServerLines()
            .Where(line => line.Timestamp >= statusRefreshAt)
            .Select(line => TryParseShipFighterCount(line.Text))
            .LastOrDefault(count => count is not null);
        if (refreshedFighterCount != expectedShipFighterCount - 1)
        {
            throw new InvalidOperationException(
                $"The one-fighter StarDock deployment was not verified: expected {expectedShipFighterCount - 1} ship fighters after deployment, observed {refreshedFighterCount?.ToString(CultureInfo.InvariantCulture) ?? "no fresh fighter count"}.");
        }

        DateTimeOffset sectorRefreshAt = DateTimeOffset.UtcNow;
        MtcRpcActionResult sectorRefreshResult = await submitCommand("D", false)
            .WaitAsync(cancellationToken)
            .ConfigureAwait(false);
        if (!sectorRefreshResult.Success)
        {
            throw new InvalidOperationException(
                $"MTC rejected the StarDock sector refresh after fighter deployment: {sectorRefreshResult.Message}");
        }

        await WaitForCommandAfterMovementResponseAsync(
            sectorRefreshAt,
            "StarDock sector refresh after fighter deployment",
            cancellationToken).ConfigureAwait(false);
    }

    private async Task WaitForFighterDeploymentQuantityPromptAsync(
        DateTimeOffset submittedAt,
        Func<IReadOnlyList<MtcServerPrompt>> getServerLines,
        CancellationToken cancellationToken)
    {
        DateTimeOffset deadline = DateTimeOffset.UtcNow + StarDockPromptTimeout;
        while (DateTimeOffset.UtcNow < deadline)
        {
            cancellationToken.ThrowIfCancellationRequested();
            bool quantityPromptObserved =
                getServerLines().Any(line =>
                    line.Timestamp >= submittedAt &&
                    FighterDeploymentQuantityPrompt.IsMatch(line.Text)) ||
                GetRecentPromptObservations().Any(prompt =>
                    prompt.Timestamp >= submittedAt &&
                    FighterDeploymentQuantityPrompt.IsMatch(prompt.Text));
            if (quantityPromptObserved)
            {
                return;
            }

            await Task.Delay(TimeSpan.FromMilliseconds(100), cancellationToken)
                .ConfigureAwait(false);
        }

        throw new TimeoutException(
            $"MTC did not observe the fighter-count prompt within {StarDockPromptTimeout.TotalSeconds:N0} seconds; no deployment quantity was sent.");
    }

    private static bool IsFreshNonFederationSectorDisplay(
        IReadOnlyList<MtcServerPrompt> recentServerLines,
        int expectedSector,
        DateTimeOffset commandPromptAt,
        DateTimeOffset now)
    {
        MtcServerPrompt? sectorLine = recentServerLines
            .Where(line =>
                line.Timestamp <= commandPromptAt &&
                line.Timestamp <= now &&
                now - line.Timestamp <= MaximumPromptAge)
            .OrderByDescending(line => line.Timestamp)
            .FirstOrDefault(line =>
            {
                Match match = SectorDisplayLine.Match(line.Text);
                return match.Success &&
                    int.TryParse(
                        match.Groups["sector"].Value,
                        NumberStyles.None,
                        CultureInfo.InvariantCulture,
                        out int sector) &&
                    sector == expectedSector;
            });
        if (sectorLine is null)
        {
            return false;
        }

        Match sectorMatch = SectorDisplayLine.Match(sectorLine.Text);
        string constellation = sectorMatch.Groups["constellation"].Value.Trim();
        if (string.IsNullOrWhiteSpace(constellation) ||
            constellation.StartsWith("???", StringComparison.OrdinalIgnoreCase) ||
            constellation.Contains("uncharted", StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        MtcServerPrompt? beaconLine = recentServerLines
            .Where(line =>
                line.Timestamp >= sectorLine.Timestamp &&
                line.Timestamp <= commandPromptAt &&
                line.Timestamp <= now &&
                now - line.Timestamp <= MaximumPromptAge)
            .OrderByDescending(line => line.Timestamp)
            .FirstOrDefault(line => BeaconDisplayLine.IsMatch(line.Text));
        if (beaconLine is null)
        {
            return false;
        }

        string beacon = BeaconDisplayLine.Match(beaconLine.Text).Groups["beacon"].Value;
        return !constellation.Contains("Federation", StringComparison.OrdinalIgnoreCase) &&
            !beacon.Contains("FedSpace", StringComparison.OrdinalIgnoreCase) &&
            !beacon.Contains("FedLaw Enforced", StringComparison.OrdinalIgnoreCase);
    }

    private static int? TryParseShipFighterCount(string line)
    {
        Match match = ShipFighterCountLine.Match(line);
        return match.Success &&
               int.TryParse(
                   match.Groups["count"].Value,
                   NumberStyles.AllowThousands,
                   CultureInfo.InvariantCulture,
                   out int count)
            ? count
            : null;
    }

    private async Task SubmitAndWaitForCommandAsync(
        string input,
        string purpose,
        CancellationToken cancellationToken)
    {
        DateTimeOffset submittedAt = DateTimeOffset.UtcNow;
        MtcRpcActionResult result = await submitCommand(input, false)
            .WaitAsync(cancellationToken)
            .ConfigureAwait(false);
        if (!result.Success)
        {
            throw new InvalidOperationException(
                $"MTC rejected the fixed {purpose}: {result.Message}");
        }

        await WaitForCommandAfterMovementResponseAsync(
            submittedAt,
            purpose,
            cancellationToken).ConfigureAwait(false);
    }

    private async Task WaitForQuickStatsCompletionAsync(
        DateTimeOffset submittedAt,
        Func<IReadOnlyList<MtcServerPrompt>> getServerLines,
        CancellationToken cancellationToken)
    {
        DateTimeOffset deadline = DateTimeOffset.UtcNow + QuickStatsTimeout;
        while (DateTimeOffset.UtcNow < deadline)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (getServerLines().Any(line =>
                    line.Timestamp >= submittedAt &&
                    line.Text.Contains('\u00B3') &&
                    line.Text.Contains("Ship ", StringComparison.Ordinal)))
            {
                return;
            }

            await Task.Delay(TimeSpan.FromMilliseconds(100), cancellationToken)
                .ConfigureAwait(false);
        }

        throw new TimeoutException(
            $"MTC did not observe the terminal quick-status line within {QuickStatsTimeout.TotalSeconds:N0} seconds.");
    }

    private async Task MoveToSectorAsync(
        int targetSector,
        CancellationToken cancellationToken)
    {
        if (!isAtCommandPrompt())
        {
            throw new InvalidOperationException(
                "A sector move requires the TradeWars Command prompt.");
        }

        DateTimeOffset moveCommandSubmittedAt = DateTimeOffset.UtcNow;
        MtcRpcActionResult moveResult = await submitCommand("M", false)
            .WaitAsync(cancellationToken)
            .ConfigureAwait(false);
        if (!moveResult.Success)
        {
            throw new InvalidOperationException(
                $"MTC rejected the move command: {moveResult.Message}");
        }

        await WaitForMoveSectorPromptAsync(
            moveCommandSubmittedAt,
            cancellationToken).ConfigureAwait(false);

        DateTimeOffset targetSubmittedAt = DateTimeOffset.UtcNow;
        MtcRpcActionResult targetResult = await submitCommand(
                targetSector.ToString(CultureInfo.InvariantCulture),
                true)
            .WaitAsync(cancellationToken)
            .ConfigureAwait(false);
        if (!targetResult.Success)
        {
            throw new InvalidOperationException(
                $"MTC rejected the movement destination: {targetResult.Message}");
        }

        await PreventMultiHopMovementAsync(
            targetSubmittedAt,
            cancellationToken).ConfigureAwait(false);
    }

    private async Task WaitForMoveSectorPromptAsync(
        DateTimeOffset submittedAt,
        CancellationToken cancellationToken)
    {
        DateTimeOffset deadline = DateTimeOffset.UtcNow + StarDockPromptTimeout;
        DateTimeOffset lastPromptAt = submittedAt.AddTicks(-1);
        while (DateTimeOffset.UtcNow < deadline)
        {
            cancellationToken.ThrowIfCancellationRequested();
            MtcServerPrompt? prompt = GetRecentPromptObservations()
                .Where(candidate =>
                    candidate.Timestamp >= submittedAt &&
                    candidate.Timestamp > lastPromptAt)
                .OrderBy(candidate => candidate.Timestamp)
                .FirstOrDefault();
            if (prompt is null)
            {
                await Task.Delay(TimeSpan.FromMilliseconds(100), cancellationToken)
                    .ConfigureAwait(false);
                continue;
            }

            lastPromptAt = prompt.Timestamp;
            if (MoveSectorPrompt.IsMatch(prompt.Text) ||
                TradeWarsTelnetTelemetryDecoder.TryGetPromptSurface(
                    prompt.Text,
                    out string moveSurface) &&
                string.Equals(moveSurface, "MoveSector", StringComparison.OrdinalIgnoreCase))
            {
                return;
            }

            if (TradeWarsTelnetTelemetryDecoder.TryGetPromptSurface(
                    prompt.Text,
                    out string surface) &&
                (string.Equals(surface, "AutopilotConfirmation", StringComparison.OrdinalIgnoreCase) ||
                 string.Equals(surface, "StopInSector", StringComparison.OrdinalIgnoreCase)))
            {
                await AbortActionForMovementPromptAsync(
                    "sector movement",
                    surface,
                    cancellationToken).ConfigureAwait(false);
            }
        }

        throw new TimeoutException(
            $"MTC did not observe the movement destination prompt within {StarDockPromptTimeout.TotalSeconds:N0} seconds.");
    }

    private async Task StartPortTradeAsync(
        TradeProduct requestedProduct,
        bool sellingExistingCargo,
        TradeProduct? followUpBuyProduct,
        CancellationToken cancellationToken)
    {
        DateTimeOffset now = DateTimeOffset.UtcNow;
        MtcServerPrompt? latestPrompt = GetRecentPromptObservations()
            .Where(prompt => prompt.Timestamp <= now)
            .OrderByDescending(prompt => prompt.Timestamp)
            .FirstOrDefault();
        bool hasFreshCommandPrompt = latestPrompt is not null &&
            now - latestPrompt.Timestamp <= MaximumPromptAge &&
            TradeWarsTelnetTelemetryDecoder.TryGetPromptSurface(
                latestPrompt.Text,
                out string promptSurface) &&
            string.Equals(promptSurface, "Command", StringComparison.OrdinalIgnoreCase);
        if (!isAtCommandPrompt() || !hasFreshCommandPrompt)
        {
            throw new InvalidOperationException(
                "A port trade requires a fresh TradeWars Command prompt; no trade input was sent.");
        }

        DateTimeOffset portCommandSubmittedAt = DateTimeOffset.UtcNow;
        await SubmitPortTradeKeyAsync("P", cancellationToken).ConfigureAwait(false);
        await WaitForPortChoicePromptAsync(
            portCommandSubmittedAt,
            cancellationToken).ConfigureAwait(false);

        DateTimeOffset tradeSubmittedAt = DateTimeOffset.UtcNow;
        await SubmitPortTradeKeyAsync("T", cancellationToken).ConfigureAwait(false);
        await CompletePortTradeAsync(
            tradeSubmittedAt,
            requestedProduct,
            sellingExistingCargo,
            followUpBuyProduct,
            cancellationToken).ConfigureAwait(false);
    }

    private async Task WaitForPortChoicePromptAsync(
        DateTimeOffset submittedAt,
        CancellationToken cancellationToken)
    {
        DateTimeOffset deadline = DateTimeOffset.UtcNow + StarDockPromptTimeout;
        DateTimeOffset lastPromptAt = submittedAt.AddTicks(-1);
        while (DateTimeOffset.UtcNow < deadline)
        {
            cancellationToken.ThrowIfCancellationRequested();
            MtcServerPrompt? prompt = GetRecentPromptObservations()
                .Where(candidate =>
                    candidate.Timestamp >= submittedAt &&
                    candidate.Timestamp > lastPromptAt)
                .OrderBy(candidate => candidate.Timestamp)
                .FirstOrDefault();
            if (prompt is null)
            {
                await Task.Delay(TimeSpan.FromMilliseconds(100), cancellationToken)
                    .ConfigureAwait(false);
                continue;
            }

            lastPromptAt = prompt.Timestamp;
            if (TradeWarsTelnetTelemetryDecoder.TryGetPromptSurface(
                    prompt.Text,
                    out string surface) &&
                (string.Equals(surface, "AutopilotConfirmation", StringComparison.OrdinalIgnoreCase) ||
                 string.Equals(surface, "StopInSector", StringComparison.OrdinalIgnoreCase)))
            {
                await AbortActionForMovementPromptAsync(
                    "port trade",
                    surface,
                    cancellationToken).ConfigureAwait(false);
            }

            if (PortChoicePrompt.IsMatch(prompt.Text))
            {
                return;
            }
        }

        throw new TimeoutException(
            $"MTC did not observe the port trade choice prompt within {StarDockPromptTimeout.TotalSeconds:N0} seconds.");
    }

    private async Task SubmitPortTradeKeyAsync(
        string input,
        CancellationToken cancellationToken)
    {
        MtcRpcActionResult result = await submitCommand(input, false)
            .WaitAsync(cancellationToken)
            .ConfigureAwait(false);
        if (!result.Success)
        {
            throw new InvalidOperationException(
                $"MTC rejected the port trade menu input: {result.Message}");
        }
    }

    private async Task ExitNavPointSelectionAsync(CancellationToken cancellationToken)
    {
        EnsureCurrentNavPointSelectionPrompt();
        DateTimeOffset submittedAt = DateTimeOffset.UtcNow;
        MtcRpcActionResult result = await submitCommand("Q", true)
            .WaitAsync(cancellationToken)
            .ConfigureAwait(false);
        if (!result.Success)
        {
            throw new InvalidOperationException(
                $"MTC rejected the NavPoint selection exit: {result.Message}");
        }

        await WaitForNavPointSelectionExitAsync(submittedAt, cancellationToken)
            .ConfigureAwait(false);
    }

    private void EnsureCurrentNavPointSelectionPrompt()
    {
        DateTimeOffset now = DateTimeOffset.UtcNow;
        MtcServerPrompt? latestPrompt = GetRecentPromptObservations()
            .Where(prompt => prompt.Timestamp <= now)
            .OrderByDescending(prompt => prompt.Timestamp)
            .FirstOrDefault();
        if (latestPrompt is null ||
            now - latestPrompt.Timestamp > MaximumPromptAge ||
            !TradeWarsTelnetTelemetryDecoder.TryGetPromptSurface(latestPrompt.Text, out string surface) ||
            !string.Equals(surface, "NavPointSelection", StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException(
                "NavPoint selection was not exited because the latest live prompt is not a fresh NavPoint selection.");
        }
    }

    private async Task WaitForNavPointSelectionExitAsync(
        DateTimeOffset submittedAt,
        CancellationToken cancellationToken)
    {
        DateTimeOffset deadline = DateTimeOffset.UtcNow.Add(MovementPromptCompletionTimeout);
        while (DateTimeOffset.UtcNow < deadline)
        {
            cancellationToken.ThrowIfCancellationRequested();
            MtcServerPrompt? prompt = GetRecentPromptObservations()
                .Where(candidate => candidate.Timestamp >= submittedAt)
                .OrderBy(candidate => candidate.Timestamp)
                .FirstOrDefault();
            if (prompt is null)
            {
                await Task.Delay(TimeSpan.FromMilliseconds(100), cancellationToken)
                    .ConfigureAwait(false);
                continue;
            }

            if (TradeWarsTelnetTelemetryDecoder.TryGetPromptSurface(prompt.Text, out string surface) &&
                string.Equals(surface, "Command", StringComparison.OrdinalIgnoreCase))
            {
                return;
            }

            if (!TradeWarsTelnetTelemetryDecoder.TryGetPromptSurface(prompt.Text, out surface) ||
                !string.Equals(surface, "NavPointSelection", StringComparison.OrdinalIgnoreCase))
            {
                throw new InvalidOperationException(
                    $"NavPoint selection exit did not return to Command; the latest prompt is '{surface}'.");
            }

            await Task.Delay(TimeSpan.FromMilliseconds(100), cancellationToken)
                .ConfigureAwait(false);
        }

        throw new TimeoutException(
            "MTC did not observe the TradeWars Command prompt after exiting NavPoint selection.");
    }

    internal static bool TryParsePortTradeQuantityPrompt(
        string prompt,
        out TradeProduct product,
        out bool sellingExistingCargo,
        out int maximumQuantity)
    {
        ArgumentNullException.ThrowIfNull(prompt);
        Match match = PortTradeQuantityPrompt.Match(prompt);
        product = match.Success && Enum.TryParse(
            match.Groups["product"].Value.Replace(" ", string.Empty, StringComparison.Ordinal),
            ignoreCase: true,
            out product)
            ? product
            : default;
        sellingExistingCargo = match.Success &&
            string.Equals(match.Groups["action"].Value, "sell", StringComparison.OrdinalIgnoreCase);
        maximumQuantity = match.Success &&
            int.TryParse(
                match.Groups["quantity"].Value.Replace(",", string.Empty, StringComparison.Ordinal),
                NumberStyles.None,
                CultureInfo.InvariantCulture,
                out int parsedQuantity)
            ? parsedQuantity
            : 0;
        return match.Success && Enum.IsDefined(product) && maximumQuantity >= 0;
    }

    private void EnsureCurrentAutopilotConfirmationPrompt()
    {
        DateTimeOffset now = DateTimeOffset.UtcNow;
        MtcServerPrompt? latestPrompt = GetRecentPromptObservations()
            .Where(prompt => prompt.Timestamp <= now)
            .OrderByDescending(prompt => prompt.Timestamp)
            .FirstOrDefault();
        if (latestPrompt is null ||
            now - latestPrompt.Timestamp > MaximumPromptAge ||
            !TradeWarsTelnetTelemetryDecoder.TryGetPromptSurface(latestPrompt.Text, out string surface) ||
            !string.Equals(surface, "AutopilotConfirmation", StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException(
                "Autopilot decline was not sent because the latest live prompt is not a fresh autopilot confirmation.");
        }
    }

    private IReadOnlyList<MtcServerPrompt> GetRecentPromptObservations()
    {
        DateTimeOffset now = DateTimeOffset.UtcNow;
        return getRecentServerPrompts()
            .Select(prompt =>
            {
                DateTimeOffset? observedAt = prompt.IsPresenceHeartbeat
                    ? prompt.IsCurrentPromptConfirmed
                        ? prompt.Timestamp
                        : prompt.PromptObservedAt
                    : prompt.Timestamp;
                return observedAt is { } timestamp && timestamp <= now
                    ? prompt with { Timestamp = timestamp }
                    : null;
            })
            .OfType<MtcServerPrompt>()
            .ToArray();
    }

    private async Task WaitForAutopilotDeclineCompletionAsync(
        DateTimeOffset submittedAt,
        CancellationToken cancellationToken)
    {
        DateTimeOffset deadline = DateTimeOffset.UtcNow.AddSeconds(15);
        DateTimeOffset lastHandledPromptAt = submittedAt.AddTicks(-1);
        while (DateTimeOffset.UtcNow < deadline)
        {
            cancellationToken.ThrowIfCancellationRequested();
            MtcServerPrompt? prompt = GetRecentPromptObservations()
                .Where(candidate =>
                    candidate.Timestamp >= submittedAt &&
                    candidate.Timestamp > lastHandledPromptAt)
                .OrderBy(candidate => candidate.Timestamp)
                .FirstOrDefault();
            if (prompt is null)
            {
                await Task.Delay(TimeSpan.FromMilliseconds(100), cancellationToken)
                    .ConfigureAwait(false);
                continue;
            }

            lastHandledPromptAt = prompt.Timestamp;
            if (TradeWarsTelnetTelemetryDecoder.TryGetPromptSurface(prompt.Text, out string surface) &&
                string.Equals(surface, "Command", StringComparison.OrdinalIgnoreCase))
            {
                return;
            }

            if (TradeWarsTelnetTelemetryDecoder.TryGetPromptSurface(prompt.Text, out surface) &&
                string.Equals(surface, "AutopilotConfirmation", StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            throw new InvalidOperationException(
                $"Autopilot decline did not return to Command; the latest prompt is '{surface}'.");
        }

        throw new TimeoutException(
            "MTC did not observe a fresh Command prompt after declining autopilot engagement.");
    }

    private async Task PreventMultiHopMovementAsync(
        DateTimeOffset submittedAt,
        CancellationToken cancellationToken)
    {
        DateTimeOffset deadline = DateTimeOffset.UtcNow + MovementPromptObservationWindow;
        DateTimeOffset lastPromptAt = submittedAt.AddTicks(-1);
        while (DateTimeOffset.UtcNow < deadline)
        {
            cancellationToken.ThrowIfCancellationRequested();
            MtcServerPrompt? prompt = GetRecentPromptObservations()
                .Where(candidate =>
                    candidate.Timestamp >= submittedAt &&
                    candidate.Timestamp > lastPromptAt)
                .OrderBy(candidate => candidate.Timestamp)
                .FirstOrDefault();
            if (prompt is null)
            {
                await Task.Delay(TimeSpan.FromMilliseconds(100), cancellationToken)
                    .ConfigureAwait(false);
                continue;
            }

            lastPromptAt = prompt.Timestamp;
            if (!TradeWarsTelnetTelemetryDecoder.TryGetPromptSurface(
                    prompt.Text,
                    out string promptSurface))
            {
                continue;
            }

            if (string.Equals(
                    promptSurface,
                    "AutopilotConfirmation",
                    StringComparison.OrdinalIgnoreCase))
            {
                await SubmitMovementPromptResponseAsync(
                    "N",
                    "decline multi-hop autopilot",
                    cancellationToken).ConfigureAwait(false);
                return;
            }

            if (string.Equals(
                    promptSurface,
                    "StopInSector",
                    StringComparison.OrdinalIgnoreCase))
            {
                await SubmitMovementPromptResponseAsync(
                    "Y",
                    "stop at the current sector",
                    cancellationToken).ConfigureAwait(false);
                return;
            }

            if (string.Equals(promptSurface, "MoveSector", StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            if (string.Equals(promptSurface, "Command", StringComparison.OrdinalIgnoreCase))
            {
                return;
            }

            throw new InvalidOperationException(
                $"Movement stopped at an unexpected prompt: '{prompt.Text}'.");
        }

        throw new TimeoutException(
            "MTC did not observe a fresh movement result prompt after submitting the destination.");
    }

    private async Task SubmitMovementPromptResponseAsync(
        string response,
        string purpose,
        CancellationToken cancellationToken)
    {
        if (Volatile.Read(ref _commandsApproved) == 0)
        {
            throw new UnauthorizedAccessException(
                $"MTC could not {purpose} because game actions were disabled.");
        }

        DateTimeOffset submittedAt = DateTimeOffset.UtcNow;
        MtcRpcActionResult result = await submitCommand(response, false)
            .WaitAsync(cancellationToken)
            .ConfigureAwait(false);
        if (!result.Success)
        {
            throw new InvalidOperationException(
                $"MTC rejected the response to {purpose}: {result.Message}");
        }

        await WaitForCommandAfterMovementResponseAsync(
            submittedAt,
            purpose,
            cancellationToken).ConfigureAwait(false);
    }

    private async Task AbortActionForMovementPromptAsync(
        string action,
        string promptSurface,
        CancellationToken cancellationToken)
    {
        bool isAutopilotPrompt = string.Equals(
            promptSurface,
            "AutopilotConfirmation",
            StringComparison.OrdinalIgnoreCase);
        await SubmitMovementPromptResponseAsync(
            isAutopilotPrompt ? "N" : "Y",
            isAutopilotPrompt
                ? $"decline autopilot during {action}"
                : $"stop at the current sector during {action}",
            cancellationToken).ConfigureAwait(false);
        throw new InvalidOperationException(
            $"{action} was interrupted by a {promptSurface} prompt; the movement prompt was handled safely and the action must be verified before retrying.");
    }

    private async Task WaitForCommandAfterMovementResponseAsync(
        DateTimeOffset submittedAt,
        string purpose,
        CancellationToken cancellationToken)
    {
        DateTimeOffset deadline = DateTimeOffset.UtcNow + MovementPromptCompletionTimeout;
        DateTimeOffset lastPromptAt = submittedAt.AddTicks(-1);
        while (DateTimeOffset.UtcNow < deadline)
        {
            cancellationToken.ThrowIfCancellationRequested();
            MtcServerPrompt? prompt = GetRecentPromptObservations()
                .Where(candidate =>
                    candidate.Timestamp >= submittedAt &&
                    candidate.Timestamp > lastPromptAt)
                .OrderBy(candidate => candidate.Timestamp)
                .FirstOrDefault();
            if (prompt is null)
            {
                await Task.Delay(TimeSpan.FromMilliseconds(100), cancellationToken)
                    .ConfigureAwait(false);
                continue;
            }

            lastPromptAt = prompt.Timestamp;
            if (!TradeWarsTelnetTelemetryDecoder.TryGetPromptSurface(
                    prompt.Text,
                    out string promptSurface))
            {
                continue;
            }
            if (string.Equals(promptSurface, "Command", StringComparison.OrdinalIgnoreCase))
            {
                return;
            }
            if (string.Equals(promptSurface, "AutopilotConfirmation", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(promptSurface, "StopInSector", StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            throw new InvalidOperationException(
                $"MTC did not return to Command after {purpose}; the latest prompt is '{promptSurface}'.");
        }

        throw new TimeoutException(
            $"MTC did not observe a fresh Command prompt after {purpose} within {MovementPromptCompletionTimeout.TotalSeconds:N0} seconds.");
    }

    private async Task CompletePortTradeAsync(
        DateTimeOffset submittedAt,
        TradeProduct? requestedProduct,
        bool sellingExistingCargo,
        TradeProduct? followUpBuyProduct,
        CancellationToken cancellationToken)
    {
        DateTimeOffset deadline = DateTimeOffset.UtcNow.AddMinutes(2);
        DateTimeOffset lastHandledPromptAt = submittedAt.AddTicks(-1);
        bool requestedTradeSelected = false;
        bool followUpTradeSelected = false;
        bool sawQuantityPrompt = false;
        DateTimeOffset? commandPromptObservedAt = null;
        var handledQuantityPrompts = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        while (DateTimeOffset.UtcNow < deadline)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (Volatile.Read(ref _commandsApproved) == 0)
            {
                throw new UnauthorizedAccessException(
                    "Port trade prompt handling stopped because MTC action approval was disabled.");
            }

            MtcServerPrompt? prompt = GetRecentPromptObservations()
                .Where(candidate =>
                    candidate.Timestamp >= submittedAt &&
                    candidate.Timestamp > lastHandledPromptAt)
                .OrderBy(candidate => candidate.Timestamp)
                .FirstOrDefault();
            if (prompt is null)
            {
                if (sawQuantityPrompt &&
                    commandPromptObservedAt is { } commandPromptAt &&
                    DateTimeOffset.UtcNow - commandPromptAt >= PortTradeCommandQuietPeriod)
                {
                    return;
                }

                await Task.Delay(TimeSpan.FromMilliseconds(100), cancellationToken)
                    .ConfigureAwait(false);
                continue;
            }

            lastHandledPromptAt = prompt.Timestamp;
            if (TradeWarsTelnetTelemetryDecoder.TryGetPromptSurface(prompt.Text, out string movementSurface) &&
                (string.Equals(movementSurface, "AutopilotConfirmation", StringComparison.OrdinalIgnoreCase) ||
                 string.Equals(movementSurface, "StopInSector", StringComparison.OrdinalIgnoreCase)))
            {
                await AbortActionForMovementPromptAsync(
                    "port trade",
                    movementSurface,
                    cancellationToken).ConfigureAwait(false);
            }

            if (TradeWarsTelnetTelemetryDecoder.TryGetPromptSurface(prompt.Text, out string surface) &&
                string.Equals(surface, "Command", StringComparison.OrdinalIgnoreCase))
            {
                if (sawQuantityPrompt && commandPromptObservedAt is null)
                {
                    commandPromptObservedAt = prompt.Timestamp;
                }

                continue;
            }

            if (TryParsePortTradeQuantityPrompt(
                prompt.Text,
                out TradeProduct offeredProduct,
                out bool offeredAsSale,
                out int maximumQuantity))
            {
                if (!handledQuantityPrompts.Add(prompt.Text))
                {
                    continue;
                }

                commandPromptObservedAt = null;
                sawQuantityPrompt = true;
                int quantity = 0;
                if (!requestedTradeSelected &&
                    requestedProduct is { } product &&
                    offeredProduct == product &&
                    offeredAsSale == sellingExistingCargo)
                {
                    requestedTradeSelected = true;
                    quantity = maximumQuantity;
                }
                // A missing sale prompt in telemetry must not discard the planned follow-up buy.
                else if (!followUpTradeSelected &&
                         followUpBuyProduct is { } nextProduct &&
                         offeredProduct == nextProduct &&
                         !offeredAsSale)
                {
                    followUpTradeSelected = true;
                    quantity = maximumQuantity;
                }

                await SubmitPortTradeInputAsync(
                    quantity.ToString(CultureInfo.InvariantCulture),
                    cancellationToken).ConfigureAwait(false);
                continue;
            }

            if (prompt.Text.StartsWith("Your offer", StringComparison.OrdinalIgnoreCase))
            {
                commandPromptObservedAt = null;
                if (!isNativeHaggleEnabled())
                {
                    await SubmitPortTradeInputAsync(string.Empty, cancellationToken)
                        .ConfigureAwait(false);
                }

                continue;
            }

            if (prompt.Text.StartsWith("How many holds of", StringComparison.OrdinalIgnoreCase))
            {
                throw new InvalidDataException(
                    $"MTC could not safely parse the port quantity prompt: '{prompt.Text}'. No response was sent.");
            }

            throw new InvalidOperationException(
                $"Port trade stopped at an unexpected prompt: '{prompt.Text}'. No response was sent.");
        }

        throw new TimeoutException(
            "MTC did not observe the TradeWars Command prompt after the port trade transaction.");
    }

    private async Task SubmitPortTradeInputAsync(
        string input,
        CancellationToken cancellationToken)
    {
        MtcRpcActionResult result = await submitCommand(input, true)
            .WaitAsync(cancellationToken)
            .ConfigureAwait(false);
        if (!result.Success)
        {
            throw new InvalidOperationException(
                $"MTC rejected a bounded port-trade response: {result.Message}");
        }
    }

    public async Task StartWorldTradeModeAsync(
        string currentAgentId,
        CancellationToken cancellationToken = default)
    {
        ValidateAgentId(currentAgentId);
        cancellationToken.ThrowIfCancellationRequested();
        if (Volatile.Read(ref _commandsApproved) == 0)
        {
            throw new UnauthorizedAccessException(
                "TWX World Trade mode is disabled until MTC action approval is enabled.");
        }

        MtcRpcActionResult result = await runWorldTradeScript(WorldTradeScriptReference)
            .WaitAsync(cancellationToken)
            .ConfigureAwait(false);
        if (!result.Success)
        {
            throw new InvalidOperationException(
                $"MTC rejected TWX World Trade mode: {result.Message}");
        }
    }

    public async Task StopWorldTradeModeAsync(
        string currentAgentId,
        CancellationToken cancellationToken = default)
    {
        ValidateAgentId(currentAgentId);
        cancellationToken.ThrowIfCancellationRequested();
        if (Volatile.Read(ref _commandsApproved) == 0)
        {
            throw new UnauthorizedAccessException(
                "Stopping TWX World Trade mode is disabled until MTC action approval is enabled.");
        }
        if (!isAtCommandPrompt())
        {
            throw new InvalidOperationException(
                "TWX World Trade mode may only be stopped at a verified TradeWars Command prompt.");
        }
        if (stopScriptByName is null)
        {
            throw new InvalidOperationException(
                "MTC is not configured to stop the fixed World Trade script by name.");
        }
        if (!await isWorldTradeModeRunning().WaitAsync(cancellationToken).ConfigureAwait(false))
        {
            throw new InvalidOperationException(
                "TWX World Trade is not running; no stop request was sent.");
        }

        MtcRpcActionResult result = await stopScriptByName(WorldTradeScriptReference)
            .WaitAsync(cancellationToken)
            .ConfigureAwait(false);
        if (!result.Success)
        {
            throw new InvalidOperationException(
                $"MTC rejected the fixed World Trade stop request: {result.Message}");
        }
        DateTimeOffset stopDeadline = DateTimeOffset.UtcNow + StarDockPromptTimeout;
        while (DateTimeOffset.UtcNow < stopDeadline)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (!await isWorldTradeModeRunning().WaitAsync(cancellationToken).ConfigureAwait(false))
            {
                return;
            }

            await Task.Delay(TimeSpan.FromMilliseconds(150), cancellationToken)
                .ConfigureAwait(false);
        }

        throw new TimeoutException(
            "MTC accepted the World Trade stop request, but the fixed script is still reported as running.");
    }

    public async Task StartStarDockShopperAsync(
        string currentAgentId,
        CancellationToken cancellationToken = default)
    {
        ValidateAgentId(currentAgentId);
        cancellationToken.ThrowIfCancellationRequested();
        if (Volatile.Read(ref _commandsApproved) == 0)
        {
            throw new UnauthorizedAccessException(
                "TWX Dock Shopper is disabled until MTC action approval is enabled.");
        }
        if (!isAtCommandPrompt())
        {
            throw new InvalidOperationException(
                "TWX Dock Shopper requires the TradeWars Command prompt.");
        }

        if (runDockShopperScript is null)
        {
            throw new InvalidOperationException(
                "MTC is not configured to launch the allowlisted Dock Shopper script.");
        }

        MtcRpcActionResult result = await runDockShopperScript(DockShopperScriptReference)
            .WaitAsync(cancellationToken)
            .ConfigureAwait(false);
        if (!result.Success)
        {
            throw new InvalidOperationException(
                $"MTC rejected the allowlisted Dock Shopper script: {result.Message}");
        }
    }

    public async Task<bool> IsStarDockShopperRunningAsync(
        string currentAgentId,
        CancellationToken cancellationToken = default)
    {
        ValidateAgentId(currentAgentId);
        cancellationToken.ThrowIfCancellationRequested();
        if (isDockShopperRunning is null)
        {
            throw new InvalidOperationException(
                "MTC does not provide active-script status for the Dock Shopper.");
        }

        return await isDockShopperRunning()
            .WaitAsync(cancellationToken)
            .ConfigureAwait(false);
    }

    public async Task SendStarDockShopperInputAsync(
        string currentAgentId,
        char input,
        CancellationToken cancellationToken = default)
    {
        ValidateAgentId(currentAgentId);
        cancellationToken.ThrowIfCancellationRequested();
        if (Volatile.Read(ref _commandsApproved) == 0)
        {
            throw new UnauthorizedAccessException(
                "TWX Dock Shopper input is disabled until MTC action approval is enabled.");
        }
        if (input is not ('R' or 'Y' or 'X' or 'D'))
        {
            throw new ArgumentOutOfRangeException(
                nameof(input),
                "Only the allowlisted Dock Shopper keys R, Y, X, and D may be sent.");
        }
        if (!await IsStarDockShopperRunningAsync(currentAgentId, cancellationToken)
                .ConfigureAwait(false))
        {
            throw new InvalidOperationException(
                "The Dock Shopper is not running; no menu input was sent.");
        }

        MtcRpcActionResult result = await submitCommand(input.ToString(), false)
            .WaitAsync(cancellationToken)
            .ConfigureAwait(false);
        if (!result.Success)
        {
            throw new InvalidOperationException(
                $"MTC rejected the single-key Dock Shopper input '{input}': {result.Message}");
        }
    }

    public async Task<bool> IsApprovedScriptRunningAsync(
        string currentAgentId,
        string scriptName,
        CancellationToken cancellationToken = default)
    {
        ValidateAgentId(currentAgentId);
        cancellationToken.ThrowIfCancellationRequested();
        string fileName = Path.GetFileName(scriptName);
        if (fileName.Equals("ls-dock-shopper-v33.ts", StringComparison.OrdinalIgnoreCase))
        {
            return await IsStarDockShopperRunningAsync(currentAgentId, cancellationToken)
                .ConfigureAwait(false);
        }
        if (fileName.Equals("2-worldtrade.ts", StringComparison.OrdinalIgnoreCase))
        {
            return await IsWorldTradeModeRunningAsync(currentAgentId, cancellationToken)
                .ConfigureAwait(false);
        }

        return false;
    }

    public async Task SendScriptInputResponseAsync(
        string currentAgentId,
        GameScriptInputPrompt prompt,
        string response,
        CancellationToken cancellationToken = default)
    {
        ValidateAgentId(currentAgentId);
        cancellationToken.ThrowIfCancellationRequested();
        if (Volatile.Read(ref _commandsApproved) == 0)
        {
            throw new UnauthorizedAccessException(
                "Script prompt responses are disabled until MTC action approval is enabled.");
        }
        if (!ScriptInputPromptPolicy.IsSafeResponse(prompt, response))
        {
            throw new ArgumentException(
                "The script prompt response is not permitted by the observed prompt.",
                nameof(response));
        }
        GameScriptInputPrompt? currentPrompt = getCurrentScriptInputPrompt?.Invoke();
        if (currentPrompt is null ||
            currentPrompt.ObservedAt != prompt.ObservedAt ||
            currentPrompt.SingleKey != prompt.SingleKey ||
            !string.Equals(currentPrompt.ScriptName, prompt.ScriptName, StringComparison.Ordinal) ||
            !string.Equals(currentPrompt.Text, prompt.Text, StringComparison.Ordinal))
        {
            throw new InvalidOperationException(
                "The script input prompt changed before the response could be sent.");
        }
        if (!await IsApprovedScriptRunningAsync(
                currentAgentId,
                prompt.ScriptName,
                cancellationToken).ConfigureAwait(false))
        {
            throw new InvalidOperationException(
                "The prompt's approved script is not running; no response was sent.");
        }

        MtcRpcActionResult result = await submitCommand(response, !prompt.SingleKey)
            .WaitAsync(cancellationToken)
            .ConfigureAwait(false);
        if (!result.Success)
        {
            throw new InvalidOperationException(
                $"MTC rejected the script prompt response: {result.Message}");
        }
    }

    public async Task<AutonomousRefurbQuote> OpenRefurbMenuAsync(
        string currentAgentId,
        int currentSector,
        int portClass,
        CancellationToken cancellationToken = default)
    {
        ValidateAgentId(currentAgentId);
        cancellationToken.ThrowIfCancellationRequested();
        EnsureRefurbActionApproved(portClass);
        if (currentSector <= 0 || !isAtCommandPrompt())
        {
            throw new InvalidOperationException(
                "The Refurb menu can only be opened from a verified Command prompt and known sector.");
        }
        if (getRecentServerLines is null)
        {
            throw new InvalidOperationException(
                "MTC does not provide recent server text required to verify Refurb quote rows.");
        }

        DateTimeOffset submittedAt = DateTimeOffset.UtcNow;
        await SubmitRefurbInputAsync(
            portClass == 0 ? "pty" : "ps  gygqs  p",
            cancellationToken).ConfigureAwait(false);
        DateTimeOffset deadline = submittedAt + RefurbQuoteTimeout;
        while (DateTimeOffset.UtcNow < deadline)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (TryParseRefurbQuote(
                    getRecentServerLines(),
                    submittedAt,
                    currentSector,
                    portClass,
                    out AutonomousRefurbQuote? quote))
            {
                return quote!;
            }

            await Task.Delay(TimeSpan.FromMilliseconds(150), cancellationToken)
                .ConfigureAwait(false);
        }

        throw new TimeoutException(
            "MTC did not observe all three fresh Refurb quote rows after opening the verified port menu.");
    }

    public async Task ExecuteRefurbUpgradeAsync(
        string currentAgentId,
        AutonomousRefurbUpgrade upgrade,
        AutonomousRefurbQuote quote,
        long synchronizedCredits,
        CancellationToken cancellationToken = default)
    {
        ValidateAgentId(currentAgentId);
        ArgumentNullException.ThrowIfNull(quote);
        cancellationToken.ThrowIfCancellationRequested();
        EnsureRefurbActionApproved(quote.PortClass);
        if (!Enum.IsDefined(upgrade))
        {
            throw new ArgumentOutOfRangeException(nameof(upgrade));
        }
        if (getRecentServerLines is null ||
            quote.ObservedAt > DateTimeOffset.UtcNow ||
            DateTimeOffset.UtcNow - quote.ObservedAt > RefurbQuoteTimeout ||
            !TryParseRefurbQuote(
                getRecentServerLines(),
                quote.ObservedAt.AddTicks(-1),
                quote.Sector,
                quote.PortClass,
                out AutonomousRefurbQuote? currentQuote) ||
            currentQuote is null ||
            currentQuote.CargoHoldUnitCost != quote.CargoHoldUnitCost ||
            currentQuote.CargoHoldsAvailable != quote.CargoHoldsAvailable ||
            currentQuote.FighterUnitCost != quote.FighterUnitCost ||
            currentQuote.FightersAvailable != quote.FightersAvailable ||
            currentQuote.ShieldUnitCost != quote.ShieldUnitCost ||
            currentQuote.ShieldsAvailable != quote.ShieldsAvailable)
        {
            throw new InvalidOperationException(
                "The Refurb quote is no longer fresh; inspect the menu again before purchasing.");
        }

        int quantity = AutonomousRefurbPurchasePolicy.GetMaximumPlannedQuantity(
            quote,
            upgrade,
            synchronizedCredits);
        if (quantity <= 0)
        {
            throw new InvalidOperationException(
                $"The verified credit reserve and live quote do not permit a {upgrade} purchase.");
        }

        string input = upgrade switch
        {
            AutonomousRefurbUpgrade.CargoHolds => $"A{quantity}*Y",
            AutonomousRefurbUpgrade.Fighters => $"B{quantity}*",
            AutonomousRefurbUpgrade.Shields => $"C{quantity}*",
            _ => throw new ArgumentOutOfRangeException(nameof(upgrade))
        };
        DateTimeOffset submittedAt = DateTimeOffset.UtcNow;
        await SubmitRefurbInputAsync(input, cancellationToken).ConfigureAwait(false);
        await WaitForServerLineAfterAsync(submittedAt, cancellationToken).ConfigureAwait(false);
        await CloseRefurbMenuAsync(currentAgentId, quote.PortClass, cancellationToken)
            .ConfigureAwait(false);
    }

    public async Task CloseRefurbMenuAsync(
        string currentAgentId,
        int portClass,
        CancellationToken cancellationToken = default)
    {
        ValidateAgentId(currentAgentId);
        cancellationToken.ThrowIfCancellationRequested();
        EnsureRefurbActionApproved(portClass);
        if (isAtCommandPrompt())
        {
            return;
        }

        await SubmitRefurbInputAsync(
            portClass == 0 ? "Q" : "QQQ",
            cancellationToken).ConfigureAwait(false);
        DateTimeOffset deadline = DateTimeOffset.UtcNow + StarDockPromptTimeout;
        while (DateTimeOffset.UtcNow < deadline)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (isAtCommandPrompt())
            {
                return;
            }

            await Task.Delay(TimeSpan.FromMilliseconds(150), cancellationToken)
                .ConfigureAwait(false);
        }

        throw new TimeoutException(
            "MTC did not observe the TradeWars Command prompt after closing the Refurb menu.");
    }

    public async Task<bool> IsWorldTradeModeRunningAsync(
        string currentAgentId,
        CancellationToken cancellationToken = default)
    {
        ValidateAgentId(currentAgentId);
        cancellationToken.ThrowIfCancellationRequested();
        return await isWorldTradeModeRunning()
            .WaitAsync(cancellationToken)
            .ConfigureAwait(false);
    }

    public async Task RefreshCimAsync(
        string currentAgentId,
        CancellationToken cancellationToken = default)
    {
        ValidateAgentId(currentAgentId);
        cancellationToken.ThrowIfCancellationRequested();
        if (Volatile.Read(ref _commandsApproved) == 0)
        {
            throw new UnauthorizedAccessException(
                "Live CIM refresh is disabled until the MTC session authorizes the request.");
        }
        if (!isAtCommandPrompt())
        {
            throw new InvalidOperationException(
                "Live CIM refresh requires the TradeWars Command prompt.");
        }

        DateTime previousPortCim = getLastPortCim();
        await RunCimScriptAsync("utilities/MTC_PortCIM.ts", cancellationToken).ConfigureAwait(false);
        if (getLastPortCim() <= previousPortCim)
        {
            throw new InvalidOperationException(
                "The port CIM request returned without recording a fresh port-data batch.");
        }

        await RunCimScriptAsync("utilities/MTC_WarpCIM.ts", cancellationToken).ConfigureAwait(false);
    }

    private async Task RunCimScriptAsync(string script, CancellationToken cancellationToken)
    {
        DateTimeOffset submittedAt = DateTimeOffset.UtcNow;
        MtcRpcActionResult result = await runCimScript(script)
            .WaitAsync(cancellationToken)
            .ConfigureAwait(false);
        if (!result.Success)
        {
            throw new InvalidOperationException(
                $"MTC rejected the TWX CIM script '{script}': {result.Message}");
        }

        await waitForCimCompletion(submittedAt, cancellationToken).ConfigureAwait(false);
    }

    private void EnsureRefurbActionApproved(int portClass)
    {
        if (Volatile.Read(ref _commandsApproved) == 0)
        {
            throw new UnauthorizedAccessException(
                "Refurb menu actions are disabled until MTC action approval is enabled.");
        }
        if (portClass is not (0 or 9))
        {
            throw new ArgumentOutOfRangeException(
                nameof(portClass),
                "Only verified Class 0 ports and StarDock are allowlisted for Refurb.");
        }
    }

    private async Task SubmitRefurbInputAsync(
        string input,
        CancellationToken cancellationToken)
    {
        MtcRpcActionResult result = await submitCommand(input, false)
            .WaitAsync(cancellationToken)
            .ConfigureAwait(false);
        if (!result.Success)
        {
            throw new InvalidOperationException(
                $"MTC rejected the fixed Refurb menu input: {result.Message}");
        }
    }

    private static bool TryParseRefurbQuote(
        IReadOnlyList<MtcServerPrompt> lines,
        DateTimeOffset submittedAt,
        int sector,
        int portClass,
        out AutonomousRefurbQuote? quote)
    {
        (int Cost, int Available, DateTimeOffset Timestamp)? holds = null;
        (int Cost, int Available, DateTimeOffset Timestamp)? fighters = null;
        (int Cost, int Available, DateTimeOffset Timestamp)? shields = null;
        foreach (MtcServerPrompt line in lines)
        {
            if (line.Timestamp < submittedAt ||
                DateTimeOffset.UtcNow - line.Timestamp > RefurbQuoteTimeout)
            {
                continue;
            }

            string[] words = line.Text.Split(
                (char[]?)null,
                StringSplitOptions.RemoveEmptyEntries);
            if (words.Length == 0)
            {
                continue;
            }

            string row = words[0].TrimEnd(':', '.').ToUpperInvariant();
            if (row == "A" &&
                TryReadQuoteNumbers(
                    words,
                    costWord: 5,
                    quantityWord: 10,
                    out int holdCost,
                    out int holdCount))
            {
                holds = (holdCost, holdCount, line.Timestamp);
            }
            else if (row == "B" &&
                     TryReadQuoteNumbers(words, costWord: 4, quantityWord: 8, out int fighterCost, out int fighterCount))
            {
                fighters = (fighterCost, fighterCount, line.Timestamp);
            }
            else if (row == "C" &&
                     TryReadQuoteNumbers(words, costWord: 5, quantityWord: 9, out int shieldCost, out int shieldCount))
            {
                shields = (shieldCost, shieldCount, line.Timestamp);
            }
        }

        if (holds is { } h && fighters is { } f && shields is { } s)
        {
            quote = new AutonomousRefurbQuote(
                sector,
                portClass,
                h.Cost,
                h.Available,
                f.Cost,
                f.Available,
                s.Cost,
                s.Available,
                new[] { h.Timestamp, f.Timestamp, s.Timestamp }.Max());
            return true;
        }

        quote = null;
        return false;
    }

    private static bool TryReadQuoteNumbers(
        IReadOnlyList<string> words,
        int costWord,
        int quantityWord,
        out int cost,
        out int quantity)
    {
        cost = 0;
        quantity = 0;
        return words.Count >= Math.Max(costWord, quantityWord) &&
            TryParseQuoteInteger(words[costWord - 1], out cost) &&
            TryParseQuoteInteger(words[quantityWord - 1], out quantity) &&
            cost > 0 &&
            quantity >= 0;
    }

    private static bool TryParseQuoteInteger(string text, out int value) =>
        int.TryParse(
            text.Trim('$', ',', ':').Replace(",", string.Empty, StringComparison.Ordinal),
            NumberStyles.None,
            CultureInfo.InvariantCulture,
            out value);

    private async Task WaitForServerLineAfterAsync(
        DateTimeOffset submittedAt,
        CancellationToken cancellationToken)
    {
        if (getRecentServerLines is null)
        {
            throw new InvalidOperationException(
                "MTC cannot verify that the Refurb purchase received a server response.");
        }

        DateTimeOffset deadline = submittedAt + StarDockPromptTimeout;
        while (DateTimeOffset.UtcNow < deadline)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (getRecentServerLines().Any(line => line.Timestamp > submittedAt))
            {
                return;
            }

            await Task.Delay(TimeSpan.FromMilliseconds(150), cancellationToken)
                .ConfigureAwait(false);
        }

        throw new TimeoutException(
            "MTC did not observe a server response to the Refurb purchase; the menu was left open.");
    }

    private void ValidateAgentId(string currentAgentId)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(currentAgentId);
        if (!string.Equals(currentAgentId, agentId, StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException(
                $"Agent '{currentAgentId}' is not attached to the active MTC session.");
        }
    }
}
