using System;
using System.IO;
using System.Linq;
using System.Text;
using Avalonia.Threading;
using LLMSurvivingProxyAgent.Core;
using Core = TWXProxy.Core;

namespace MTC;

public partial class MainWindow
{
    private MtcJsonRpcServer? _jsonRpcServer;

    private void ApplyJsonRpcPreferences()
    {
        try
        {
            _jsonRpcServer ??= new MtcJsonRpcServer(_gameAgent, BuildMtcRpcBridge());

            MtcRpcProcessConfiguration processConfiguration =
                MtcRpcProcessConfiguration.ReadFromEnvironment();
            if (processConfiguration.Requested)
            {
                if (!processConfiguration.Valid)
                    Core.GlobalModules.DebugLog("[MTC.JsonRpc] process-only RPC was requested but no valid process token was supplied.\n");

                _jsonRpcServer.ApplyOptions(new MtcJsonRpcServerOptions
                {
                    Enabled = processConfiguration.Valid,
                    BindAddress = "127.0.0.1",
                    Port = processConfiguration.Port,
                    AuthToken = processConfiguration.AuthToken,
                    ApprovalLevel = MtcRpcApprovalLevels.Parse(_appPrefs.JsonRpcApprovalLevel),
                    ProcessOnly = true,
                    SecretInputEnabled = processConfiguration.Valid && processConfiguration.SecretInputEnabled,
                    CredentialTarget = processConfiguration.CredentialTarget,
                });
                return;
            }

            _appPrefs.EnsureJsonRpcAuthToken();
            _jsonRpcServer.ApplyOptions(new MtcJsonRpcServerOptions
            {
                Enabled = _appPrefs.JsonRpcEnabled,
                BindAddress = AppPreferences.NormalizeJsonRpcBindAddress(_appPrefs.JsonRpcBindAddress),
                Port = AppPreferences.NormalizeJsonRpcPort(_appPrefs.JsonRpcPort),
                AuthToken = AppPreferences.NormalizeJsonRpcAuthToken(_appPrefs.JsonRpcAuthToken),
                ApprovalLevel = MtcRpcApprovalLevels.Parse(_appPrefs.JsonRpcApprovalLevel),
            });
        }
        catch (Exception ex)
        {
            Core.GlobalModules.DebugLog($"[MTC.JsonRpc] failed to apply preferences: {ex}\n");
            _parser.Feed($"\x1b[1;31m[JSON-RPC failed: {ex.Message}]\x1b[0m\r\n");
            _buffer.Dirty = true;
        }
    }

    private MtcRpcBridge BuildMtcRpcBridge()
        => new()
        {
            GetContextAsync = BuildMtcRpcContextAsync,
            GetRecentEventsAsync = GetMtcRpcRecentEventsAsync,
            QuerySectorAsync = QueryMtcRpcSectorAsync,
            ListScriptsAsync = ListMtcRpcScriptsAsync,
            SendCommandAsync = SendMtcRpcCommandAsync,
            RunMombotCommandAsync = ExecuteGameAgentMombotCommandAsync,
            RunScriptAsync = RunMtcRpcScriptAsync,
            StopScriptAsync = StopMtcRpcScriptAsync,
            ApproveActionAsync = ApproveMtcRpcActionAsync,
            GetSensitivePasswordPromptAsync = GetMtcRpcSensitivePasswordPromptAsync,
            SendSensitivePasswordAsync = SendMtcRpcSensitivePasswordAsync,
            ReadSensitiveCredentialAsync = WindowsCredentialSecretStore.ReadPrintableAsciiSecret,
        };

    private Task<GameAgentContextSnapshot> BuildMtcRpcContextAsync(int recentEventCount)
        => InvokeMtcRpcUiAsync(() =>
        {
            _gameAgent.SetGameName(GetGameAgentGameName());
            return Task.FromResult(_gameAgent.BuildContextSnapshot(
                _state,
                _sessionDb,
                BuildGameAgentBotSnapshot(),
                BuildGameAgentOnlinePlayersSnapshot(),
                BuildGameAgentRunningScriptsSnapshot(),
                recentEventCount,
                _gameAgent.GetCurrentScriptInputPrompt()));
        });

    private Task<IReadOnlyList<GameAgentEvent>> GetMtcRpcRecentEventsAsync(int limit, bool includeAnsi)
    {
        IReadOnlyList<GameAgentEvent> events = _gameAgent.GetRecentEvents(limit);
        if (includeAnsi)
            return Task.FromResult(events);

        IReadOnlyList<GameAgentEvent> stripped = events
            .Select(evt => new GameAgentEvent
            {
                Timestamp = evt.Timestamp,
                GameName = evt.GameName,
                Kind = evt.Kind,
                PlainText = evt.PlainText,
                AnsiText = string.Empty,
                CurrentSector = evt.CurrentSector,
                PromptSurface = evt.PromptSurface,
                Metadata = evt.Metadata,
            })
            .ToArray();
        return Task.FromResult(stripped);
    }

    private Task<GameAgentSectorSnapshot?> QueryMtcRpcSectorAsync(int sector)
        => InvokeMtcRpcUiAsync(() => Task.FromResult(GameAgentRuntime.BuildSectorSnapshot(_sessionDb, sector)));

    private async Task<IReadOnlyList<GameAgentRunningScriptSnapshot>> ListMtcRpcScriptsAsync()
    {
        GameAgentContextSnapshot context = await BuildMtcRpcContextAsync(recentEventCount: 0).ConfigureAwait(false);
        return context.RunningScripts;
    }

    private Task<MtcRpcActionResult> SendMtcRpcCommandAsync(string command, bool appendEnter)
        => InvokeMtcRpcUiAsync(() =>
        {
            if (IsLearnModeActive())
                return Task.FromResult(MtcRpcActionResult.Fail(
                    "Game-agent commands are disabled while Learn mode is observing human gameplay."));
            if (!IsMtcRpcConnected())
                return Task.FromResult(MtcRpcActionResult.Fail("No active game connection."));

            string payload = command ?? string.Empty;
            if (appendEnter && !payload.EndsWith('\r') && !payload.EndsWith('\n'))
                payload += "\r";

            byte[] bytes = Encoding.Latin1.GetBytes(payload);
            if (_terminalInputHandler == null)
                return Task.FromResult(MtcRpcActionResult.Fail("Terminal input is not available."));

            _terminalInputHandler.Invoke(bytes);
            return Task.FromResult(MtcRpcActionResult.Ok("Command submitted through the MTC terminal input path.", new Dictionary<string, string>
            {
                ["bytes"] = bytes.Length.ToString(),
                ["appendEnter"] = appendEnter ? "true" : "false",
            }));
        });

    private Task<MtcRpcActionResult> SendMtcRpcScriptInputAsync(
        GameScriptInputPrompt prompt,
        string response)
        => InvokeMtcRpcUiAsync(() =>
        {
            if (IsLearnModeActive())
                return Task.FromResult(MtcRpcActionResult.Fail(
                    "Game-agent script input is disabled while Learn mode is observing human gameplay."));
            if (!ScriptInputPromptPolicy.IsSafeResponse(prompt, response))
            {
                return Task.FromResult(MtcRpcActionResult.Fail(
                    "The script response is not permitted by the observed prompt."));
            }

            GameScriptInputPrompt? currentPrompt = _gameAgent.GetCurrentScriptInputPrompt();
            if (currentPrompt is null ||
                currentPrompt.ObservedAt != prompt.ObservedAt ||
                currentPrompt.SingleKey != prompt.SingleKey ||
                !string.Equals(currentPrompt.ScriptName, prompt.ScriptName, StringComparison.Ordinal) ||
                !string.Equals(currentPrompt.Text, prompt.Text, StringComparison.Ordinal))
            {
                return Task.FromResult(MtcRpcActionResult.Fail(
                    "The script input prompt changed before the response could be delivered."));
            }

            Core.ModInterpreter? interpreter = CurrentInterpreter;
            if (interpreter is null || !interpreter.IsAnyScriptWaitingForInput())
            {
                return Task.FromResult(MtcRpcActionResult.Fail(
                    "No TWX script is currently waiting for input."));
            }

            _gameAgent.ClearScriptInputPrompt();
            if (!interpreter.LocalInputEvent(response))
            {
                return Task.FromResult(MtcRpcActionResult.Fail(
                    "The waiting TWX script did not consume the response."));
            }

            return Task.FromResult(MtcRpcActionResult.Ok(
                "Response delivered directly to the waiting TWX script."));
        });

    private Task<MtcRpcSensitivePrompt?> GetMtcRpcSensitivePasswordPromptAsync()
        => InvokeMtcRpcUiAsync(() =>
        {
            MtcRpcSensitivePrompt? prompt =
                MtcRpcSecretInputPolicy.FindCurrentAccountSetupPasswordPrompt(
                    _gameAgent.GetRecentEvents(120),
                    _state.Connected,
                    GetGameAgentGameName(),
                    _state.Host,
                    _state.Port,
                    DateTimeOffset.UtcNow);

            return Task.FromResult(
                prompt != null &&
                !string.Equals(prompt.PromptId, _lastMtcRpcSensitivePromptConsumed, StringComparison.Ordinal)
                    ? prompt
                    : null);
        });

    private Task<MtcRpcActionResult> SendMtcRpcSensitivePasswordAsync(string promptId, byte[] secret)
        => InvokeMtcRpcUiAsync(() =>
        {
            MtcRpcSensitivePrompt? currentPrompt =
                MtcRpcSecretInputPolicy.FindCurrentAccountSetupPasswordPrompt(
                    _gameAgent.GetRecentEvents(120),
                    _state.Connected,
                    GetGameAgentGameName(),
                    _state.Host,
                    _state.Port,
                    DateTimeOffset.UtcNow);

            if (currentPrompt == null ||
                !string.Equals(currentPrompt.PromptId, promptId, StringComparison.Ordinal) ||
                string.Equals(promptId, _lastMtcRpcSensitivePromptConsumed, StringComparison.Ordinal))
            {
                return Task.FromResult(MtcRpcActionResult.Fail("The approved password prompt is no longer current."));
            }

            if (_terminalInputHandler == null)
            {
                return Task.FromResult(MtcRpcActionResult.Fail("Sensitive terminal input is unavailable."));
            }

            bool initialPassword = string.Equals(currentPrompt.Stage, "password", StringComparison.Ordinal);
            if (initialPassword)
            {
                _mtcRpcPasswordConfirmationGuard.Begin(secret);
            }
            else if (!_mtcRpcPasswordConfirmationGuard.Matches(secret))
            {
                _mtcRpcPasswordConfirmationGuard.Clear();
                return Task.FromResult(MtcRpcActionResult.Fail(
                    "The confirmation credential does not match the initial protected submission."));
            }

            byte[] payload;
            try
            {
                payload = MtcRpcSecretInputPolicy.CreateCredentialPasswordLine(secret);
            }
            catch
            {
                _mtcRpcPasswordConfirmationGuard.Clear();
                return Task.FromResult(MtcRpcActionResult.Fail("Sensitive terminal input is unavailable."));
            }

            try
            {
                _sensitiveTerminalInputInProgress = true;
                _sensitiveOutputRedactionRequired = true;
                _terminalInputHandler.Invoke(payload);
                _lastMtcRpcSensitivePromptConsumed = promptId;
                if (!initialPassword)
                    _mtcRpcPasswordConfirmationGuard.Clear();
                return Task.FromResult(MtcRpcActionResult.Ok(
                    "Credential input submitted to the verified account-setup prompt."));
            }
            catch
            {
                _mtcRpcPasswordConfirmationGuard.Clear();
                return Task.FromResult(MtcRpcActionResult.Fail("Sensitive terminal input could not be submitted."));
            }
            finally
            {
                _sensitiveTerminalInputInProgress = false;
                System.Security.Cryptography.CryptographicOperations.ZeroMemory(payload);
            }
        });

    private Task<MtcRpcActionResult> RunMtcRpcScriptAsync(string script)
        => InvokeMtcRpcUiAsync(() =>
        {
            if (IsLearnModeActive())
                return Task.FromResult(MtcRpcActionResult.Fail(
                    "Game-agent scripts are disabled while Learn mode is observing human gameplay."));
            string scriptReference = (script ?? string.Empty).Trim().Replace('\\', '/');
            if (string.IsNullOrWhiteSpace(scriptReference))
                return Task.FromResult(MtcRpcActionResult.Fail("Script name is required."));

            bool isWorldTradeScript =
                WorldTradeScriptStartPolicy.IsWorldTradeScriptReference(scriptReference);
            DateTimeOffset now = DateTimeOffset.UtcNow;
            GameAgentContextSnapshot contextSnapshot = BuildGameAgentContextSnapshot();
            string? worldTradeBlockReason = WorldTradeScriptStartPolicy.GetBlockReason(
                scriptReference,
                contextSnapshot,
                BuildIntegratedGameClientSnapshot().StarDockSector,
                now);
            if (worldTradeBlockReason is not null)
                return Task.FromResult(MtcRpcActionResult.Fail(worldTradeBlockReason));

            if (RequiresTradeWarsCommandPrompt(scriptReference) &&
                !_gameAgent.IsAtTradeWarsCommandPrompt())
            {
                return Task.FromResult(MtcRpcActionResult.Fail(
                    "CIM scripts require the authenticated TradeWars Command prompt."));
            }

            Core.ModInterpreter? interpreter = CurrentInterpreter;
            bool remoteProxyScripts = interpreter == null && CanUseRemoteProxyScripts();
            if (interpreter == null && !remoteProxyScripts)
                return Task.FromResult(MtcRpcActionResult.Fail("Proxy scripts are not available for the current game."));
            if (isWorldTradeScript && interpreter == null)
            {
                return Task.FromResult(MtcRpcActionResult.Fail(
                    "TWX World Trade requires the local script interpreter so its verified GO! menu can be started autonomously."));
            }

            try
            {
                if (interpreter != null)
                {
                    if (isWorldTradeScript)
                    {
                        string scriptsDirectory = GetConfiguredScriptsDirectoryValue();
                        if (string.IsNullOrWhiteSpace(scriptsDirectory))
                        {
                            return Task.FromResult(MtcRpcActionResult.Fail(
                                "The configured TWX scripts directory is missing; World Trade was not loaded."));
                        }

                        scriptReference = ResolveWorldTradeScriptPath(
                            scriptReference,
                            scriptsDirectory);
                    }

                    if (RequiresTradeWarsCommandPrompt(scriptReference))
                    {
                        string scriptsDirectory = GetConfiguredScriptsDirectoryValue();
                        if (string.IsNullOrWhiteSpace(scriptsDirectory))
                        {
                            return Task.FromResult(MtcRpcActionResult.Fail(
                                "The configured TWX scripts directory is missing; CIM script was not loaded."));
                        }

                        scriptReference = ResolveCimScriptPath(scriptReference, scriptsDirectory);
                        if (!File.Exists(scriptReference))
                        {
                            return Task.FromResult(MtcRpcActionResult.Fail(
                                $"Required CIM script was not found at '{scriptReference}'."));
                        }
                    }

                    Core.ProxyGameOperations.LoadScript(interpreter, scriptReference);
                    if (!IsRequestedScriptActive(
                            Core.ProxyGameOperations.GetRunningScripts(interpreter),
                            scriptReference))
                    {
                        return Task.FromResult(MtcRpcActionResult.Fail(
                            $"TWX script '{scriptReference}' did not remain active after loading. Check its compiler diagnostics and interpreter output."));
                    }

                    if (isWorldTradeScript &&
                        !Core.ProxyGameOperations.TrySetScriptVariable(
                            interpreter,
                            scriptReference,
                            "$MOVE~EXTRASEND",
                            string.Empty))
                    {
                        bool stopped = Core.ProxyGameOperations.StopScriptByName(
                            interpreter,
                            scriptReference);
                        string stopStatus = stopped
                            ? " The waiting script was stopped."
                            : " The waiting script could not be stopped.";
                        return Task.FromResult(MtcRpcActionResult.Fail(
                            "TWX World Trade could not disable its extra movement commands for autonomous trading." +
                            stopStatus));
                    }

                    bool densityOnlyScanner = isWorldTradeScript &&
                        WorldTradeScriptStartPolicy.IsDensityOnlyScanner(
                            contextSnapshot,
                            now);
                    if (isWorldTradeScript &&
                        !Core.ProxyGameOperations.TrySetScriptVariable(
                            interpreter,
                            scriptReference,
                            "$MOVE~DENSITYONLYSCANNER",
                            densityOnlyScanner ? "1" : "0"))
                    {
                        bool stopped = Core.ProxyGameOperations.StopScriptByName(
                            interpreter,
                            scriptReference);
                        string stopStatus = stopped
                            ? " The waiting script was stopped."
                            : " The waiting script could not be stopped.";
                        return Task.FromResult(MtcRpcActionResult.Fail(
                            "TWX World Trade could not configure the scanner-specific density-scan command." +
                            stopStatus));
                    }

                    if (densityOnlyScanner &&
                        !Core.ProxyGameOperations.TrySetScriptVariable(
                            interpreter,
                            scriptReference,
                            "$MOVE~SCANHOLO",
                            "0"))
                    {
                        bool stopped = Core.ProxyGameOperations.StopScriptByName(
                            interpreter,
                            scriptReference);
                        string stopStatus = stopped
                            ? " The waiting script was stopped."
                            : " The waiting script could not be stopped.";
                        return Task.FromResult(MtcRpcActionResult.Fail(
                            "TWX World Trade could not disable holo-scanning for the verified Density-only ship." +
                            stopStatus));
                    }

                    if (isWorldTradeScript &&
                        !Core.ProxyGameOperations.TryHandleScriptMenuOption(
                            interpreter,
                            scriptReference,
                            "WORLDTRADE",
                            "GO"))
                    {
                        bool stopped = Core.ProxyGameOperations.StopScriptByName(
                            interpreter,
                            scriptReference);
                        string stopStatus = stopped
                            ? " The waiting script was stopped."
                            : " The waiting script could not be stopped.";
                        return Task.FromResult(MtcRpcActionResult.Fail(
                            "TWX World Trade loaded but its expected WorldTrade GO! settings-menu option could not be safely selected." +
                            stopStatus));
                    }
                }
                else
                    SendProxyMenuCommand($"ss {scriptReference}");

                _parser.Feed($"\x1b[1;36m[JSON-RPC loaded script: {scriptReference}]\x1b[0m\r\n");
                _buffer.Dirty = true;
                RebuildProxyMenu();
                RebuildScriptsMenu();
                return Task.FromResult(MtcRpcActionResult.Ok("Script load requested.", new Dictionary<string, string>
                {
                    ["script"] = scriptReference,
                }));
            }
            catch (Exception ex)
            {
                return Task.FromResult(MtcRpcActionResult.Fail(ex.Message));
            }
        });

    internal static bool RequiresTradeWarsCommandPrompt(string scriptReference)
    {
        string normalized = (scriptReference ?? string.Empty).Trim().Replace('\\', '/');
        string scriptName = normalized[(normalized.LastIndexOf('/') + 1)..];
        return string.Equals(scriptName, "MTC_PortCIM.ts", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(scriptName, "MTC_WarpCIM.ts", StringComparison.OrdinalIgnoreCase);
    }

    internal static string ResolveCimScriptPath(string scriptReference, string scriptsDirectory)
    {
        if (string.IsNullOrWhiteSpace(scriptsDirectory))
            throw new ArgumentException("A configured TWX scripts directory is required.", nameof(scriptsDirectory));

        string normalized = (scriptReference ?? string.Empty).Trim().Replace('\\', '/');
        string scriptName = normalized[(normalized.LastIndexOf('/') + 1)..];
        string canonicalName = string.Equals(
            scriptName,
            "MTC_PortCIM.ts",
            StringComparison.OrdinalIgnoreCase)
            ? "MTC_PortCIM.ts"
            : string.Equals(
                scriptName,
                "MTC_WarpCIM.ts",
                StringComparison.OrdinalIgnoreCase)
                ? "MTC_WarpCIM.ts"
                : throw new ArgumentException(
                    "Only the fixed MTC CIM scripts can be resolved this way.",
                    nameof(scriptReference));

        return Path.GetFullPath(Path.Combine(scriptsDirectory, "utilities", canonicalName));
    }

    internal static string ResolveWorldTradeScriptPath(
        string scriptReference,
        string scriptsDirectory)
    {
        if (!WorldTradeScriptStartPolicy.IsWorldTradeScriptReference(scriptReference))
        {
            throw new ArgumentException(
                "Only the approved WorldTrade script can be resolved this way.",
                nameof(scriptReference));
        }
        if (string.IsNullOrWhiteSpace(scriptsDirectory))
        {
            throw new ArgumentException(
                "A configured TWX scripts directory is required.",
                nameof(scriptsDirectory));
        }

        string normalizedReference = scriptReference
            .Trim()
            .Replace('\\', Path.DirectorySeparatorChar)
            .Replace('/', Path.DirectorySeparatorChar);
        string scriptsRoot = Path.GetFullPath(scriptsDirectory);
        string resolvedPath = Path.GetFullPath(Path.Combine(scriptsRoot, normalizedReference));
        if (File.Exists(resolvedPath))
        {
            return resolvedPath;
        }

        string[] referenceParts = normalizedReference.Split(
            Path.DirectorySeparatorChar,
            StringSplitOptions.RemoveEmptyEntries);
        string scriptsRootName = Path.GetFileName(Path.TrimEndingDirectorySeparator(scriptsRoot));
        if (referenceParts.Length > 1 &&
            string.Equals(scriptsRootName, referenceParts[0], StringComparison.OrdinalIgnoreCase))
        {
            string resolvedFromCategoryRoot = Path.GetFullPath(
                Path.Combine(scriptsRoot, Path.Combine(referenceParts.Skip(1).ToArray())));
            if (File.Exists(resolvedFromCategoryRoot))
            {
                return resolvedFromCategoryRoot;
            }
        }

        throw new FileNotFoundException(
            $"The approved WorldTrade script was not found under the configured TWX scripts directory. Checked '{resolvedPath}'.",
            resolvedPath);
    }

    internal static bool IsRequestedScriptActive(
        IReadOnlyList<Core.RunningScriptInfo> runningScripts,
        string scriptReference)
    {
        string requested = scriptReference.Trim().Replace('\\', '/').TrimStart('/');
        string requestedName = requested[(requested.LastIndexOf('/') + 1)..];

        return runningScripts.Any(running =>
        {
            string reference = running.Reference.Trim().Replace('\\', '/').TrimStart('/');
            return string.Equals(reference, requested, StringComparison.OrdinalIgnoreCase) ||
                reference.EndsWith('/' + requested, StringComparison.OrdinalIgnoreCase) ||
                string.Equals(running.Name, requestedName, StringComparison.OrdinalIgnoreCase);
        });
    }

    private Task<MtcRpcActionResult> StopMtcRpcScriptAsync(int? id, string? name)
        => InvokeMtcRpcUiAsync(() =>
        {
            if (IsLearnModeActive())
                return Task.FromResult(MtcRpcActionResult.Fail(
                    "Game-agent script controls are disabled while Learn mode is observing human gameplay."));
            Core.ModInterpreter? interpreter = CurrentInterpreter;
            bool remoteProxyScripts = interpreter == null && CanUseRemoteProxyScripts();
            if (interpreter == null && !remoteProxyScripts)
                return Task.FromResult(MtcRpcActionResult.Fail("Proxy scripts are not available for the current game."));

            try
            {
                bool stopped;
                if (interpreter != null && id is { } scriptId)
                {
                    stopped = Core.ProxyGameOperations.StopScriptById(interpreter, scriptId);
                    return Task.FromResult(stopped
                        ? MtcRpcActionResult.Ok($"Script ID {scriptId} stopped.")
                        : MtcRpcActionResult.Fail($"Script ID {scriptId} was not found."));
                }

                if (interpreter != null && !string.IsNullOrWhiteSpace(name))
                {
                    stopped = Core.ProxyGameOperations.StopScriptByName(interpreter, name);
                    return Task.FromResult(stopped
                        ? MtcRpcActionResult.Ok($"Script '{name}' stopped.")
                        : MtcRpcActionResult.Fail($"Script '{name}' was not found."));
                }

                if (remoteProxyScripts && id is { } remoteId)
                {
                    SendProxyMenuCommand($"sk {remoteId}");
                    return Task.FromResult(MtcRpcActionResult.Ok($"Remote proxy kill requested for script ID {remoteId}."));
                }

                return Task.FromResult(MtcRpcActionResult.Fail("Stopping a remote script by name is not supported; use id."));
            }
            catch (Exception ex)
            {
                return Task.FromResult(MtcRpcActionResult.Fail(ex.Message));
            }
        });

    private Task<bool> ApproveMtcRpcActionAsync(string action, string details)
        => InvokeMtcRpcUiAsync(() => ShowConfirmAsync(
            "JSON-RPC Action Approval",
            $"{action}\n\n{details}\n\nAllow this JSON-RPC client action?",
            "Allow",
            "Reject"));

    private bool IsMtcRpcConnected()
        => _gameInstance?.IsConnected == true || _telnet.IsConnected;

    private Task<T> InvokeMtcRpcUiAsync<T>(Func<Task<T>> action)
    {
        if (Dispatcher.UIThread.CheckAccess())
            return action();

        var tcs = new TaskCompletionSource<T>(TaskCreationOptions.RunContinuationsAsynchronously);
        Dispatcher.UIThread.Post(async () =>
        {
            try
            {
                tcs.SetResult(await action().ConfigureAwait(true));
            }
            catch (Exception ex)
            {
                tcs.SetException(ex);
            }
        });
        return tcs.Task;
    }
}
