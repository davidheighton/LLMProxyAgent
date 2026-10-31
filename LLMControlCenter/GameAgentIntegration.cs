using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Avalonia.Threading;
using LLMSurvivingProxyAgent.Runtime;
using Core = TWXProxy.Core;

namespace MTC;

public partial class MainWindow
{
    private string _lastGameAgentServerEventSignature = string.Empty;
    private DateTime _lastGameAgentServerEventUtc = DateTime.MinValue;
    private string _lastMtcRpcSensitivePromptConsumed = string.Empty;
    private volatile string? _lastGameAgentPromptSurface;
    private DateTimeOffset? _lastGameAgentPromptObservedAt;
    private readonly MtcRpcPasswordConfirmationGuard _mtcRpcPasswordConfirmationGuard = new();
    private volatile bool _gameAgentInputRedactionRequired = true;
    private volatile bool _onlineRefreshCommandPromptObserved;
    private volatile bool _sensitiveTerminalInputInProgress;
    private volatile bool _sensitiveOutputRedactionRequired;

    private void OpenGameAgentWindow()
    {
        try
        {
            if (_gameAgentWindow != null)
            {
                _gameAgentWindow.Activate();
                _gameAgentWindow.Focus();
                return;
            }

            _gameAgent.SetGameName(GetGameAgentGameName());
            var window = new GameAgentWindow(
                BuildGameAgentContextSnapshot,
                SendMtcRpcCommandAsync,
                ExecuteGameAgentMombotCommandAsync,
                RunMtcRpcScriptAsync,
                StopMtcRpcScriptAsync,
                () => _dashboardHost.GetAgentMonitorSnapshot(DefaultAgentId),
                GameAgentPresenceHeartbeatInterval,
                _appPrefs);
            window.Closed += (_, _) => _gameAgentWindow = null;
            _gameAgentWindow = window;
            ShowOwnedChildWindow(window, activate: false);
        }
        catch (Exception ex)
        {
            _gameAgentWindow = null;
            _ = ShowMessageAsync("Game Agent", $"Could not open Game Agent:\n{ex.Message}");
        }
    }

    private async Task OpenGameAgentReplayWindowAsync()
    {
        string? path = await PickProxyOpenPathAsync(
            "Open Game Agent Event Log",
            "Game Agent Events",
            "*.jsonl");
        if (string.IsNullOrWhiteSpace(path))
            return;

        _gameAgentReplayWindow?.Close();
        var window = new GameAgentReplayWindow(path);
        window.Closed += (_, _) =>
        {
            if (ReferenceEquals(_gameAgentReplayWindow, window))
                _gameAgentReplayWindow = null;
        };
        _gameAgentReplayWindow = window;
        ShowOwnedChildWindow(window, activate: false);
    }

    private Task<MtcRpcActionResult> ExecuteGameAgentMombotCommandAsync(string command)
        => InvokeMtcRpcUiAsync(() =>
        {
            string input = (command ?? string.Empty).Trim();
            if (string.IsNullOrWhiteSpace(input))
                return Task.FromResult(MtcRpcActionResult.Fail("Mombot command is required."));

            MTC.mombot.mombotStatusSnapshot snapshot = _mombot.GetStatusSnapshot();
            if (!snapshot.Enabled)
                return Task.FromResult(MtcRpcActionResult.Fail("Native MTC Mombot is not enabled; mombot commands are unavailable."));
            if (!snapshot.AcceptSelfCommands)
                return Task.FromResult(MtcRpcActionResult.Fail("Native MTC Mombot is not accepting local/self commands."));

            try
            {
                ExecuteMombotLocalInput(input);
                return Task.FromResult(MtcRpcActionResult.Ok("Mombot command submitted through the native MTC mombot dispatcher.", new Dictionary<string, string>
                {
                    ["command"] = input,
                }));
            }
            catch (Exception ex)
            {
                return Task.FromResult(MtcRpcActionResult.Fail(ex.Message));
            }
        });

    private async Task ConfigureGameAgentAsync()
    {
        var window = new GameAgentConfigWindow(_appPrefs);
        bool saved = await window.ShowDialog<bool>(this);
        if (saved)
        {
            _gameAgentWindow?.Close();
            _gameAgentWindow = null;
        }
    }

    private GameAgentContextSnapshot BuildGameAgentContextSnapshot()
    {
        _gameAgent.SetGameName(GetGameAgentGameName());
        GameAgentBotSnapshot bot = BuildGameAgentBotSnapshot();
        string[] onlinePlayers = BuildGameAgentOnlinePlayersSnapshot();
        IReadOnlyList<Core.RunningScriptInfo> runningScripts = BuildGameAgentRunningScriptsSnapshot();

        return _gameAgent.BuildContextSnapshot(
            _state,
            _sessionDb,
            bot,
            onlinePlayers,
            runningScripts,
            scriptInputPrompt: _gameAgent.GetCurrentScriptInputPrompt());
    }

    private GameAgentBotSnapshot BuildGameAgentBotSnapshot()
    {
        try
        {
            MTC.mombot.mombotStatusSnapshot snapshot = _mombot.GetStatusSnapshot();
            return new GameAgentBotSnapshot
            {
                NativeMombotRunning = _mombot.Enabled,
                ExternalBotName = _gameInstance?.ActiveBotName ?? string.Empty,
                BotName = snapshot.BotName,
                TeamName = snapshot.TeamName,
                Mode = snapshot.Mode,
                LastLoadedModule = snapshot.LastLoadedModule,
                WatcherAttached = snapshot.WatcherAttached,
                AcceptsSelfCommands = snapshot.AcceptSelfCommands,
                AcceptsSubspaceCommands = snapshot.AcceptSubspaceCommands,
                AcceptsPrivateCommands = snapshot.AcceptPrivateCommands,
            };
        }
        catch
        {
            return new GameAgentBotSnapshot
            {
                NativeMombotRunning = false,
                ExternalBotName = _gameInstance?.ActiveBotName ?? string.Empty,
            };
        }
    }

    private string[] BuildGameAgentOnlinePlayersSnapshot()
    {
        try
        {
            return _onlinePlayers.ToArray();
        }
        catch
        {
            return [];
        }
    }

    private IReadOnlyList<Core.RunningScriptInfo> BuildGameAgentRunningScriptsSnapshot()
    {
        try
        {
            return Core.ProxyGameOperations.GetRunningScripts(CurrentInterpreter);
        }
        catch
        {
            return [];
        }
    }

    private void ObserveGameAgentServerLine(string plainText, string ansiText, bool isPrompt)
    {
        string normalized = NormalizeGameAgentText(plainText);
        if (string.IsNullOrWhiteSpace(normalized))
            return;

        bool isClassifiedPrompt =
            TradeWarsTelnetTelemetryDecoder.TryGetPromptSurface(normalized, out string classifiedSurface);
        bool publishAsPrompt = isPrompt || isClassifiedPrompt;
        bool redactFollowingSecretOutput = _sensitiveOutputRedactionRequired;
        string? protectedPromptStage = null;
        string? protectedSetupPromptCategory = null;
        string? protectedSetupNoticeCategory = null;
        if (redactFollowingSecretOutput)
        {
            if (!isPrompt)
            {
                protectedSetupNoticeCategory =
                    MtcRpcSecretInputPolicy.ClassifyProtectedSetupNotice(normalized);
                if (protectedSetupNoticeCategory == null)
                    return;

                normalized = "[redacted account-setup password notice]";
            }
            else
            {
                if (normalized.StartsWith("Command [TL=", StringComparison.OrdinalIgnoreCase))
                {
                    _sensitiveOutputRedactionRequired = false;
                }
                else
                {
                    protectedPromptStage = MtcRpcSecretInputPolicy.ClassifyPasswordPrompt(normalized);
                    if (protectedPromptStage != null)
                    {
                        normalized = "[redacted account-setup password prompt]";
                    }
                    else
                    {
                        protectedSetupPromptCategory =
                            MtcRpcSecretInputPolicy.ClassifyProtectedSetupPrompt(normalized);
                        normalized = "[redacted account-setup follow-up prompt]";
                    }
                }
            }
        }

        if (publishAsPrompt)
        {
            _gameAgentInputRedactionRequired =
                !normalized.StartsWith("Command [TL=", StringComparison.OrdinalIgnoreCase);
            _onlineRefreshCommandPromptObserved =
                normalized.StartsWith("Command [TL=", StringComparison.OrdinalIgnoreCase);
        }

        string signature = string.Concat(publishAsPrompt ? "prompt|" : "line|", _state.Sector, "|", normalized);
        DateTime now = DateTime.UtcNow;
        if (string.Equals(signature, _lastGameAgentServerEventSignature, StringComparison.Ordinal) &&
            (now - _lastGameAgentServerEventUtc).TotalMilliseconds < 1000)
        {
            return;
        }

        _lastGameAgentServerEventSignature = signature;
        _lastGameAgentServerEventUtc = now;
        string promptSurface = publishAsPrompt
            ? isClassifiedPrompt ? classifiedSurface : ResolveGameAgentPromptSurface(normalized)
            : string.Empty;
        _lastGameAgentPromptSurface = publishAsPrompt ? promptSurface : null;
        _lastGameAgentPromptObservedAt = publishAsPrompt
            ? new DateTimeOffset(now, TimeSpan.Zero)
            : null;

        _gameAgent.SetGameName(GetGameAgentGameName());
        var metadata = new Dictionary<string, string>();
        if (protectedPromptStage != null)
            metadata["protectedSetupPromptStage"] = protectedPromptStage;
        if (protectedSetupPromptCategory != null)
            metadata["protectedSetupPromptCategory"] = protectedSetupPromptCategory;
        if (protectedSetupNoticeCategory != null)
            metadata["protectedSetupNoticeCategory"] = protectedSetupNoticeCategory;

        RecordGameAgentEvent(new GameAgentEvent
        {
            GameName = GetGameAgentGameName(),
            Kind = publishAsPrompt ? GameAgentEventKind.ServerPrompt : GameAgentEventKind.ServerLine,
            PlainText = normalized,
            AnsiText = redactFollowingSecretOutput ? string.Empty : ansiText ?? string.Empty,
            CurrentSector = _state.Sector,
            PromptSurface = promptSurface,
            Metadata = metadata,
        });
    }

    private void ObserveGameAgentClientInput(byte[] bytes)
    {
        if (bytes.Length == 0)
            return;

        _lastGameAgentPromptSurface = null;
        _lastGameAgentPromptObservedAt = null;
        bool redact = _gameAgentInputRedactionRequired || LooksLikeSensitiveLoginInput(bytes);
        string text = redact
            ? "[redacted sensitive input]"
            : FormatGameAgentInput(bytes);

        if (string.IsNullOrWhiteSpace(text))
            return;

        _gameAgent.SetGameName(GetGameAgentGameName());
        RecordGameAgentEvent(new GameAgentEvent
        {
            GameName = GetGameAgentGameName(),
            Kind = GameAgentEventKind.ClientInput,
            PlainText = text,
            CurrentSector = _state.Sector,
            Metadata = new Dictionary<string, string>
            {
                ["bytes"] = bytes.Length.ToString(),
                ["redacted"] = redact ? "true" : "false",
            },
        });
    }

    private void ObserveGameAgentSensitiveInput()
    {
        _gameAgent.SetGameName(GetGameAgentGameName());
        RecordGameAgentEvent(new GameAgentEvent
        {
            GameName = GetGameAgentGameName(),
            Kind = GameAgentEventKind.ClientInput,
            PlainText = "[redacted account-setup credential input]",
            CurrentSector = _state.Sector,
            Metadata = new Dictionary<string, string>
            {
                ["redacted"] = "true",
                ["inputType"] = "account-setup-credential",
            },
        });
    }

    private void ObserveGameAgentConnectionChanged(bool connected)
    {
        _gameAgentInputRedactionRequired = true;
        _onlineRefreshCommandPromptObserved = false;
        _sensitiveOutputRedactionRequired = false;
        _lastMtcRpcSensitivePromptConsumed = string.Empty;
        _mtcRpcPasswordConfirmationGuard.Clear();
        _gameAgent.SetGameName(GetGameAgentGameName());
        RecordGameAgentEvent(new GameAgentEvent
        {
            GameName = GetGameAgentGameName(),
            Kind = connected ? GameAgentEventKind.Connected : GameAgentEventKind.Disconnected,
            PlainText = connected ? "Connected" : "Disconnected",
            CurrentSector = _state.Sector,
            Metadata = new Dictionary<string, string>
            {
                ["host"] = _state.Host,
                ["port"] = _state.Port.ToString(),
                ["embeddedProxy"] = _state.EmbeddedProxy ? "true" : "false",
            },
        });
    }

    private void RecordGameAgentPresenceHeartbeat()
    {
        if (!_state.Connected)
            return;

        _gameAgent.SetGameName(GetGameAgentGameName());
        DateTimeOffset heartbeatAt = DateTimeOffset.UtcNow;
        DateTimeOffset? promptObservedAt = _lastGameAgentPromptObservedAt;
        bool promptIsCurrent = promptObservedAt is { } observedAt &&
            observedAt <= heartbeatAt &&
            !string.IsNullOrWhiteSpace(_lastGameAgentPromptSurface);
        var metadata = new Dictionary<string, string>
        {
            ["connected"] = "true",
        };
        if (promptIsCurrent && promptObservedAt is { } currentPromptAt)
        {
            metadata["promptObservedAt"] = currentPromptAt.ToString("O");
            metadata["promptIsCurrent"] = "true";
        }

        RecordGameAgentEvent(new GameAgentEvent
        {
            Timestamp = heartbeatAt,
            GameName = GetGameAgentGameName(),
            Kind = GameAgentEventKind.PresenceHeartbeat,
            PlainText = "MTC observed its connected session.",
            CurrentSector = _state.Sector,
            Metadata = metadata,
            PromptSurface = promptIsCurrent ? _lastGameAgentPromptSurface ?? string.Empty : string.Empty,
        });
    }

    private void ObserveGameAgentCurrentSectorChanged(int sector)
    {
        _gameAgent.SetGameName(GetGameAgentGameName());
        RecordGameAgentEvent(new GameAgentEvent
        {
            GameName = GetGameAgentGameName(),
            Kind = GameAgentEventKind.CurrentSectorChanged,
            PlainText = $"Current sector changed to {sector}",
            CurrentSector = sector,
        });
    }

    private void ObserveGameAgentShipStatus(Core.ShipStatus status)
    {
        _gameAgent.SetGameName(GetGameAgentGameName());
        var metadata = new Dictionary<string, string>();
        AddObservedStatusValue(metadata, status, Core.ShipStatusFields.CurrentSector, "currentSector", status.CurrentSector);
        AddObservedStatusValue(metadata, status, Core.ShipStatusFields.Turns, "turns", status.Turns);
        AddObservedStatusValue(metadata, status, Core.ShipStatusFields.Credits, "credits", status.Credits);
        AddObservedStatusValue(metadata, status, Core.ShipStatusFields.Fighters, "fighters", status.Fighters);
        AddObservedStatusValue(metadata, status, Core.ShipStatusFields.Shields, "shields", status.Shields);
        AddObservedStatusValue(metadata, status, Core.ShipStatusFields.HoldsEmpty, "holdsEmpty", status.HoldsEmpty);
        AddObservedStatusValue(metadata, status, Core.ShipStatusFields.TotalHolds, "holdsTotal", status.TotalHolds);
        AddObservedStatusValue(metadata, status, Core.ShipStatusFields.FuelOre, "fuelOre", status.FuelOre);
        AddObservedStatusValue(metadata, status, Core.ShipStatusFields.Organics, "organics", status.Organics);
        AddObservedStatusValue(metadata, status, Core.ShipStatusFields.Equipment, "equipment", status.Equipment);
        AddObservedStatusValue(metadata, status, Core.ShipStatusFields.Experience, "experience", status.Experience);
        AddObservedStatusValue(metadata, status, Core.ShipStatusFields.Alignment, "alignment", status.Alignment);
        AddObservedStatusValue(metadata, status, Core.ShipStatusFields.Corporation, "corporationId", status.Corp);
        AddObservedStatusValue(metadata, status, Core.ShipStatusFields.ShipName, "shipName", status.ShipName);
        AddObservedStatusValue(metadata, status, Core.ShipStatusFields.TraderName, "traderName", status.TraderName);
        AddObservedStatusValue(metadata, status, Core.ShipStatusFields.ShipType, "shipType", status.ShipType);
        AddObservedStatusValue(metadata, status, Core.ShipStatusFields.LongRangeScanner, "scannerType", status.LRSType);
        RecordGameAgentEvent(new GameAgentEvent
        {
            GameName = GetGameAgentGameName(),
            Kind = GameAgentEventKind.SynchronizedShipStatus,
            PlainText = "Complete ship-status response parsed.",
            CurrentSector = status.CurrentSector,
            Metadata = metadata,
        });
    }

    private void RecordGameAgentEvent(GameAgentEvent gameEvent)
    {
        if (gameEvent.Kind is GameAgentEventKind.Connected or GameAgentEventKind.Disconnected)
        {
            _lastGameAgentPromptSurface = null;
            _lastGameAgentPromptObservedAt = null;
        }

        _gameAgent.Record(gameEvent);

        string? eventKind = gameEvent.Kind switch
        {
            GameAgentEventKind.Connected => "Connected",
            GameAgentEventKind.Disconnected => "Disconnected",
            GameAgentEventKind.CurrentSectorChanged => "CurrentSectorChanged",
            GameAgentEventKind.SynchronizedShipStatus => "SynchronizedShipStatus",
            GameAgentEventKind.ServerPrompt => "ServerPrompt",
            GameAgentEventKind.PresenceHeartbeat => "PresenceHeartbeat",
            _ => null,
        };
        if (eventKind is null)
        {
            return;
        }

        var proxyEvent = new ProxyGameEvent(
            gameEvent.Timestamp,
            eventKind,
            gameEvent.CurrentSector > 0 ? gameEvent.CurrentSector : null,
            gameEvent.PromptSurface,
            gameEvent.Metadata);
        if (!_dashboardHost.TryPublishGameEvent(DefaultAgentId, proxyEvent))
        {
            Core.GlobalModules.DebugLog(
                $"[Dashboard] agent event queue is full; dropped {eventKind} for {DefaultAgentId}.\n");
        }
    }

    private void OnSynchronizedShipStatusUpdated(Core.ShipStatus status)
    {
        Dispatcher.UIThread.Post(() => ObserveGameAgentShipStatus(status));
    }

    private static void AddObservedStatusValue<T>(
        IDictionary<string, string> metadata,
        Core.ShipStatus status,
        Core.ShipStatusFields field,
        string key,
        T value)
    {
        if (status.ObservedFields.HasFlag(field))
        {
            metadata[key] = Convert.ToString(value, System.Globalization.CultureInfo.InvariantCulture)
                ?? string.Empty;
        }
    }

    private string GetGameAgentGameName()
    {
        string name = _embeddedGameName ?? _state.GameName;
        if (string.IsNullOrWhiteSpace(name))
            name = DeriveGameName();

        return Core.SharedPaths.SanitizeFileComponent(name);
    }

    private static bool LooksLikeAgentPrompt(string line)
    {
        string normalized = NormalizeGameAgentText(line);
        if (string.IsNullOrWhiteSpace(normalized))
            return false;

        return normalized.Contains("(?=Help)?", StringComparison.OrdinalIgnoreCase) ||
               normalized.StartsWith("Command [TL=", StringComparison.OrdinalIgnoreCase) ||
               normalized.StartsWith("Citadel command", StringComparison.OrdinalIgnoreCase) ||
               normalized.StartsWith("Computer command", StringComparison.OrdinalIgnoreCase) ||
               normalized.StartsWith("Corporate command", StringComparison.OrdinalIgnoreCase) ||
               normalized.StartsWith("Planet command", StringComparison.OrdinalIgnoreCase) ||
               normalized.StartsWith("Port command", StringComparison.OrdinalIgnoreCase) ||
               normalized.StartsWith("Sub-space radio", StringComparison.OrdinalIgnoreCase) ||
               normalized.EndsWith("Your choice?", StringComparison.OrdinalIgnoreCase);
    }

    private static string ResolveGameAgentPromptSurface(string line)
    {
        string normalized = NormalizeGameAgentText(line);
        if (normalized.StartsWith("Command [TL=", StringComparison.OrdinalIgnoreCase))
            return "Command";

        int commandIndex = normalized.IndexOf(" command", StringComparison.OrdinalIgnoreCase);
        if (commandIndex > 0)
            return normalized[..commandIndex].Trim();

        if (normalized.StartsWith("Sub-space radio", StringComparison.OrdinalIgnoreCase))
            return "Sub-space radio";

        return normalized.Length <= 80 ? normalized : normalized[..80];
    }

    private static string NormalizeGameAgentText(string? text)
    {
        if (string.IsNullOrEmpty(text))
            return string.Empty;

        return Core.AnsiCodes.NormalizeTerminalText(text)
            .Replace("\r", string.Empty, StringComparison.Ordinal)
            .TrimEnd('\n');
    }

    private static bool LooksLikeSensitiveLoginInput(byte[] bytes)
    {
        if (bytes.Length > 64)
            return true;

        string text = Encoding.Latin1.GetString(bytes).Trim();
        if (text.Length == 0)
            return false;

        return !text.Contains(' ', StringComparison.Ordinal) &&
               text.Any(char.IsLetterOrDigit) &&
               text.Length >= 3;
    }

    private static string FormatGameAgentInput(byte[] bytes)
    {
        string text = NormalizeGameAgentText(Encoding.Latin1.GetString(bytes));
        if (!string.IsNullOrWhiteSpace(text))
            return text;

        if (bytes.Any(value => value == (byte)'\r' || value == (byte)'\n'))
            return "<ENTER>";

        if (bytes.Length == 1 && bytes[0] == 0x1B)
            return "<ESC>";

        return Convert.ToHexString(bytes);
    }
}
