using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Threading;
using LLMSurvivingProxyAgent.Core;
using LLMSurvivingProxyAgent.Runtime;

namespace MTC;

internal sealed class GameAgentWindow : Window
{
    private readonly Func<GameAgentContextSnapshot> _getContext;
    private readonly Func<string, bool, Task<MtcRpcActionResult>> _sendCommandAsync;
    private readonly Func<string, Task<MtcRpcActionResult>> _executeMombotCommandAsync;
    private readonly Func<string, Task<MtcRpcActionResult>> _runScriptAsync;
    private readonly Func<int?, string?, Task<MtcRpcActionResult>> _stopScriptAsync;
    private readonly Func<GameAgentBackgroundStatusSnapshot> _getBackgroundStatus;
    private readonly AppPreferences _preferences;
    private readonly IGameAgentModel _localModel = new LocalObserverGameAgentModel();
    private readonly TextBox _conversationBox;
    private readonly TextBox _inputBox;
    private readonly TextBox _contextBox;
    private readonly ComboBox _integrationCombo;
    private readonly ComboBox _modelCombo;
    private readonly TextBlock _statusText;
    private readonly Button _autoStepButton;
    private readonly Button _autoRunButton;
    private readonly Button _stopAutomationButton;
    private readonly TextBlock _modelMonitorText;
    private readonly TextBlock _usageMonitorText;
    private readonly TextBlock _processMonitorText;
    private readonly TextBlock _scheduleMonitorText;
    private readonly DispatcherTimer _monitorTimer;
    private readonly TimeSpan _presenceHeartbeatInterval;
    private CancellationTokenSource? _automationCts;
    private GameAgentContextSnapshot? _latestContext;
    private DateTimeOffset? _modelRequestStartedAt;
    private DateTimeOffset? _lastModelRequestAt;
    private TimeSpan? _lastModelRequestDuration;
    private string _activeOperation = "Idle";
    private DateTimeOffset? _activeOperationStartedAt;
    private string _lastModelStatus = "No model requests yet";
    private int _modelRequestCount;
    private int _modelRequestFailures;
    private int _modelFallbackCount;
    private int _usageReportCount;
    private long _inputTokenTotal;
    private long _outputTokenTotal;
    private long _reasoningTokenTotal;
    private bool _hasInputTokenTotal;
    private bool _hasOutputTokenTotal;
    private bool _hasReasoningTokenTotal;
    private int _monitorTimerTicks;

    public GameAgentWindow(
        Func<GameAgentContextSnapshot> getContext,
        Func<string, bool, Task<MtcRpcActionResult>> sendCommandAsync,
        Func<string, Task<MtcRpcActionResult>> executeMombotCommandAsync,
        Func<string, Task<MtcRpcActionResult>> runScriptAsync,
        Func<int?, string?, Task<MtcRpcActionResult>> stopScriptAsync,
        Func<GameAgentBackgroundStatusSnapshot> getBackgroundStatus,
        TimeSpan presenceHeartbeatInterval,
        AppPreferences preferences)
    {
        _getContext = getContext;
        _sendCommandAsync = sendCommandAsync;
        _executeMombotCommandAsync = executeMombotCommandAsync;
        _runScriptAsync = runScriptAsync;
        _stopScriptAsync = stopScriptAsync;
        _getBackgroundStatus = getBackgroundStatus;
        _presenceHeartbeatInterval = presenceHeartbeatInterval;
        _preferences = preferences;

        Title = "Game Agent";
        Width = 1050;
        Height = 720;
        MinWidth = 780;
        MinHeight = 640;
        Background = new SolidColorBrush(Color.FromRgb(0x07, 0x12, 0x17));
        FontFamily = new FontFamily("Cascadia Code, Menlo, Consolas, Courier New, monospace");

        _conversationBox = BuildReadOnlyBox("The game agent conversation will appear here.");
        _contextBox = BuildReadOnlyBox("Live game context will appear here.");
        _contextBox.MinWidth = 260;
        _modelMonitorText = BuildMonitorValue();
        _usageMonitorText = BuildMonitorValue();
        _processMonitorText = BuildMonitorValue();
        _scheduleMonitorText = BuildMonitorValue();
        _monitorTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(1) };
        _monitorTimer.Tick += (_, _) => RefreshMonitorTick();

        _inputBox = new TextBox
        {
            AcceptsReturn = true,
            TextWrapping = TextWrapping.Wrap,
            MinHeight = 80,
            Watermark = "Ask or command the agent. Try: start World Trade, stop script 3, or answer the current script prompt.",
            Background = new SolidColorBrush(Color.FromRgb(0x03, 0x1d, 0x26)),
            Foreground = Brushes.Gainsboro,
            BorderBrush = new SolidColorBrush(Color.FromRgb(0x12, 0x5e, 0x70)),
            Padding = new Thickness(8),
        };
        _inputBox.AddHandler(KeyDownEvent, OnInputKeyDown, RoutingStrategies.Tunnel);

        _integrationCombo = new ComboBox
        {
            ItemsSource = BuildIntegrationChoices(),
            SelectedItem = FindIntegrationChoice(AppPreferences.NormalizeGameAgentProvider(_preferences.GameAgentProvider)),
            Width = 160,
            Background = new SolidColorBrush(Color.FromRgb(0x03, 0x1d, 0x26)),
            Foreground = Brushes.Gainsboro,
            BorderBrush = new SolidColorBrush(Color.FromRgb(0x12, 0x5e, 0x70)),
            Padding = new Thickness(6),
        };
        _integrationCombo.SelectionChanged += (_, _) => UpdateIntegrationControls();

        _modelCombo = new ComboBox
        {
            ItemsSource = new[] { ResolveInitialProviderModel(_preferences, GetSelectedProviderId()) },
            SelectedIndex = 0,
            Width = 220,
            Background = new SolidColorBrush(Color.FromRgb(0x03, 0x1d, 0x26)),
            Foreground = Brushes.Gainsboro,
            BorderBrush = new SolidColorBrush(Color.FromRgb(0x12, 0x5e, 0x70)),
            Padding = new Thickness(6),
        };
        _modelCombo.SelectionChanged += (_, _) =>
        {
            SavePreferences();
            RefreshMonitorPanel();
        };

        var refreshModelsButton = new Button
        {
            Content = "Models",
            MinWidth = 76,
            Height = 32,
        };
        refreshModelsButton.Click += async (_, _) => await RefreshLmStudioModelsAsync();

        var askButton = new Button
        {
            Content = "Ask",
            MinWidth = 100,
            Height = 32,
        };
        askButton.Click += (_, _) => Submit();

        var refreshButton = new Button
        {
            Content = "Refresh Context",
            MinWidth = 140,
            Height = 32,
        };
        refreshButton.Click += (_, _) => RefreshContext();

        var snapshotButton = new Button
        {
            Content = "Save Snapshot",
            MinWidth = 130,
            Height = 32,
        };
        snapshotButton.Click += (_, _) => SaveSnapshot();

        var sampleButton = new Button
        {
            Content = "Export Sample",
            MinWidth = 130,
            Height = 32,
        };
        sampleButton.Click += (_, _) => ExportTrainingSample();

        _autoStepButton = new Button
        {
            Content = "Run Request",
            MinWidth = 110,
            Height = 32,
            IsVisible = false,
        };
        _autoStepButton.Click += (_, _) => Submit();

        _autoRunButton = new Button
        {
            Content = "Run Suggested Action",
            MinWidth = 110,
            Height = 32,
            IsVisible = false,
        };
        _autoRunButton.Click += async (_, _) => await RunCopilotSuggestionAsync(10);

        _stopAutomationButton = new Button
        {
            Content = "Stop",
            MinWidth = 90,
            Height = 32,
            IsEnabled = false,
        };
        _stopAutomationButton.Click += (_, _) => StopAutomation();

        var clearButton = new Button
        {
            Content = "Clear",
            MinWidth = 100,
            Height = 32,
        };
        clearButton.Click += (_, _) => _conversationBox.Text = BuildWelcomeMessage();

        _statusText = new TextBlock
        {
            Text = "Type an advice question or an action request. Action requests execute in the game stream.",
            Foreground = new SolidColorBrush(Color.FromRgb(0x8a, 0xb8, 0xc0)),
            VerticalAlignment = VerticalAlignment.Center,
            TextWrapping = TextWrapping.Wrap,
        };

        var buttons = new WrapPanel
        {
            HorizontalAlignment = HorizontalAlignment.Right,
            Children =
            {
                WithControlMargin(refreshButton),
                WithControlMargin(snapshotButton),
                WithControlMargin(sampleButton),
                WithControlMargin(clearButton),
                WithControlMargin(askButton),
            },
        };

        var automationButtons = new WrapPanel
        {
            HorizontalAlignment = HorizontalAlignment.Right,
            Children =
            {
                WithControlMargin(_stopAutomationButton),
            },
        };

        var modelRow = new WrapPanel
        {
            Children =
            {
                WithControlMargin(new TextBlock
                {
                    Text = "Provider",
                    Foreground = new SolidColorBrush(Color.FromRgb(0x8a, 0xb8, 0xc0)),
                    VerticalAlignment = VerticalAlignment.Center,
                }),
                WithControlMargin(_integrationCombo),
                WithControlMargin(refreshModelsButton),
                WithControlMargin(_modelCombo),
            },
        };

        var controlPanel = new StackPanel
        {
            Spacing = 8,
            Children =
            {
                modelRow,
                _inputBox,
                _statusText,
                automationButtons,
                buttons,
            },
        };

        var leftPane = new Grid
        {
            RowDefinitions = new RowDefinitions("*,Auto"),
            RowSpacing = 10,
        };
        leftPane.Children.Add(WrapPanel("Conversation", _conversationBox).WithRow(0));
        leftPane.Children.Add(controlPanel.WithRow(1));

        var mainGrid = new Grid
        {
            ColumnDefinitions = new ColumnDefinitions("*,320"),
            Margin = new Thickness(14),
            ColumnSpacing = 12,
        };

        mainGrid.Children.Add(leftPane.WithColumn(0));
        var monitorTabs = new TabControl();
        monitorTabs.Items.Add(new TabItem
        {
            Header = "Context",
            Content = WrapPanel("Live Context", _contextBox),
        });
        monitorTabs.Items.Add(new TabItem
        {
            Header = "Monitor",
            Content = BuildMonitorView(),
        });
        mainGrid.Children.Add(monitorTabs.WithColumn(1));

        Content = mainGrid;
        _conversationBox.Text = BuildWelcomeMessage();
        UpdateIntegrationControls();
        RefreshMonitorPanel();

        Opened += (_, _) =>
        {
            SafeRefreshContext("Could not load the live game context");
            _monitorTimer.Start();
            try
            {
                _inputBox.Focus();
            }
            catch
            {
                // Focus is best-effort during startup; the window should still open.
            }

            _ = RefreshLmStudioModelsAsync();
        };
        Closed += (_, _) =>
        {
            _monitorTimer.Stop();
            StopAutomation();
        };
    }

    private Control BuildMonitorView()
    {
        var sections = new StackPanel
        {
            Spacing = 8,
            Margin = new Thickness(2),
            Children =
            {
                BuildMonitorCard("MODEL & THINKING", _modelMonitorText),
                BuildMonitorCard("LLM USAGE", _usageMonitorText),
                BuildMonitorCard("LIVE PROCESSES", _processMonitorText),
                BuildMonitorCard("CHECKS & TIMERS", _scheduleMonitorText),
            },
        };
        return new ScrollViewer
        {
            VerticalScrollBarVisibility = Avalonia.Controls.Primitives.ScrollBarVisibility.Auto,
            Content = sections,
        };
    }

    private static Control BuildMonitorCard(string title, TextBlock value)
    {
        var content = new StackPanel
        {
            Spacing = 5,
            Children =
            {
                new TextBlock
                {
                    Text = title,
                    Foreground = new SolidColorBrush(Color.FromRgb(0x00, 0xd4, 0xc9)),
                    FontSize = 12,
                    FontWeight = FontWeight.SemiBold,
                },
                value,
            },
        };
        return new Border
        {
            Background = new SolidColorBrush(Color.FromRgb(0x0b, 0x26, 0x30)),
            BorderBrush = new SolidColorBrush(Color.FromRgb(0x1b, 0x82, 0x95)),
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(8),
            Padding = new Thickness(10),
            Child = content,
        };
    }

    private static TextBlock BuildMonitorValue()
        => new()
        {
            Text = "Waiting for runtime data.",
            Foreground = Brushes.Gainsboro,
            FontFamily = new FontFamily("Cascadia Code, Menlo, Consolas, Courier New, monospace"),
            FontSize = 12,
            TextWrapping = TextWrapping.Wrap,
        };

    public void RefreshContext()
    {
        GameAgentContextSnapshot context = _getContext();
        _latestContext = context;
        _contextBox.Text = BuildContextText(context);
        _statusText.Text = $"Watching {context.GameName}; {context.RecentEvents.Count} recent event(s) loaded.";
        RefreshMonitorPanel();
    }

    private void RefreshMonitorTick()
    {
        if (++_monitorTimerTicks % 5 == 0)
        {
            try
            {
                _latestContext = _getContext();
                _contextBox.Text = BuildContextText(_latestContext);
            }
            catch (Exception ex)
            {
                _processMonitorText.Text = $"Could not refresh live process state: {ex.Message}";
            }
        }

        RefreshMonitorPanel();
    }

    private void RefreshMonitorPanel()
    {
        GameAgentContextSnapshot? context = _latestContext;
        string provider = GetSelectedProviderId();
        string model = provider == "local"
            ? "Local observer"
            : _modelCombo.SelectedItem?.ToString() ?? ResolveInitialProviderModel(_preferences, provider);
        string thinkingMode = provider switch
        {
            "local" => "Local observer; no generative reasoning",
            "ollama" => "Thinking disabled (think=false)",
            "anthropic" => "Provider default; extended thinking not requested",
            _ => "Provider default; reasoning effort not requested",
        };
        string outputLimit = provider switch
        {
            "ollama" => "384 generated tokens",
            "local" => "No token generation",
            _ => "900 generated tokens",
        };
        _modelMonitorText.Text =
            $"Provider: {GameAgentProviders.Find(provider).Label}\n" +
            $"Model: {(string.IsNullOrWhiteSpace(model) ? "not selected" : model)}\n" +
            $"Thinking: {thinkingMode}\n" +
            $"Generation: temperature 0.2 · {outputLimit}\n" +
            $"Context limit: {_preferences.GameAgentContextLimitCharacters:N0} characters\n" +
            $"Current: {GetOperationDescription()}";

        string inputTokens = _hasInputTokenTotal ? _inputTokenTotal.ToString("N0") : "not reported";
        string outputTokens = _hasOutputTokenTotal ? _outputTokenTotal.ToString("N0") : "not reported";
        string reasoningTokens = _hasReasoningTokenTotal ? _reasoningTokenTotal.ToString("N0") : "not reported";
        string latency = _lastModelRequestDuration is { } duration
            ? FormatDuration(duration)
            : "not available";
        _usageMonitorText.Text =
            $"Completed requests: {_modelRequestCount:N0}\n" +
            $"Input / output tokens: {inputTokens} / {outputTokens}\n" +
            $"Reasoning tokens: {reasoningTokens}\n" +
            $"Usage reports: {_usageReportCount:N0} · last latency: {latency}\n" +
            $"Last request: {FormatTimestamp(_lastModelRequestAt)}\n" +
            $"Fallbacks: {_modelFallbackCount:N0} · errors: {_modelRequestFailures:N0}\n" +
            $"Last result: {_lastModelStatus}";
        if (_modelRequestStartedAt is { } startedAt)
        {
            _usageMonitorText.Text +=
                $"\nRequest elapsed: {FormatDuration(DateTimeOffset.UtcNow - startedAt)} / " +
                $"{FormatDuration(GameAgentProviders.RequestTimeout)} timeout";
        }

        if (context is null)
        {
            _processMonitorText.Text = "Waiting for a live game-context snapshot.";
            _scheduleMonitorText.Text = "Checks and timers will appear when the runtime context is available.";
            return;
        }

        GameAgentBackgroundStatusSnapshot background;
        try
        {
            background = _getBackgroundStatus();
        }
        catch (Exception ex)
        {
            _processMonitorText.Text = $"Runtime status unavailable: {ex.Message}";
            _scheduleMonitorText.Text = "Background check schedule is unavailable until runtime status recovers.";
            return;
        }

        var processLines = new StringBuilder();
        processLines.Append("Game session: ")
            .AppendLine(context.Connected ? $"connected · {context.CurrentPrompt}" : "disconnected");
        processLines.Append("TWX scripts: ");
        if (context.RunningScripts.Count == 0)
        {
            processLines.AppendLine("none observed");
        }
        else
        {
            processLines.AppendLine();
            foreach (GameAgentRunningScriptSnapshot script in context.RunningScripts.Take(10))
            {
                processLines.Append("  #").Append(script.Id).Append(' ').Append(script.Name);
                if (script.Paused)
                    processLines.Append(" · paused");
                if (script.IsSystemScript)
                    processLines.Append(" · system");
                processLines.AppendLine();
            }
        }
        processLines.Append("Native Mombot: ")
            .AppendLine(context.Bot.NativeMombotRunning
                ? $"active · {Display(context.Bot.LastLoadedModule)}"
                : "inactive");
        processLines.Append("Embedded dashboard: ")
            .AppendLine(background.DashboardHostAvailable ? "online" : "not started");
        processLines.Append("Agent runtime: ")
            .AppendLine(background.Runtime is { IsRunning: true } ? "running" : "idle");
        if (background.LearningModeRunning)
            processLines.AppendLine("Agent mode: LEARN (observe-only; no game commands)");

        if (background.AutonomousWorkerRunning)
        {
            processLines.Append("Autonomous worker: RUNNING");
            if (background.Worker is { } worker)
            {
                processLines.Append(" · ").AppendLine(Display(worker.Activity));
                processLines.Append("Last worker update: ")
                    .AppendLine(FormatTimestamp(worker.UpdatedAt));
                if (!string.IsNullOrWhiteSpace(worker.LastAction))
                    processLines.Append("Last action: ").AppendLine(worker.LastAction);
                processLines.Append("Action watchdog: ")
                    .Append(worker.WatchdogUnchangedCycles)
                    .Append('/')
                    .Append(AutonomousActionWatchdog.RequiredUnchangedCycles)
                    .AppendLine(" unchanged cycles");
            }
            else
            {
                processLines.AppendLine();
            }
        }
        else
        {
            processLines.AppendLine("Autonomous worker: stopped");
            if (background.Worker is { } stoppedWorker &&
                stoppedWorker.UpdatedAt != DateTimeOffset.MinValue &&
                !string.IsNullOrWhiteSpace(stoppedWorker.Activity))
            {
                processLines.Append("Agent startup/status: ")
                    .AppendLine(Display(stoppedWorker.Activity));
            }
        }
        if (background.Worker is { } pilotStatus)
        {
            _usageMonitorText.Text +=
                $"\n\nPilot model: {Display(pilotStatus.ModelProvider)} / {Display(pilotStatus.ModelName)}\n" +
                $"Pilot thinking: {Display(pilotStatus.ModelThinkingMode)}\n" +
                $"Pilot decisions: {pilotStatus.ModelRequestsCompleted:N0} complete · " +
                $"{pilotStatus.ModelRequestFailures:N0} failed\n" +
                $"Pilot last latency: {(pilotStatus.LastModelRequestDuration is { } pilotDuration ? FormatDuration(pilotDuration) : "not available")}";
            if (pilotStatus.ModelRequestStartedAt is { } pilotStartedAt)
            {
                _usageMonitorText.Text +=
                    $"\nPilot currently thinking: {FormatDuration(DateTimeOffset.UtcNow - pilotStartedAt)}";
            }
            if (!string.IsNullOrWhiteSpace(pilotStatus.LastModelRequestError))
                _usageMonitorText.Text += $"\nPilot last error: {pilotStatus.LastModelRequestError}";
        }

        if (background.Runtime is { } runtimeStatus)
        {
            processLines.Append("Reactive queue: ").Append(runtimeStatus.ReactiveQueueDepth)
                .Append(" · strategic queue: ").AppendLine(runtimeStatus.StrategicQueueDepth.ToString());
            processLines.Append("Last reactive cycle: ").AppendLine(FormatTimestamp(runtimeStatus.LastReactiveCycleAt));
            processLines.Append("Last strategic cycle: ").AppendLine(FormatTimestamp(runtimeStatus.LastStrategicCycleAt));
        }
        _processMonitorText.Text = processLines.ToString().TrimEnd();

        var scheduleLines = new StringBuilder();
        DateTimeOffset now = DateTimeOffset.UtcNow;
        GameAgentEvent? lastHeartbeat = context.RecentEvents
            .Where(evt => evt.Kind == GameAgentEventKind.PresenceHeartbeat)
            .MaxBy(evt => evt.Timestamp);
        scheduleLines.Append("MTC presence heartbeat: every ")
            .AppendLine(FormatDuration(_presenceHeartbeatInterval));
        if (!context.Connected)
        {
            scheduleLines.AppendLine("Next heartbeat: paused while disconnected");
        }
        else if (lastHeartbeat is { } heartbeat)
        {
            TimeSpan remaining = heartbeat.Timestamp + _presenceHeartbeatInterval - now;
            scheduleLines.Append("Next heartbeat: ")
                .AppendLine(remaining > TimeSpan.Zero ? $"in {FormatDuration(remaining)}" : "due now");
        }
        else
        {
            scheduleLines.AppendLine("Next heartbeat: awaiting first connected event");
        }

        GameAgentEvent? lastPrompt = context.RecentEvents
            .Where(evt => evt.Kind == GameAgentEventKind.ServerPrompt)
            .MaxBy(evt => evt.Timestamp);
        if (lastPrompt is { } promptEvent)
        {
            TimeSpan promptAge = now - promptEvent.Timestamp;
            TimeSpan promptFreshness = TimeSpan.FromSeconds(45);
            scheduleLines.Append("Server prompt freshness: ")
                .Append(FormatDuration(promptAge))
                .Append(" / ")
                .Append(FormatDuration(promptFreshness))
                .AppendLine(promptAge <= promptFreshness ? " · fresh" : " · stale");
        }
        else
        {
            scheduleLines.AppendLine("Server prompt freshness: no prompt observed");
        }

        if (background.Worker is { } workerStatus)
        {
            int remainingCycles = Math.Max(
                0,
                AutonomousActionWatchdog.RequiredUnchangedCycles - workerStatus.WatchdogUnchangedCycles);
            scheduleLines.Append("Watchdog: recovery review after ")
                .Append(remainingCycles)
                .AppendLine(" more unchanged Command cycle(s)");
            if (workerStatus.WatchdogNextReviewAt is { } nextReview)
            {
                TimeSpan cooldown = nextReview - now;
                scheduleLines.Append("Next watchdog review eligibility: ")
                    .AppendLine(cooldown > TimeSpan.Zero
                        ? $"in {FormatDuration(cooldown)}"
                        : "eligible on next qualifying cycle");
            }
        }
        else
        {
            scheduleLines.AppendLine("Watchdog: checks each fresh, healthy Command cycle");
        }

        scheduleLines.Append("Monitor refresh: 1 second · context refresh: 5 seconds\n")
            .Append("LLM request timeout: ")
            .AppendLine(FormatDuration(GameAgentProviders.RequestTimeout));
        scheduleLines.AppendLine("Port/warp CIM: on demand · completion wait limit 3 minutes");
        _scheduleMonitorText.Text = scheduleLines.ToString().TrimEnd();
    }

    private string GetOperationDescription()
    {
        if (_activeOperationStartedAt is not { } startedAt)
            return _activeOperation;

        return $"{_activeOperation} · {FormatDuration(DateTimeOffset.UtcNow - startedAt)}";
    }

    private static string FormatTimestamp(DateTimeOffset? timestamp)
        => timestamp is { } value
            ? value.ToLocalTime().ToString("g")
            : "never";

    private static string FormatTimestamp(DateTimeOffset timestamp)
        => timestamp == DateTimeOffset.MinValue
            ? "never"
            : timestamp.ToLocalTime().ToString("g");

    private static string FormatDuration(TimeSpan duration)
    {
        if (duration < TimeSpan.Zero)
            duration = TimeSpan.Zero;

        return duration.TotalMinutes >= 1
            ? $"{(int)duration.TotalMinutes}m {duration.Seconds:D2}s"
            : $"{duration.TotalSeconds:0.0}s";
    }

    private bool SafeRefreshContext(string failurePrefix)
    {
        try
        {
            RefreshContext();
            return true;
        }
        catch (Exception ex)
        {
            _contextBox.Text = $"{failurePrefix}:\n{ex.Message}";
            _statusText.Text = failurePrefix + ".";
            return false;
        }
    }

    private void OnInputKeyDown(object? sender, KeyEventArgs e)
    {
        if ((e.Key == Key.Enter || e.Key == Key.Return) &&
            !e.KeyModifiers.HasFlag(KeyModifiers.Shift))
        {
            e.Handled = true;
            Submit();
        }
    }

    private async void Submit()
    {
        string prompt = _inputBox.Text?.Trim() ?? string.Empty;
        if (string.IsNullOrWhiteSpace(prompt))
            return;

        _inputBox.Text = string.Empty;
        GameAgentContextSnapshot context;
        try
        {
            context = _getContext();
            _contextBox.Text = BuildContextText(context);
            _statusText.Text = $"Watching {context.GameName}; {context.RecentEvents.Count} recent event(s) loaded.";
        }
        catch (Exception ex)
        {
            AppendConversation("Agent", $"Could not load the live game context:\n{ex.Message}");
            return;
        }

        AppendConversation("You", prompt);
        if (TryHandleTeachCommand(prompt, context))
            return;
        if (TryParseActionRequest(prompt, out GameAgentActionRequest? actionRequest))
        {
            await RunActionRequestAsync(actionRequest!);
            return;
        }

        try
        {
            IGameAgentModel model = BuildActiveModel();
            DateTimeOffset requestStartedAt = DateTimeOffset.UtcNow;
            _modelRequestStartedAt = requestStartedAt;
            _lastModelRequestDuration = null;
            _activeOperation = $"LLM request · {GameAgentProviders.Find(GetSelectedProviderId()).Label}";
            _activeOperationStartedAt = requestStartedAt;
            RefreshMonitorPanel();
            _statusText.Text = $"Asking {GameAgentProviders.Find(GetSelectedProviderId()).Label}...";
            GameAgentModelReply reply = await model.AskAsync(new GameAgentModelRequest
            {
                Prompt = prompt,
                Context = context,
                MaxContextCharacters = _preferences.GameAgentContextLimitCharacters,
                ScriptCatalogDirectory = _preferences.ScriptsDirectory,
            }, CancellationToken.None);
            _modelRequestCount++;
            _lastModelRequestAt = requestStartedAt;
            _lastModelRequestDuration = DateTimeOffset.UtcNow - requestStartedAt;
            _lastModelStatus = reply.Status;
            if (reply.UsedExternalModel == false &&
                !string.Equals(GetSelectedProviderId(), "local", StringComparison.OrdinalIgnoreCase) &&
                string.Equals(reply.Status, "local-observer fallback", StringComparison.OrdinalIgnoreCase))
            {
                _modelFallbackCount++;
            }
            if (reply.Usage is { } usage)
            {
                _usageReportCount++;
                if (usage.InputTokens is { } inputTokens)
                {
                    _inputTokenTotal += inputTokens;
                    _hasInputTokenTotal = true;
                }
                if (usage.OutputTokens is { } outputTokens)
                {
                    _outputTokenTotal += outputTokens;
                    _hasOutputTokenTotal = true;
                }
                if (usage.ReasoningTokens is { } reasoningTokens)
                {
                    _reasoningTokenTotal += reasoningTokens;
                    _hasReasoningTokenTotal = true;
                }
            }
            if (GameAgentToolRegistry.TryParseModelAction(reply.Content, out GameAgentToolAction? toolAction, out string actionError))
            {
                if (!IsExplicitActionRequest(prompt))
                {
                    AppendConversation("Agent", $"The model proposed {toolAction!.Kind}, but I did not execute it because your message did not explicitly request an action.\n{reply.Content}");
                    _statusText.Text = "Action not executed.";
                    return;
                }

                _activeOperation = $"Executing {toolAction!.Kind}";
                RefreshMonitorPanel();
                await ExecuteModelToolActionAsync(toolAction!, context);
                return;
            }

            if (!string.IsNullOrWhiteSpace(actionError))
            {
                AppendConversation("Agent", $"The model returned an invalid tool request; no action was taken.\n{actionError}");
                _statusText.Text = "Invalid tool request.";
                return;
            }

            AppendConversation("Agent", reply.Content);
            _statusText.Text = reply.UsedExternalModel ? reply.Status : "Local observer model. Action requests execute from chat.";
        }
        catch (Exception ex)
        {
            _modelRequestFailures++;
            _lastModelRequestAt = DateTimeOffset.UtcNow;
            _lastModelStatus = $"Request failed: {ex.Message}";
            AppendConversation("Agent", $"{GameAgentProviders.Find(GetSelectedProviderId()).Label} request failed: {ex.Message}");
        }
        finally
        {
            if (_modelRequestStartedAt is { } modelStartedAt)
            {
                _lastModelRequestDuration ??= DateTimeOffset.UtcNow - modelStartedAt;
            }
            _modelRequestStartedAt = null;
            _activeOperation = "Idle";
            _activeOperationStartedAt = null;
            RefreshMonitorPanel();
        }
    }

    private async Task ExecuteModelToolActionAsync(GameAgentToolAction action, GameAgentContextSnapshot context)
    {
        MtcRpcActionResult result;
        switch (action.Kind)
        {
            case GameAgentToolActionKind.RunScript:
                if (!context.Connected)
                {
                    AppendConversation("Agent", "Script not started: no active game connection.");
                    _statusText.Text = "Script start blocked.";
                    return;
                }

                if (!GameAgentToolRegistry.TryNormalizeApprovedScript(action.ScriptReference, out string approvedScript))
                {
                    AppendConversation("Agent", "Script not started: this script is not in the reviewed Game Agent capability list.");
                    _statusText.Text = "Script start blocked.";
                    return;
                }

                bool compatiblePrompt = string.Equals(context.CurrentPrompt, "Command", StringComparison.OrdinalIgnoreCase) ||
                    (string.Equals(approvedScript, "ship-upgrades/ls-dock-shopper-v33.ts", StringComparison.OrdinalIgnoreCase) &&
                     string.Equals(context.CurrentPrompt, "Citadel", StringComparison.OrdinalIgnoreCase));
                if (!compatiblePrompt)
                {
                    AppendConversation("Agent", $"Script not started: {approvedScript} requires a compatible TradeWars Command prompt{(approvedScript.StartsWith("ship-upgrades/", StringComparison.OrdinalIgnoreCase) ? " or Citadel prompt" : string.Empty)}; current prompt is {Display(context.CurrentPrompt)}.");
                    _statusText.Text = "Script start blocked.";
                    return;
                }

                if (context.RunningScripts.Any(script =>
                        !script.IsSystemScript &&
                        GameAgentToolRegistry.IsSameApprovedScript(script.Reference, approvedScript)))
                {
                    AppendConversation("Agent", $"Script not started: {approvedScript} is already running.");
                    _statusText.Text = "Script already running.";
                    return;
                }

                result = await _runScriptAsync(approvedScript);
                AppendToolActionResult("run_script", result);
                return;

            case GameAgentToolActionKind.StopScript:
                GameAgentRunningScriptSnapshot? runningScript = context.RunningScripts
                    .FirstOrDefault(script => script.Id == action.ScriptId);
                if (runningScript == null ||
                    runningScript.IsSystemScript ||
                    !GameAgentToolRegistry.IsApprovedRunningScriptReference(runningScript.Reference))
                {
                    AppendConversation("Agent", $"Script not stopped: ID {action.ScriptId} is not a running, non-system reviewed script.");
                    _statusText.Text = "Script stop blocked.";
                    return;
                }

                result = await _stopScriptAsync(action.ScriptId, null);
                AppendToolActionResult("stop_script", result);
                return;

            case GameAgentToolActionKind.RunMombotCommand:
                if (!context.Bot.NativeMombotRunning || !context.Bot.AcceptsSelfCommands)
                {
                    AppendConversation("Agent", "Mombot command not sent: native Mombot is not active and accepting local/self commands.");
                    _statusText.Text = "Mombot action blocked.";
                    return;
                }

                if (!GameAgentToolRegistry.IsAllowedMombotCommand(action.Command, out string normalizedCommand))
                {
                    AppendConversation("Agent", "Mombot command not sent: it is not in the local, non-combat command allowlist.");
                    _statusText.Text = "Mombot action blocked.";
                    return;
                }

                result = await _executeMombotCommandAsync(normalizedCommand);
                AppendToolActionResult("run_mombot_command", result);
                return;

            case GameAgentToolActionKind.RespondToPrompt:
                GameScriptInputPrompt? expectedPrompt = context.ScriptInputPrompt;
                if (!context.Connected ||
                    expectedPrompt == null ||
                    !string.Equals(expectedPrompt.ScriptName, action.ScriptName, StringComparison.OrdinalIgnoreCase) ||
                    !string.Equals(expectedPrompt.Text, action.ExpectedPrompt, StringComparison.Ordinal))
                {
                    AppendConversation("Agent", "Prompt response not sent: the exact script prompt is not present in the current live context.");
                    _statusText.Text = "Prompt response blocked.";
                    return;
                }

                GameAgentContextSnapshot currentContext = _getContext();
                GameScriptInputPrompt? currentPrompt = currentContext.ScriptInputPrompt;
                bool promptUnchanged = currentPrompt != null &&
                    currentPrompt.ObservedAt == expectedPrompt.ObservedAt &&
                    currentPrompt.SingleKey == expectedPrompt.SingleKey &&
                    string.Equals(currentPrompt.ScriptName, expectedPrompt.ScriptName, StringComparison.Ordinal) &&
                    string.Equals(currentPrompt.Text, expectedPrompt.Text, StringComparison.Ordinal);
                bool sourceScriptStillRunning = currentContext.RunningScripts.Any(script =>
                    !script.IsSystemScript &&
                    string.Equals(
                        System.IO.Path.GetFileName(script.Reference),
                        System.IO.Path.GetFileName(expectedPrompt.ScriptName),
                        StringComparison.OrdinalIgnoreCase));
                if (!promptUnchanged || currentPrompt is null || !sourceScriptStillRunning ||
                    !ScriptInputPromptPolicy.IsSafeResponse(currentPrompt, action.Command))
                {
                    AppendConversation("Agent", "Prompt response not sent: the prompt changed, its script stopped, or the response is not permitted by the observed choices and numeric limits.");
                    _statusText.Text = "Prompt response blocked.";
                    return;
                }

                result = await _sendCommandAsync(action.Command, !currentPrompt.SingleKey);
                AppendToolActionResult("respond_to_prompt", result);
                return;

            default:
                AppendConversation("Agent", "No action taken: the requested tool is not supported.");
                _statusText.Text = "Unsupported tool.";
                return;
        }
    }

    private void AppendToolActionResult(string toolName, MtcRpcActionResult result)
    {
        AppendConversation(
            "Agent",
            $"{toolName}: {(result.Success ? "completed" : "failed")}.\n{result.Message}");
        _statusText.Text = result.Success ? "Tool action complete." : "Tool action failed.";
    }

    private static bool IsExplicitActionRequest(string prompt)
    {
        string lower = prompt.ToLowerInvariant();
        return !LooksAdvisory(lower) &&
               Regex.IsMatch(
                   lower,
                   @"\b(start|run|execute|stop|halt|answer|respond|proceed|continue|set|configure|change|trade|earn|buy|sell|use)\b",
                   RegexOptions.CultureInvariant);
    }

    private IGameAgentModel BuildActiveModel()
    {
        SavePreferences();
        string provider = GetSelectedProviderId();
        if (provider == "local")
            return _localModel;

        return GameAgentProviders.BuildModel(BuildProviderConfig(provider), _localModel);
    }

    private async Task RefreshLmStudioModelsAsync()
    {
        string provider = GetSelectedProviderId();
        if (provider == "local")
            return;

        string selected = _modelCombo.SelectedItem?.ToString() ?? string.Empty;
        try
        {
            _statusText.Text = $"Loading {GameAgentProviders.Find(provider).Label} models...";
            IReadOnlyList<string> models = await GameAgentProviders.GetAvailableModelsAsync(
                BuildProviderConfig(provider),
                CancellationToken.None);
            if (models.Count == 0)
            {
                _statusText.Text = $"{GameAgentProviders.Find(provider).Label} returned no models.";
                return;
            }

            _modelCombo.ItemsSource = models;
            _modelCombo.SelectedItem = models.Contains(selected, StringComparer.OrdinalIgnoreCase)
                ? models.First(model => string.Equals(model, selected, StringComparison.OrdinalIgnoreCase))
                : models[0];
            SavePreferences();
            _statusText.Text = $"Loaded {models.Count} {GameAgentProviders.Find(provider).Label} model(s).";
        }
        catch (Exception ex)
        {
            _statusText.Text = $"{GameAgentProviders.Find(provider).Label} model list unavailable: {ex.Message}";
        }
    }

    private string GetSelectedProviderId()
        => _integrationCombo.SelectedItem is IntegrationChoice choice
            ? AppPreferences.NormalizeGameAgentProvider(choice.Id)
            : "lmstudio";

    private void UpdateIntegrationControls()
    {
        string provider = GetSelectedProviderId();
        _modelCombo.IsEnabled = provider != "local";
        string saved = ResolveInitialProviderModel(_preferences, provider);
        _modelCombo.ItemsSource = string.IsNullOrWhiteSpace(saved) ? [] : new[] { saved };
        _modelCombo.SelectedIndex = string.IsNullOrWhiteSpace(saved) ? -1 : 0;
        SavePreferences();
        _statusText.Text = $"{GameAgentProviders.Find(provider).Label} selected. Action requests will execute in the game stream.";
        RefreshMonitorPanel();
        if (provider != "local")
            _ = RefreshLmStudioModelsAsync();
    }

    private void SavePreferences()
    {
        try
        {
            string provider = GetSelectedProviderId();
            _preferences.GameAgentProvider = provider;

            string model = _modelCombo.SelectedItem?.ToString()?.Trim() ?? string.Empty;
            if (!string.IsNullOrWhiteSpace(model))
                _preferences.GameAgentProviderModels[provider] = model;

            _preferences.Save();
        }
        catch
        {
            // Best-effort preference persistence.
        }
    }

    private GameAgentProviderConfig BuildProviderConfig(string provider)
    {
        provider = AppPreferences.NormalizeGameAgentProvider(provider);
        return new GameAgentProviderConfig
        {
            Provider = provider,
            Model = _modelCombo.SelectedItem?.ToString() ?? ResolveInitialProviderModel(_preferences, provider),
            Port = provider switch
            {
                "ollama" => _preferences.GameAgentOllamaPort,
                "lmstudio" => _preferences.GameAgentLmStudioPort,
                _ => 0,
            },
            ApiKey = provider switch
            {
                "openai" => _preferences.GameAgentOpenAiApiKey,
                "anthropic" => _preferences.GameAgentAnthropicApiKey,
                _ => string.Empty,
            },
        };
    }

    private static string ResolveInitialProviderModel(AppPreferences preferences, string provider)
    {
        string normalizedProvider = AppPreferences.NormalizeGameAgentProvider(provider);
        if (preferences.GameAgentProviderModels.TryGetValue(normalizedProvider, out string? saved) &&
            !string.IsNullOrWhiteSpace(saved))
        {
            return saved.Trim();
        }

        string? env = provider == "lmstudio"
            ? Environment.GetEnvironmentVariable("MTC_GAME_AGENT_LMSTUDIO_MODEL")
            : null;
        if (!string.IsNullOrWhiteSpace(env))
            return env;

        return provider switch
        {
            "ollama" => GameAgentProviders.DefaultOllamaModel,
            "openai" => "gpt-4o-mini",
            "anthropic" => "claude-sonnet-4-5",
            "local" => "local-observer",
            _ => "local-model",
        };
    }

    private static IntegrationChoice FindIntegrationChoice(string provider)
        => BuildIntegrationChoices()
            .FirstOrDefault(choice => string.Equals(choice.Id, provider, StringComparison.OrdinalIgnoreCase))
           ?? new IntegrationChoice("lmstudio", "LM Studio");

    private static IReadOnlyList<IntegrationChoice> BuildIntegrationChoices()
        => GameAgentProviders.Choices
            .Select(choice => new IntegrationChoice(choice.Id, choice.Label))
            .ToArray();

    private sealed class IntegrationChoice
    {
        public IntegrationChoice(string id, string label)
        {
            Id = id;
            Label = label;
        }

        public string Id { get; }
        public string Label { get; }

        public override string ToString()
            => Label;

        public override bool Equals(object? obj)
            => obj is IntegrationChoice other &&
               string.Equals(Id, other.Id, StringComparison.OrdinalIgnoreCase);

        public override int GetHashCode()
            => StringComparer.OrdinalIgnoreCase.GetHashCode(Id);
    }

    private enum GameAgentActionRequestKind
    {
        MoveToSector,
        FindEnemyShip,
        HoloScan,
        DensityScan,
        QuickStats,
        Redisplay,
        Port,
        Land,
        RawCommand,
    }

    private sealed class GameAgentActionRequest
    {
        public GameAgentActionRequestKind Kind { get; init; }
        public int TargetSector { get; set; }
        public string BotMovementCommand { get; init; } = string.Empty;
        public string RawCommand { get; init; } = string.Empty;
        public string Description { get; init; } = string.Empty;
        public int MaxSteps { get; init; } = 12;
    }

    private sealed class GameAgentRequestStep
    {
        public string Command { get; init; } = string.Empty;
        public bool AppendEnter { get; init; }
        public string Message { get; init; } = string.Empty;
        public bool UseMombot { get; init; }
        public bool Complete { get; init; }
        public bool Blocked { get; init; }
        public int DelayMs { get; init; } = 850;

        public static GameAgentRequestStep Done(string message)
            => new()
            {
                Complete = true,
                Message = message,
            };

        public static GameAgentRequestStep Block(string message)
            => new()
            {
                Blocked = true,
                Message = message,
            };
    }

    private void SaveSnapshot()
    {
        try
        {
            GameAgentContextSnapshot context = _getContext();
            string path = GameAgentRuntime.ExportSnapshot(context);
            _statusText.Text = $"Snapshot saved: {path}";
            AppendConversation("Agent", $"Saved current observer snapshot:\n{path}");
            SafeRefreshContext("Could not refresh the live game context");
        }
        catch (Exception ex)
        {
            _statusText.Text = "Snapshot failed.";
            AppendConversation("Agent", $"Could not save the observer snapshot:\n{ex.Message}");
        }
    }

    private void ExportTrainingSample()
    {
        try
        {
            GameAgentContextSnapshot context = _getContext();
            string path = GameAgentRuntime.ExportTrainingSample(context);
            _statusText.Text = $"Training sample exported: {path}";
            AppendConversation("Agent", $"Exported offline training sample:\n{path}");
            SafeRefreshContext("Could not refresh the live game context");
        }
        catch (Exception ex)
        {
            _statusText.Text = "Training sample export failed.";
            AppendConversation("Agent", $"Could not export the training sample:\n{ex.Message}");
        }
    }

    private async Task RunActionRequestAsync(GameAgentActionRequest request)
    {
        if (_automationCts != null)
        {
            AppendConversation("Agent", "A request is already running. Press Stop first.");
            return;
        }

        _automationCts = new CancellationTokenSource();
        SetAutomationControls(running: true);
        _activeOperation = "Gameplay request";
        _activeOperationStartedAt = DateTimeOffset.UtcNow;
        RefreshMonitorPanel();
        CancellationToken token = _automationCts.Token;
        try
        {
            AppendConversation("Agent", $"Executing request: {request.Description}");
            for (int step = 1; step <= request.MaxSteps; step++)
            {
                token.ThrowIfCancellationRequested();
                GameAgentContextSnapshot context = _getContext();
                _contextBox.Text = BuildContextText(context);

                GameAgentRequestStep next = PlanNextRequestStep(request, context, step);
                if (next.Complete)
                {
                    AppendConversation("Agent", next.Message);
                    _statusText.Text = "Request complete.";
                    return;
                }
                if (next.Blocked)
                {
                    AppendConversation("Agent", next.Message + "\nType `teach <command> <note>` if you want this case captured as a correction.");
                    _statusText.Text = "Request stopped.";
                    return;
                }

                _statusText.Text = $"Request step {step}: sending {DisplayAction(next.Command)}.";
                MtcRpcActionResult result = next.UseMombot
                    ? await _executeMombotCommandAsync(next.Command)
                    : await _sendCommandAsync(next.Command, next.AppendEnter);
                token.ThrowIfCancellationRequested();
                if (!result.Success)
                {
                    AppendConversation("Agent", $"Request send failed for {DisplayAction(next.Command)}:\n{result.Message}");
                    _statusText.Text = "Request stopped: send failed.";
                    return;
                }

                AppendConversation("Agent", $"Sent {DisplayAction(next.Command)}. {next.Message}");
                await Task.Delay(next.DelayMs, token);
            }

            AppendConversation("Agent", $"Request stopped after {request.MaxSteps} steps. Type `teach <command> <note>` if the next command is obvious from here.");
            _statusText.Text = "Request step limit reached.";
        }
        catch (OperationCanceledException)
        {
            AppendConversation("Agent", "Request stopped by user.");
            _statusText.Text = "Request stopped.";
        }
        catch (Exception ex)
        {
            AppendConversation("Agent", $"Request failed:\n{ex.Message}");
            _statusText.Text = "Request failed.";
        }
        finally
        {
            _automationCts?.Dispose();
            _automationCts = null;
            SetAutomationControls(running: false);
            _activeOperation = "Idle";
            _activeOperationStartedAt = null;
            SafeRefreshContext("Could not refresh the live game context");
            RefreshMonitorPanel();
        }
    }

    private async Task RunCopilotSuggestionAsync(int stepLimit)
    {
        if (_automationCts != null)
            return;

        _automationCts = new CancellationTokenSource();
        SetAutomationControls(running: true);
        _activeOperation = "Copilot action loop";
        _activeOperationStartedAt = DateTimeOffset.UtcNow;
        RefreshMonitorPanel();
        CancellationToken token = _automationCts.Token;
        try
        {
            AppendConversation("Agent", $"Running deterministic copilot suggestion: up to {stepLimit} request step(s).");
            for (int step = 1; step <= stepLimit; step++)
            {
                token.ThrowIfCancellationRequested();
                bool continued = await ExecuteAutomationStepAsync(step, token);
                if (!continued)
                    break;

                if (step < stepLimit)
                    await Task.Delay(850, token);
            }
        }
        catch (OperationCanceledException)
        {
            AppendConversation("Agent", "Request stopped by user.");
            _statusText.Text = "Request stopped.";
        }
        finally
        {
            _automationCts?.Dispose();
            _automationCts = null;
            SetAutomationControls(running: false);
            _activeOperation = "Idle";
            _activeOperationStartedAt = null;
            SafeRefreshContext("Could not refresh the live game context");
            RefreshMonitorPanel();
        }
    }

    private async Task<bool> ExecuteAutomationStepAsync(int step, CancellationToken token)
    {
        GameAgentContextSnapshot context;
        try
        {
            context = _getContext();
            _contextBox.Text = BuildContextText(context);
        }
        catch (Exception ex)
        {
            AppendConversation("Agent", $"Request runner stopped: could not load context:\n{ex.Message}");
            _statusText.Text = "Request stopped: context unavailable.";
            return false;
        }

        GameAgentCopilotRecommendation recommendation = context.CopilotRecommendation;
        GameAgentCopilotValidation automationValidation = GameAgentCopilot.ValidateForAutomationLab(recommendation);
        if (!automationValidation.AutoSendAllowed)
        {
            AppendConversation("Agent", BuildAutomationBlockedReply(context, automationValidation));
            _statusText.Text = "Request stopped: action blocked.";
            return false;
        }

        string action = recommendation.Proposal.Action.Trim();
        bool appendEnter = ShouldAppendEnterForAutomation(action);
        _statusText.Text = $"Request step {step}: sending {DisplayAction(action)} at {recommendation.Prompt}.";
        MtcRpcActionResult result = await _sendCommandAsync(action, appendEnter);
        token.ThrowIfCancellationRequested();

        if (!result.Success)
        {
            AppendConversation("Agent", $"Request runner send failed for {DisplayAction(action)}:\n{result.Message}");
            _statusText.Text = "Request stopped: send failed.";
            return false;
        }

        AppendConversation(
            "Agent",
            $"Request step {step} sent {DisplayAction(action)} at {recommendation.Prompt}.\nReason: {recommendation.Proposal.Reason}");
        await Task.Delay(350, token);
        SafeRefreshContext("Could not refresh the live game context");
        return true;
    }

    private void StopAutomation()
    {
        _automationCts?.Cancel();
    }

    private void SetAutomationControls(bool running)
    {
        _autoStepButton.IsEnabled = !running;
        _autoRunButton.IsEnabled = !running;
        _stopAutomationButton.IsEnabled = running;
    }

    private bool TryHandleTeachCommand(string prompt, GameAgentContextSnapshot context)
    {
        string trimmed = prompt.Trim();
        if (!trimmed.StartsWith("teach ", StringComparison.OrdinalIgnoreCase) &&
            !trimmed.StartsWith("stuck ", StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        string payload = trimmed[(trimmed.IndexOf(' ') + 1)..].Trim();
        if (string.IsNullOrWhiteSpace(payload))
        {
            AppendConversation("Agent", "Teach syntax: teach <correct command> [note]");
            return true;
        }

        string action;
        string note;
        int separator = payload.IndexOf(' ');
        if (separator < 0)
        {
            action = payload;
            note = string.Empty;
        }
        else
        {
            action = payload[..separator].Trim();
            note = payload[(separator + 1)..].Trim();
        }

        try
        {
            string path = GameAgentRuntime.ExportCorrectionSample(
                context,
                context.CopilotRecommendation,
                action,
                note);
            AppendConversation("Agent", $"Saved correction sample for action {DisplayAction(action)}:\n{path}");
            _statusText.Text = "Correction sample saved.";
            SafeRefreshContext("Could not refresh the live game context");
        }
        catch (Exception ex)
        {
            AppendConversation("Agent", $"Could not save correction sample:\n{ex.Message}");
            _statusText.Text = "Correction sample failed.";
        }

        return true;
    }

    private static string BuildAutomationBlockedReply(GameAgentContextSnapshot context, GameAgentCopilotValidation validation)
    {
        var sb = new StringBuilder();
        sb.AppendLine("Request runner stopped.");
        sb.AppendLine($"Prompt: {Display(context.CopilotRecommendation.Prompt)}");
        sb.AppendLine($"Proposed action: {DisplayAction(context.CopilotRecommendation.Proposal.Action)}");
        foreach (string reason in validation.Reasons.Take(6))
            sb.Append("- ").AppendLine(reason);
        sb.AppendLine("To turn this into training data, type: teach <correct command> <why>");
        return sb.ToString().TrimEnd();
    }

    private static bool ShouldAppendEnterForAutomation(string action)
        => action.Length > 1;

    private static string DisplayAction(string? action)
        => string.IsNullOrWhiteSpace(action) ? "-" : $"'{action.Trim()}'";

    private static bool TryParseActionRequest(string prompt, out GameAgentActionRequest? request)
    {
        request = null;
        string text = prompt.Trim();
        string lower = text.ToLowerInvariant();
        if (string.IsNullOrWhiteSpace(text) || LooksAdvisory(lower))
            return false;

        Match moveMatch = Regex.Match(
            lower,
            @"\b(?:go|move|warp|travel|fly|head|navigate)\s+(?:to\s+)?(?:sector\s+)?(?<sector>\d{1,6})\b",
            RegexOptions.CultureInvariant);
        Match botMoveMatch = Regex.Match(
            lower,
            @"\b(?<mode>twarp|t-warp|transwarp|mow|bot\s+t|bot\s+m)\b.*?(?:sector\s+)?(?<sector>\d{1,6})\b",
            RegexOptions.CultureInvariant);
        if (botMoveMatch.Success && int.TryParse(botMoveMatch.Groups["sector"].Value, out int botSector) && botSector > 0)
        {
            string mode = botMoveMatch.Groups["mode"].Value.Replace(" ", string.Empty);
            string botCommand = mode.Contains("m", StringComparison.OrdinalIgnoreCase) && !mode.Contains("trans", StringComparison.OrdinalIgnoreCase)
                ? "m"
                : "t";
            request = new GameAgentActionRequest
            {
                Kind = GameAgentActionRequestKind.MoveToSector,
                TargetSector = botSector,
                BotMovementCommand = botCommand,
                Description = botCommand == "m"
                    ? $"mow to sector {botSector} using native Mombot"
                    : $"twarp to sector {botSector} using native Mombot",
                MaxSteps = 24,
            };
            return true;
        }

        if (!moveMatch.Success)
        {
            moveMatch = Regex.Match(
                lower,
                @"\bsector\s+(?<sector>\d{1,6})\b",
                RegexOptions.CultureInvariant);
        }
        if (moveMatch.Success && int.TryParse(moveMatch.Groups["sector"].Value, out int sector) && sector > 0)
        {
            request = new GameAgentActionRequest
            {
                Kind = GameAgentActionRequestKind.MoveToSector,
                TargetSector = sector,
                Description = $"move to sector {sector}",
                MaxSteps = 24,
            };
            return true;
        }

        Match rawMatch = Regex.Match(
            text,
            @"^\s*(?:send|type|enter)\s+(?<command>.+?)\s*$",
            RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
        if (rawMatch.Success)
        {
            string command = rawMatch.Groups["command"].Value.Trim().Trim('\'', '"');
            if (!string.IsNullOrWhiteSpace(command))
            {
                request = new GameAgentActionRequest
                {
                    Kind = GameAgentActionRequestKind.RawCommand,
                    RawCommand = command,
                    Description = $"send raw command {DisplayAction(command)}",
                    MaxSteps = 1,
                };
                return true;
            }
        }

        if (ContainsActionVerb(lower) && lower.Contains("find", StringComparison.Ordinal) &&
            (lower.Contains("enemy", StringComparison.Ordinal) ||
             lower.Contains("ship", StringComparison.Ordinal) ||
             lower.Contains("trader", StringComparison.Ordinal) ||
             lower.Contains("player", StringComparison.Ordinal)))
        {
            request = new GameAgentActionRequest
            {
                Kind = GameAgentActionRequestKind.FindEnemyShip,
                Description = "find an enemy ship or visible trader contact",
                MaxSteps = 48,
            };
            return true;
        }

        if (ContainsActionVerb(lower) && lower.Contains("holo", StringComparison.Ordinal))
        {
            request = new GameAgentActionRequest
            {
                Kind = GameAgentActionRequestKind.HoloScan,
                Description = "run a holo scan",
                MaxSteps = 4,
            };
            return true;
        }

        if (ContainsActionVerb(lower) && lower.Contains("density", StringComparison.Ordinal))
        {
            request = new GameAgentActionRequest
            {
                Kind = GameAgentActionRequestKind.DensityScan,
                Description = "run a density scan",
                MaxSteps = 4,
            };
            return true;
        }

        if (ContainsActionVerb(lower) && (lower.Contains("quick", StringComparison.Ordinal) || lower.Contains("stats", StringComparison.Ordinal)))
        {
            request = new GameAgentActionRequest
            {
                Kind = GameAgentActionRequestKind.QuickStats,
                Description = "refresh quick stats",
                MaxSteps = 1,
            };
            return true;
        }

        if (ContainsActionVerb(lower) && lower.Contains("redisplay", StringComparison.Ordinal))
        {
            request = new GameAgentActionRequest
            {
                Kind = GameAgentActionRequestKind.Redisplay,
                Description = "redisplay current sector",
                MaxSteps = 1,
            };
            return true;
        }

        if (ContainsActionVerb(lower) && Regex.IsMatch(lower, @"\bport\b|\bdock\b", RegexOptions.CultureInvariant))
        {
            request = new GameAgentActionRequest
            {
                Kind = GameAgentActionRequestKind.Port,
                Description = "enter the port menu",
                MaxSteps = 3,
            };
            return true;
        }

        if (ContainsActionVerb(lower) && Regex.IsMatch(lower, @"\bland\b|\bplanet\b", RegexOptions.CultureInvariant))
        {
            request = new GameAgentActionRequest
            {
                Kind = GameAgentActionRequestKind.Land,
                Description = "try to land on a planet",
                MaxSteps = 3,
            };
            return true;
        }

        return false;
    }

    private static bool LooksAdvisory(string lower)
        => lower.Contains("tell me how", StringComparison.Ordinal) ||
           lower.Contains("how do i", StringComparison.Ordinal) ||
           lower.Contains("how should", StringComparison.Ordinal) ||
           lower.Contains("what should", StringComparison.Ordinal) ||
           lower.Contains("recommend", StringComparison.Ordinal) ||
           lower.Contains("advice", StringComparison.Ordinal) ||
           lower.Contains("explain", StringComparison.Ordinal) ||
           lower.StartsWith("how ", StringComparison.Ordinal) ||
           lower.StartsWith("what ", StringComparison.Ordinal) ||
           lower.StartsWith("why ", StringComparison.Ordinal);

    private static bool ContainsActionVerb(string lower)
        => Regex.IsMatch(lower, @"\b(go|run|do|execute|start|begin|move|scan|find|hunt|search|port|dock|land|send|type|enter|refresh|show)\b", RegexOptions.CultureInvariant);

    private static GameAgentRequestStep PlanNextRequestStep(GameAgentActionRequest request, GameAgentContextSnapshot context, int step)
    {
        string prompt = context.CopilotRecommendation.Prompt;
        string rawPrompt = context.CopilotRecommendation.PromptRaw;

        return request.Kind switch
        {
            GameAgentActionRequestKind.MoveToSector => PlanMoveToSector(request, context, prompt, rawPrompt),
            GameAgentActionRequestKind.FindEnemyShip => PlanFindEnemyShip(request, context, prompt, rawPrompt, step),
            GameAgentActionRequestKind.HoloScan => PlanScan(context, prompt, "H", step),
            GameAgentActionRequestKind.DensityScan => PlanScan(context, prompt, "D", step),
            GameAgentActionRequestKind.QuickStats => PromptCommand(prompt, "/", "Refreshing quick stats."),
            GameAgentActionRequestKind.Redisplay => PromptCommand(prompt, "D", "Redisplaying current sector."),
            GameAgentActionRequestKind.Port => PromptCommand(prompt, "P", "Entering the port menu."),
            GameAgentActionRequestKind.Land => PromptCommand(prompt, "L", "Trying to land on a planet."),
            GameAgentActionRequestKind.RawCommand => new GameAgentRequestStep
            {
                Command = request.RawCommand,
                AppendEnter = request.RawCommand.Length > 1,
                Message = "Sending exactly what you requested.",
            },
            _ => GameAgentRequestStep.Block("I do not know how to execute that request yet."),
        };
    }

    private static GameAgentRequestStep PlanMoveToSector(GameAgentActionRequest request, GameAgentContextSnapshot context, string prompt, string rawPrompt)
    {
        if (request.TargetSector > 0 && context.CurrentSector == request.TargetSector)
            return GameAgentRequestStep.Done($"Arrived in sector {request.TargetSector}.");

        if (!string.IsNullOrWhiteSpace(request.BotMovementCommand) && prompt != "SpecialConfirmation")
        {
            if (!context.Bot.NativeMombotRunning)
                return GameAgentRequestStep.Block("Native MTC Mombot is not active, so I cannot run the requested bot movement command.");
            if (!context.Bot.AcceptsSelfCommands)
                return GameAgentRequestStep.Block("Native MTC Mombot is active but not accepting local/self commands.");

            return new GameAgentRequestStep
            {
                Command = $"{request.BotMovementCommand} {request.TargetSector}",
                UseMombot = true,
                Message = request.BotMovementCommand == "m"
                    ? $"Running native Mombot mow to sector {request.TargetSector}."
                    : $"Running native Mombot twarp to sector {request.TargetSector}.",
                DelayMs = 1500,
            };
        }

        if (prompt == "Command")
        {
            return new GameAgentRequestStep
            {
                Command = request.TargetSector.ToString(),
                AppendEnter = true,
                Message = $"Requesting movement to sector {request.TargetSector}.",
                DelayMs = 1100,
            };
        }

        if (prompt == "SpecialConfirmation")
        {
            string lower = rawPrompt.ToLowerInvariant();
            if (lower.Contains("make this jump blind", StringComparison.Ordinal) ||
                lower.Contains("jump blind", StringComparison.Ordinal))
            {
                return new GameAgentRequestStep
                {
                    Command = "N",
                    Message = "Declining blind TransWarp jump to protect the ship.",
                    DelayMs = 900,
                };
            }

            if (lower.Contains("abort", StringComparison.Ordinal))
            {
                return new GameAgentRequestStep
                {
                    Command = "N",
                    Message = "Declining abort prompt to continue the requested movement.",
                    DelayMs = 900,
                };
            }

            return new GameAgentRequestStep
            {
                Command = "Y",
                Message = "Accepting movement confirmation because you requested this action.",
                DelayMs = 1200,
            };
        }

        if (prompt is "Computer" or "Planet" or "Citadel" or "Port" or "ScanChoice")
        {
            return new GameAgentRequestStep
            {
                Command = "Q",
                Message = $"Leaving {prompt} so the movement request can continue from Command.",
                DelayMs = 700,
            };
        }

        return GameAgentRequestStep.Block($"I cannot continue movement from prompt '{Display(prompt)}' yet.");
    }

    private static GameAgentRequestStep PlanFindEnemyShip(GameAgentActionRequest request, GameAgentContextSnapshot context, string prompt, string rawPrompt, int step)
    {
        if (context.CurrentSectorDetails != null && HasVisibleContact(context.CurrentSectorDetails))
        {
            return GameAgentRequestStep.Done($"Visible contact found in current sector {context.CurrentSector}: {DescribeContacts(context.CurrentSectorDetails)}");
        }

        GameAgentSectorSnapshot? contactSector = context.AdjacentSectors.FirstOrDefault(HasVisibleContact);
        if (contactSector != null)
        {
            request.TargetSector = contactSector.Number;
            return new GameAgentRequestStep
            {
                Command = contactSector.Number.ToString(),
                AppendEnter = true,
                Message = $"Moving toward visible contact in adjacent sector {contactSector.Number}: {DescribeContacts(contactSector)}",
                DelayMs = 1200,
            };
        }

        if (prompt == "Command")
        {
            string? scannerType = GameAgentScanPolicy.GetFreshScannerType(
                context,
                DateTimeOffset.UtcNow);
            bool hasHoloScanner = GameScannerType.IsHoloScanner(scannerType);
            bool hasDensityScanner = GameScannerType.IsDensityScanner(scannerType);
            GameAgentSectorSnapshot? candidate = ChooseExplorationSector(context);

            if (!hasHoloScanner)
            {
                if (!hasDensityScanner)
                {
                    return GameAgentRequestStep.Block(
                        "Enemy search is blocked until fresh ship status verifies an installed scanner.");
                }

                if (step == 1)
                    return PlanScan(context, prompt, "D", step);

                if (candidate == null)
                {
                    return GameAgentRequestStep.Done(
                        "No visible contact was found. A Holo Scanner is required to reveal object-level contacts in adjacent sectors.");
                }
            }
            else if (step <= 2 || step % 4 == 1)
            {
                return PlanScan(context, prompt, "H", step);
            }

            if (candidate == null)
                return GameAgentRequestStep.Done("No visible contact was found in the scanned sectors.");

            return new GameAgentRequestStep
            {
                Command = candidate.Number.ToString(),
                AppendEnter = true,
                Message = $"No contact is visible yet; exploring adjacent sector {candidate.Number}.",
                DelayMs = 1200,
            };
        }

        if (prompt == "ScanChoice")
            return PlanScan(context, prompt, "H", step);

        if (prompt == "SpecialConfirmation")
        {
            if (rawPrompt.Contains("make this jump blind", StringComparison.OrdinalIgnoreCase) ||
                rawPrompt.Contains("jump blind", StringComparison.OrdinalIgnoreCase))
            {
                return new GameAgentRequestStep
                {
                    Command = "N",
                    Message = "Declining blind TransWarp jump to protect the ship.",
                    DelayMs = 900,
                };
            }

            return new GameAgentRequestStep
            {
                Command = rawPrompt.Contains("abort", StringComparison.OrdinalIgnoreCase) ? "N" : "Y",
                Message = "Answering confirmation to continue the requested search.",
                DelayMs = 1000,
            };
        }

        if (prompt is "Computer" or "Planet" or "Citadel" or "Port")
        {
            return new GameAgentRequestStep
            {
                Command = "Q",
                Message = $"Leaving {prompt} so the search can continue from Command.",
                DelayMs = 700,
            };
        }

        return GameAgentRequestStep.Block($"I cannot continue the search from prompt '{Display(prompt)}' yet.");
    }

    private static GameAgentRequestStep PlanScan(
        GameAgentContextSnapshot context,
        string prompt,
        string requestedScanType,
        int step)
    {
        string? scannerType = GameAgentScanPolicy.GetFreshScannerType(
            context,
            DateTimeOffset.UtcNow);
        if (!GameAgentScanPolicy.TrySelectScanType(
                requestedScanType,
                scannerType,
                out string selectedScanType,
                out bool requiresScanChoice))
        {
            return GameAgentRequestStep.Block(
                "Scanning is blocked until fresh ship status verifies an installed Density or Holo Scanner.");
        }

        string requestedScanner = requestedScanType == "H" ? "Holo" : "Density";
        string selectedScanner = selectedScanType == "H" ? "Holo" : "Density";
        bool usingFallback = !string.Equals(
            requestedScanType,
            selectedScanType,
            StringComparison.OrdinalIgnoreCase);

        if (prompt == "Command")
        {
            if (step > 1)
                return GameAgentRequestStep.Done("Scan completed and the ship returned to the Command prompt.");

            return new GameAgentRequestStep
            {
                Command = "S",
                Message = !requiresScanChoice
                    ? usingFallback
                        ? "No Holo Scanner is verified; S will run the available Density scan directly."
                        : "Running the Density scan directly; S selects it when no Holo Scanner is installed."
                    : usingFallback
                        ? $"{requestedScanner} Scanner is not verified; opening the available {selectedScanner} scan."
                        : $"Opening {selectedScanner.ToLowerInvariant()} scan.",
                DelayMs = requiresScanChoice ? 500 : 1400,
            };
        }

        if (prompt == "ScanChoice")
        {
            if (!requiresScanChoice)
            {
                return GameAgentRequestStep.Block(
                    "The game requested a scanner choice even though fresh ship status shows no Holo Scanner; no input was sent.");
            }

            return new GameAgentRequestStep
            {
                Command = selectedScanType,
                Message = usingFallback
                    ? $"{requestedScanner} Scanner is not verified; running the available {selectedScanner.ToLowerInvariant()} scan."
                    : $"Running {selectedScanner.ToLowerInvariant()} scan.",
                DelayMs = 1400,
            };
        }

        return GameAgentRequestStep.Block($"Scan request needs Command or ScanChoice prompt; current prompt is '{Display(prompt)}'.");
    }

    private static GameAgentRequestStep PromptCommand(string prompt, string command, string message)
        => prompt == "Command"
            ? new GameAgentRequestStep { Command = command, Message = message, DelayMs = 800 }
            : GameAgentRequestStep.Block($"That request needs the Command prompt; current prompt is '{Display(prompt)}'.");

    private static bool HasVisibleContact(GameAgentSectorSnapshot sector)
        => sector.Traders.Count > 0 || sector.Ships.Count > 0;

    private static string DescribeContacts(GameAgentSectorSnapshot sector)
    {
        var contacts = new List<string>();
        contacts.AddRange(sector.Traders.Select(trader => "trader " + trader));
        contacts.AddRange(sector.Ships.Select(ship => "ship " + ship));
        return contacts.Count == 0 ? "none" : string.Join("; ", contacts.Take(6));
    }

    private static GameAgentSectorSnapshot? ChooseExplorationSector(GameAgentContextSnapshot context)
        => context.AdjacentSectors
            .Where(sector => sector.Number > 0 && sector.Number != context.CurrentSector)
            .OrderBy(sector => sector.NavHaz)
            .ThenBy(sector => string.Equals(sector.Explored, "No", StringComparison.OrdinalIgnoreCase) ? 0 : 1)
            .ThenBy(sector => sector.Number)
            .FirstOrDefault();

    private static TextBox BuildReadOnlyBox(string watermark)
        => new()
        {
            IsReadOnly = true,
            AcceptsReturn = true,
            TextWrapping = TextWrapping.Wrap,
            Watermark = watermark,
            Background = new SolidColorBrush(Color.FromRgb(0x03, 0x1d, 0x26)),
            Foreground = Brushes.Gainsboro,
            BorderBrush = new SolidColorBrush(Color.FromRgb(0x12, 0x5e, 0x70)),
            Padding = new Thickness(8),
        };

    private static T WithControlMargin<T>(T control) where T : Control
    {
        control.Margin = new Thickness(0, 0, 8, 6);
        return control;
    }

    private static Control WrapPanel(string title, Control child)
        => new Border
        {
            Background = new SolidColorBrush(Color.FromRgb(0x0b, 0x26, 0x30)),
            BorderBrush = new SolidColorBrush(Color.FromRgb(0x1b, 0x82, 0x95)),
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(10),
            Padding = new Thickness(10),
            Child = new DockPanel
            {
                Children =
                {
                    new TextBlock
                    {
                        Text = title,
                        Foreground = new SolidColorBrush(Color.FromRgb(0x00, 0xd4, 0xc9)),
                        FontSize = 16,
                        FontWeight = FontWeight.SemiBold,
                        Margin = new Thickness(0, 0, 0, 8),
                    }.WithDock(Dock.Top),
                    child,
                }
            }
        };

    private static string BuildWelcomeMessage()
        => "Agent:\nAsk for advice when you want recommendations. Give me an action request when you want me to act, for example `move to sector 1234`, `holo scan`, `go find an enemy ship`, or `send /`. Action requests execute in the game stream and can change game state. Press Stop to interrupt. Type `teach <command> <note>` when I get stuck or choose the wrong next input.\n\n";

    private void AppendConversation(string speaker, string message)
    {
        string existing = _conversationBox.Text ?? string.Empty;
        _conversationBox.Text = existing + $"{speaker}:\n{message.Trim()}\n\n";
        _conversationBox.CaretIndex = _conversationBox.Text.Length;
    }

    internal static string BuildLocalObserverReply(string prompt, GameAgentContextSnapshot context)
    {
        string normalized = prompt.Trim().ToLowerInvariant();
        if (normalized.Contains("status", StringComparison.Ordinal) ||
            normalized.Contains("context", StringComparison.Ordinal) ||
            normalized.Contains("where am i", StringComparison.Ordinal))
        {
            return BuildStatusReply(context);
        }

        if (normalized.Contains("what happened", StringComparison.Ordinal) ||
            normalized.Contains("recent", StringComparison.Ordinal) ||
            normalized.Contains("last", StringComparison.Ordinal))
        {
            return BuildRecentEventsReply(context);
        }

        if (normalized.Contains("log", StringComparison.Ordinal) ||
            normalized.Contains("train", StringComparison.Ordinal) ||
            normalized.Contains("replay", StringComparison.Ordinal))
        {
            return $"Training events are being written as JSONL here:\n{context.EventLogPath}\n\nThat file can be replayed later into a model or test harness.";
        }

        if (normalized.Contains("risk", StringComparison.Ordinal) ||
            normalized.Contains("danger", StringComparison.Ordinal) ||
            normalized.Contains("safe", StringComparison.Ordinal))
        {
            return BuildRiskReply(context);
        }

        if (normalized.Contains("recommend", StringComparison.Ordinal) ||
            normalized.Contains("copilot", StringComparison.Ordinal) ||
            normalized.Contains("next action", StringComparison.Ordinal) ||
            normalized.Contains("what should i do", StringComparison.Ordinal))
        {
            return BuildCopilotReply(context);
        }

        if (normalized.Contains("tool", StringComparison.Ordinal) ||
            normalized.Contains("command", StringComparison.Ordinal) ||
            normalized.Contains("script", StringComparison.Ordinal))
        {
            return BuildToolReply(context);
        }

        return "I can currently answer from structured MTC state and recent observed game lines. The next build step is to connect this context to a model/tool loop, then add approval-gated actions like sending commands or starting scripts.";
    }

    private static string BuildCopilotReply(GameAgentContextSnapshot context)
    {
        GameAgentCopilotRecommendation recommendation = context.CopilotRecommendation;
        GameAgentCopilotProposal proposal = recommendation.Proposal;
        if (string.IsNullOrWhiteSpace(proposal.Action))
            return $"Copilot prompt: {Display(recommendation.Prompt)}\nNo legal next action is known for this prompt.";

        var sb = new StringBuilder();
        sb.AppendLine($"Copilot prompt: {Display(recommendation.Prompt)}");
        if (recommendation.Sector > 0)
            sb.AppendLine($"Sector: {recommendation.Sector}");
        sb.AppendLine($"Recommended action: {proposal.Action}");
        sb.AppendLine($"Reason: {proposal.Reason}");
        sb.AppendLine($"Expected prompt: {Display(proposal.ExpectedPrompt)}");
        sb.AppendLine($"Risk: {proposal.Risk}   Confidence: {proposal.Confidence:0.00}");
        sb.AppendLine($"Validation: {recommendation.Validation.Status}");
        foreach (string reason in recommendation.Validation.Reasons.Take(4))
            sb.Append("- ").AppendLine(reason);
        sb.AppendLine("Say the action plainly if you want me to execute it.");
        return sb.ToString().TrimEnd();
    }

    private static string BuildStatusReply(GameAgentContextSnapshot context)
        => $"Game: {context.GameName}\n" +
           $"Connected: {(context.Connected ? "yes" : "no")}\n" +
           $"Server: {context.Host}:{context.Port}\n" +
           $"Trader: {Display(context.TraderName)}   Corp: {(context.Corp > 0 ? context.Corp.ToString() : "-")}\n" +
           $"Sector: {Display(context.CurrentSector)}   Prompt: {Display(context.CurrentPrompt)}\n" +
           $"Credits: {context.Credits:N0}   Fighters: {context.Fighters:N0}   Shields: {context.Shields:N0}\n" +
           $"Holds: {context.HoldsEmpty:N0} empty / {context.HoldsTotal:N0} total";

    private static string BuildRecentEventsReply(GameAgentContextSnapshot context)
    {
        var events = context.RecentEvents
            .Where(evt => evt.Kind is GameAgentEventKind.ServerLine or GameAgentEventKind.ServerPrompt or GameAgentEventKind.CurrentSectorChanged or GameAgentEventKind.ShipStatus)
            .TakeLast(18)
            .ToArray();

        if (events.Length == 0)
            return "I do not have recent gameplay events yet.";

        var sb = new StringBuilder();
        sb.AppendLine("Recent gameplay events:");
        foreach (GameAgentEvent evt in events)
        {
            string text = string.IsNullOrWhiteSpace(evt.PlainText)
                ? evt.Kind.ToString()
                : evt.PlainText.Trim();
            if (text.Length > 140)
                text = text[..140] + "...";
            sb.Append(evt.Timestamp.ToLocalTime().ToString("HH:mm:ss"))
              .Append(" [")
              .Append(evt.Kind)
              .Append("] ")
              .AppendLine(text);
        }

        return sb.ToString().TrimEnd();
    }

    private static string BuildRiskReply(GameAgentContextSnapshot context)
    {
        bool lowShields = context.Shields > 0 && context.Shields < 100;
        bool lowFighters = context.Fighters > 0 && context.Fighters < 100;
        bool notConnected = !context.Connected;

        if (!lowShields && !lowFighters && !notConnected)
        {
            if (context.Hazards.Count == 0)
                return "I do not see an obvious risk from the current sidebar state or compact sector snapshot. This is still a shallow check; deeper risk detection will come from event classifiers.";

            return "Local signals to consider:\n" + string.Join("\n", context.Hazards.Select(hazard => "- " + hazard));
        }

        var sb = new StringBuilder("Potential risks:\n");
        if (notConnected)
            sb.AppendLine("- The client is not connected.");
        if (lowShields)
            sb.AppendLine($"- Shields look low: {context.Shields:N0}.");
        if (lowFighters)
            sb.AppendLine($"- Fighters look low: {context.Fighters:N0}.");
        foreach (string hazard in context.Hazards.Take(8))
            sb.Append("- ").AppendLine(hazard);
        return sb.ToString().TrimEnd();
    }

    private static string BuildToolReply(GameAgentContextSnapshot context)
    {
        var sb = new StringBuilder();
        sb.AppendLine("Available Game Agent tools:");
        foreach (GameAgentToolDescriptor tool in GameAgentToolRegistry.DescribeTools())
        {
            sb.Append("- ")
              .Append(tool.Name)
              .Append(tool.CanExecuteGameCommand ? " [action]" : " [observer]")
              .Append(tool.RequiresApproval ? " [approval]" : string.Empty)
              .Append(": ")
              .AppendLine(tool.Description);
        }

        sb.AppendLine();
        sb.AppendLine(GameAgentToolRegistry.ObserveContext(context).Message);
        sb.AppendLine("Action requests typed in chat execute through the Game Agent request runner.");
        return sb.ToString().TrimEnd();
    }

    private static string BuildContextText(GameAgentContextSnapshot context)
    {
        var sb = new StringBuilder();
        sb.AppendLine(BuildStatusReply(context));
        sb.AppendLine();
        AppendBotSnapshot(sb, context);
        AppendScriptSnapshot(sb, context);
        if (context.ScriptInputPrompt is { } scriptPrompt)
        {
            sb.AppendLine("Current TWX script input prompt:");
            sb.Append("  script: ").AppendLine(scriptPrompt.ScriptName);
            sb.Append("  type: ").AppendLine(scriptPrompt.SingleKey ? "single key" : "line input");
            sb.Append("  prompt: ").AppendLine(scriptPrompt.Text);
            sb.AppendLine();
        }
        AppendOnlinePlayers(sb, context);
        AppendPromptHistory(sb, context);
        AppendHazards(sb, context);
        AppendSectorSnapshot(sb, "Current sector", context.CurrentSectorDetails);
        if (context.AdjacentSectors.Count > 0)
        {
            sb.AppendLine("Adjacent sectors:");
            foreach (GameAgentSectorSnapshot sector in context.AdjacentSectors)
                sb.Append("  ").AppendLine(FormatSectorOneLine(sector));
            sb.AppendLine();
        }
        sb.AppendLine("Agent tools:");
        foreach (GameAgentToolDescriptor tool in GameAgentToolRegistry.DescribeTools())
            sb.Append("  ").Append(tool.Name).Append(tool.CanExecuteGameCommand ? " (action)" : string.Empty).AppendLine();
        sb.AppendLine();
        sb.AppendLine("Copilot recommendation:");
        sb.Append("  ").Append(context.CopilotRecommendation.Proposal.Action)
          .Append(" at ").Append(context.CopilotRecommendation.Prompt)
          .Append(" - ").AppendLine(context.CopilotRecommendation.Proposal.Reason);
        sb.AppendLine();
        sb.AppendLine("Event log:");
        sb.AppendLine(string.IsNullOrWhiteSpace(context.EventLogPath) ? "(not created yet)" : context.EventLogPath);
        sb.AppendLine();
        sb.AppendLine("Recent events:");
        foreach (GameAgentEvent evt in context.RecentEvents.TakeLast(24))
        {
            string text = string.IsNullOrWhiteSpace(evt.PlainText) ? evt.Kind.ToString() : evt.PlainText.Trim();
            if (text.Length > 95)
                text = text[..95] + "...";
            sb.Append(evt.Timestamp.ToLocalTime().ToString("HH:mm:ss"))
              .Append(' ')
              .Append(evt.Kind)
              .Append(" | ")
              .AppendLine(text);
        }

        return sb.ToString().TrimEnd();
    }

    private static void AppendBotSnapshot(StringBuilder sb, GameAgentContextSnapshot context)
    {
        GameAgentBotSnapshot bot = context.Bot;
        if (!bot.NativeMombotRunning && string.IsNullOrWhiteSpace(bot.ExternalBotName))
            return;

        sb.AppendLine("Bot:");
        if (bot.NativeMombotRunning)
        {
            sb.Append("  Native Mombot");
            if (!string.IsNullOrWhiteSpace(bot.BotName))
                sb.Append(" ").Append(bot.BotName);
            if (!string.IsNullOrWhiteSpace(bot.Mode))
                sb.Append(" mode=").Append(bot.Mode);
            if (!string.IsNullOrWhiteSpace(bot.LastLoadedModule))
                sb.Append(" module=").Append(bot.LastLoadedModule);
            sb.Append(bot.WatcherAttached ? " watcher=attached" : " watcher=off");
            sb.AppendLine();
        }
        if (!string.IsNullOrWhiteSpace(bot.ExternalBotName))
            sb.Append("  External bot: ").AppendLine(bot.ExternalBotName);
        sb.AppendLine();
    }

    private static void AppendScriptSnapshot(StringBuilder sb, GameAgentContextSnapshot context)
    {
        if (context.RunningScripts.Count == 0)
            return;

        sb.AppendLine("Running scripts:");
        foreach (GameAgentRunningScriptSnapshot script in context.RunningScripts.Take(12))
        {
            sb.Append("  #").Append(script.Id).Append(' ').Append(script.Name);
            if (script.IsBot)
                sb.Append(" [bot]");
            if (script.IsSystemScript)
                sb.Append(" [system]");
            if (script.Paused)
                sb.Append(" [paused]");
            sb.AppendLine();
        }
        sb.AppendLine();
    }

    private static void AppendOnlinePlayers(StringBuilder sb, GameAgentContextSnapshot context)
    {
        if (context.OnlinePlayers.Count == 0)
            return;

        sb.Append("Online: ").AppendLine(string.Join(", ", context.OnlinePlayers.Take(20)));
        sb.AppendLine();
    }

    private static void AppendPromptHistory(StringBuilder sb, GameAgentContextSnapshot context)
    {
        if (context.RecentPrompts.Count == 0)
            return;

        sb.Append("Recent prompts: ").AppendLine(string.Join(" -> ", context.RecentPrompts));
        sb.AppendLine();
    }

    private static void AppendHazards(StringBuilder sb, GameAgentContextSnapshot context)
    {
        if (context.Hazards.Count == 0)
            return;

        sb.AppendLine("Local signals:");
        foreach (string hazard in context.Hazards.Take(10))
            sb.Append("  ").AppendLine(hazard);
        sb.AppendLine();
    }

    private static void AppendSectorSnapshot(StringBuilder sb, string label, GameAgentSectorSnapshot? sector)
    {
        if (sector == null)
            return;

        sb.Append(label).Append(": ").AppendLine(FormatSectorOneLine(sector));
        if (sector.Traders.Count > 0)
            sb.Append("  Traders: ").AppendLine(string.Join("; ", sector.Traders));
        if (sector.Ships.Count > 0)
            sb.Append("  Ships: ").AppendLine(string.Join("; ", sector.Ships));
        if (sector.Planets.Count > 0)
            sb.Append("  Planets: ").AppendLine(string.Join("; ", sector.Planets));
        sb.AppendLine();
    }

    private static string FormatSectorOneLine(GameAgentSectorSnapshot sector)
    {
        var details = new List<string>();
        if (!string.IsNullOrWhiteSpace(sector.Port))
            details.Add("port " + sector.Port);
        if (!string.IsNullOrWhiteSpace(sector.Fighters))
            details.Add("figs " + sector.Fighters);
        if (!string.IsNullOrWhiteSpace(sector.ArmidMines))
            details.Add("armids " + sector.ArmidMines);
        if (!string.IsNullOrWhiteSpace(sector.LimpetMines))
            details.Add("limpets " + sector.LimpetMines);
        if (sector.NavHaz > 0)
            details.Add($"haz {sector.NavHaz}%");
        if (sector.Anomaly)
            details.Add("anom");

        string suffix = details.Count > 0 ? " | " + string.Join(", ", details) : string.Empty;
        return $"{sector.Number} [{sector.Explored}] -> {string.Join(" ", sector.WarpsOut)}{suffix}";
    }

    private static string Display(string? value)
        => string.IsNullOrWhiteSpace(value) ? "-" : value.Trim();

    private static string Display(int value)
        => value > 0 ? value.ToString() : "-";
}
