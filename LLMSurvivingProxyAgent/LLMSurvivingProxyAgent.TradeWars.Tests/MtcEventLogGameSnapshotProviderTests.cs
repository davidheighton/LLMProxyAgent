using System.Text.Json;
using LLMSurvivingProxyAgent.Core;
using LLMSurvivingProxyAgent.Runtime;

namespace LLMSurvivingProxyAgent.TradeWars.Tests;

public sealed class EventLogGameSnapshotProviderTests
{
    [Fact]
    public async Task CapturesFreshSnapshotOnlyAfterConnectedStatusAndSectorEvents()
    {
        var directory = CreateEventLogDirectory();
        try
        {
            var observedAt = DateTimeOffset.UtcNow.AddSeconds(-1);
            WriteEvents(
                directory,
                ConnectionEvent(3, observedAt.AddSeconds(-2)),
                HeartbeatEvent(observedAt.AddMilliseconds(-1500)),
                PromptEvent("Command", observedAt.AddSeconds(-1)),
                SectorEvent(12, observedAt.AddMilliseconds(-1)),
                ShipStatusEvent(
                    12,
                    observedAt,
                    250_000,
                    400,
                    "Merchant Cruiser",
                    "Meridian",
                    scannerType: "Holo"));
            var provider = CreateProvider(directory);

            var snapshot = await provider.CaptureSynchronizedSnapshotAsync("meridian");
            var situation = await provider.CaptureSituationAsync("meridian");

            Assert.NotNull(snapshot);
            Assert.Equal(12, snapshot.CurrentSector);
            Assert.Equal(250_000, snapshot.SynchronizedCredits);
            Assert.Equal(400, snapshot.Turns);
            Assert.Equal("Merchant Cruiser", snapshot.Ship?.ShipType);
            Assert.Equal(string.Empty, snapshot.Ship?.Owner);
            Assert.Equal(18, snapshot.HoldsEmpty);
            Assert.Equal(50, snapshot.HoldsTotal);
            Assert.Equal(12, snapshot.FuelOre);
            Assert.Equal(8, snapshot.Organics);
            Assert.Equal(12, snapshot.Equipment);
            Assert.Equal(75, snapshot.Shields);
            Assert.Equal(1_234, snapshot.Experience);
            Assert.Equal(250, snapshot.Alignment);
            Assert.Equal(3, snapshot.CorporationId);
            Assert.True(snapshot.HoloScannerInstalled);
            Assert.False(snapshot.DensityScannerInstalled);
            Assert.True(snapshot.Authenticated);
            Assert.Equal("Command", snapshot.CurrentPrompt);
            Assert.Equal(observedAt, snapshot.ObservedAt);
            Assert.Equal(GamePresenceState.Connected, situation.Presence);
            Assert.Equal(GameAuthenticationState.Authenticated, situation.Authentication);
            Assert.Equal(GameSynchronizationState.Healthy, situation.Synchronization);
            Assert.Empty(situation.UnknownFields);
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    [Fact]
    public async Task CapturesRecentMenuTextOnlyAfterGameAuthentication()
    {
        var directory = CreateEventLogDirectory();
        try
        {
            DateTimeOffset observedAt = DateTimeOffset.UtcNow;
            WriteEvents(
                directory,
                ConnectionEvent(3, observedAt.AddSeconds(-5)),
                ServerLineEvent("Password:", observedAt.AddSeconds(-4)),
                HeartbeatEvent(observedAt.AddSeconds(-3)),
                PromptEvent("Command", observedAt.AddSeconds(-2)),
                ServerLineEvent("A  Cargo holds     : 20", observedAt.AddSeconds(-1)),
                ServerLineEvent("B  Fighters        : 500", observedAt.AddMilliseconds(-500)),
                SectorEvent(12, observedAt.AddMilliseconds(-100)),
                ShipStatusEvent(12, observedAt, 250_000, 400, "Merchant Cruiser", "Meridian"));
            var provider = CreateProvider(directory);

            GameSituationReport situation = await provider.CaptureSituationAsync("meridian");

            Assert.Equal(
                ["A  Cargo holds     : 20", "B  Fighters        : 500"],
                situation.RecentServerText.Select(line => line.Text));
            Assert.All(
                situation.RecentServerText,
                line => Assert.True(line.ObservedAt >= observedAt.AddSeconds(-1)));
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    [Fact]
    public async Task IdentifiesAnUninstalledHoloScannerFromFreshShipStatus()
    {
        var directory = CreateEventLogDirectory();
        try
        {
            DateTimeOffset observedAt = DateTimeOffset.UtcNow;
            WriteEvents(
                directory,
                ConnectionEvent(3, observedAt.AddSeconds(-2)),
                HeartbeatEvent(observedAt.AddSeconds(-1)),
                PromptEvent("Command", observedAt.AddMilliseconds(-500)),
                SectorEvent(551, observedAt.AddMilliseconds(-1)),
                ShipStatusEvent(
                    551,
                    observedAt,
                    25_000,
                    300,
                    "Merchant Cruiser",
                    "Meridian",
                    scannerType: "None"));
            var provider = CreateProvider(directory);

            GameSnapshot? snapshot = await provider.CaptureSynchronizedSnapshotAsync("meridian");

            Assert.NotNull(snapshot);
            Assert.False(snapshot.HoloScannerInstalled);
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    [Theory]
    [InlineData("Dens")]
    [InlineData("Density")]
    [InlineData("Density Scanner")]
    public async Task IdentifiesDensityScannerFromFreshShipStatus(string scannerType)
    {
        var directory = CreateEventLogDirectory();
        try
        {
            DateTimeOffset observedAt = DateTimeOffset.UtcNow;
            WriteEvents(
                directory,
                ConnectionEvent(3, observedAt.AddSeconds(-2)),
                HeartbeatEvent(observedAt.AddSeconds(-1)),
                PromptEvent("Command", observedAt.AddMilliseconds(-500)),
                SectorEvent(551, observedAt.AddMilliseconds(-1)),
                ShipStatusEvent(
                    551,
                    observedAt,
                    25_000,
                    300,
                    "Merchant Cruiser",
                    "Meridian",
                    scannerType: scannerType));
            var provider = CreateProvider(directory);

            GameSnapshot? snapshot = await provider.CaptureSynchronizedSnapshotAsync("meridian");

            Assert.NotNull(snapshot);
            Assert.True(snapshot.DensityScannerInstalled);
            Assert.False(snapshot.HoloScannerInstalled);
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    [Theory]
    [InlineData("None", false)]
    [InlineData("Dens", true)]
    public async Task MergesQuickStatusScannerObservationIntoFreshShipSnapshot(
        string scannerType,
        bool densityScannerInstalled)
    {
        var directory = CreateEventLogDirectory();
        try
        {
            DateTimeOffset observedAt = DateTimeOffset.UtcNow;
            WriteEvents(
                directory,
                ConnectionEvent(3, observedAt.AddSeconds(-3)),
                HeartbeatEvent(observedAt.AddSeconds(-2)),
                PromptEvent("Command", observedAt.AddSeconds(-1)),
                SectorEvent(551, observedAt.AddSeconds(-1)),
                ShipStatusEvent(
                    551,
                    observedAt.AddMilliseconds(-500),
                    36_017,
                    65_520,
                    "Seeschlange Merchant Cruiser",
                    "StarTrader"),
                PartialShipStatusEvent(
                    551,
                    observedAt,
                    36_017,
                    65_520,
                    30,
                    scannerType: scannerType));
            var provider = CreateProvider(directory);

            GameSituationReport situation = await provider.CaptureSituationAsync("meridian");

            Assert.Equal(GameSynchronizationState.Healthy, situation.Synchronization);
            Assert.NotNull(situation.Snapshot);
            Assert.Equal(551, situation.Snapshot.CurrentSector);
            Assert.Equal(36_017, situation.Snapshot.SynchronizedCredits);
            Assert.Equal(65_520, situation.Snapshot.Turns);
            Assert.Equal("Seeschlange Merchant Cruiser", situation.Snapshot.Ship?.ShipType);
            Assert.Equal(30, situation.Snapshot.Ship?.Fighters);
            Assert.False(situation.Snapshot.HoloScannerInstalled);
            Assert.Equal(densityScannerInstalled, situation.Snapshot.DensityScannerInstalled);
            Assert.Equal(observedAt, situation.Snapshot.ObservedAt);
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    [Fact]
    public async Task IdentifiesScannerAbsenceFromAnExplicitServerResponse()
    {
        var directory = CreateEventLogDirectory();
        try
        {
            DateTimeOffset observedAt = DateTimeOffset.UtcNow;
            WriteEvents(
                directory,
                ConnectionEvent(3, observedAt.AddSeconds(-2)),
                HeartbeatEvent(observedAt.AddSeconds(-1)),
                PromptEvent("Command", observedAt.AddMilliseconds(-500)),
                SectorEvent(551, observedAt.AddMilliseconds(-1)),
                ShipStatusEvent(
                    551,
                    observedAt,
                    25_000,
                    300,
                    "Merchant Cruiser",
                    "Meridian"),
                ServerLineEvent(
                    "You don't have a long range scanner.",
                    observedAt.AddMilliseconds(1)));
            var provider = CreateProvider(directory);

            GameSnapshot? snapshot = await provider.CaptureSynchronizedSnapshotAsync("meridian");

            Assert.NotNull(snapshot);
            Assert.False(snapshot.HoloScannerInstalled);
            Assert.False(snapshot.DensityScannerInstalled);
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    [Fact]
    public async Task ClassifiesRawMtcCommandPromptAsAuthenticated()
    {
        var directory = CreateEventLogDirectory();
        try
        {
            var observedAt = DateTimeOffset.UtcNow;
            WriteEvents(
                directory,
                ConnectionEvent(3, observedAt.AddSeconds(-2)),
                HeartbeatEvent(observedAt.AddSeconds(-1)),
                PromptEvent("Command [TL=20]", observedAt));
            var provider = CreateProvider(directory);

            var situation = await provider.CaptureSituationAsync("meridian");

            Assert.Equal(GamePresenceState.Connected, situation.Presence);
            Assert.Equal(GameAuthenticationState.Authenticated, situation.Authentication);
            Assert.Equal("Command", situation.CurrentPrompt);
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    [Fact]
    public async Task RefreshesCommandPromptFromRecentPresenceHeartbeat()
    {
        var directory = CreateEventLogDirectory();
        try
        {
            var commandPromptAt = DateTimeOffset.UtcNow.AddSeconds(-60);
            var heartbeatAt = DateTimeOffset.UtcNow;
            WriteEvents(
                directory,
                ConnectionEvent(3, commandPromptAt.AddSeconds(-1)),
                PromptEvent("Command", commandPromptAt),
                HeartbeatEvent(
                    heartbeatAt,
                    "Command",
                    commandPromptAt,
                    promptIsCurrent: true));
            var provider = CreateProvider(directory);

            var situation = await provider.CaptureSituationAsync("meridian");

            Assert.Equal(GamePresenceState.Connected, situation.Presence);
            Assert.Equal(GameAuthenticationState.Authenticated, situation.Authentication);
            Assert.Equal("Command", situation.CurrentPrompt);
            Assert.Equal(heartbeatAt, situation.CurrentPromptObservedAt);
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    [Fact]
    public async Task AuthenticatesFreshStopInSectorPromptFromPresenceHeartbeat()
    {
        var directory = CreateEventLogDirectory();
        try
        {
            var commandPromptAt = DateTimeOffset.UtcNow.AddSeconds(-60);
            var heartbeatAt = DateTimeOffset.UtcNow;
            WriteEvents(
                directory,
                ConnectionEvent(3, commandPromptAt.AddSeconds(-1)),
                PromptEvent("Command", commandPromptAt),
                HeartbeatEvent(heartbeatAt, "StopInSector", heartbeatAt));
            var provider = CreateProvider(directory);

            var situation = await provider.CaptureSituationAsync("meridian");

            Assert.Equal(GamePresenceState.Connected, situation.Presence);
            Assert.Equal(GameAuthenticationState.Authenticated, situation.Authentication);
            Assert.Equal("StopInSector", situation.CurrentPrompt);
            Assert.Equal(heartbeatAt, situation.CurrentPromptObservedAt);
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    [Fact]
    public async Task AuthenticatesFreshAutopilotConfirmationFromPresenceHeartbeat()
    {
        var directory = CreateEventLogDirectory();
        try
        {
            var commandPromptAt = DateTimeOffset.UtcNow.AddSeconds(-60);
            var heartbeatAt = DateTimeOffset.UtcNow;
            WriteEvents(
                directory,
                ConnectionEvent(3, commandPromptAt.AddSeconds(-1)),
                PromptEvent("Command", commandPromptAt),
                HeartbeatEvent(heartbeatAt, "AutopilotConfirmation", heartbeatAt));
            var provider = CreateProvider(directory);

            var situation = await provider.CaptureSituationAsync("meridian");

            Assert.Equal(GamePresenceState.Connected, situation.Presence);
            Assert.Equal(GameAuthenticationState.Authenticated, situation.Authentication);
            Assert.Equal("AutopilotConfirmation", situation.CurrentPrompt);
            Assert.Equal(heartbeatAt, situation.CurrentPromptObservedAt);
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    [Fact]
    public async Task PresenceHeartbeatDoesNotRefreshAnExpiredPrompt()
    {
        var directory = CreateEventLogDirectory();
        try
        {
            var promptObservedAt = DateTimeOffset.UtcNow.AddSeconds(-60);
            var heartbeatAt = DateTimeOffset.UtcNow;
            WriteEvents(
                directory,
                ConnectionEvent(3, promptObservedAt.AddSeconds(-1)),
                PromptEvent("AutopilotConfirmation", promptObservedAt),
                HeartbeatEvent(heartbeatAt, "AutopilotConfirmation", promptObservedAt));
            var provider = CreateProvider(directory);

            var situation = await provider.CaptureSituationAsync("meridian");

            Assert.Equal("AutopilotConfirmation", situation.CurrentPrompt);
            Assert.Equal(promptObservedAt, situation.CurrentPromptObservedAt);
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    [Fact]
    public async Task PresenceHeartbeatWithoutPromptObservationDoesNotRefreshAStalePrompt()
    {
        var directory = CreateEventLogDirectory();
        try
        {
            var promptObservedAt = DateTimeOffset.UtcNow.AddSeconds(-60);
            var heartbeatAt = DateTimeOffset.UtcNow;
            WriteEvents(
                directory,
                ConnectionEvent(3, promptObservedAt.AddSeconds(-1)),
                PromptEvent("Command", promptObservedAt),
                HeartbeatEvent(heartbeatAt, "Command"));
            var provider = CreateProvider(directory);

            var situation = await provider.CaptureSituationAsync("meridian");

            Assert.Equal("Command", situation.CurrentPrompt);
            Assert.Equal(promptObservedAt, situation.CurrentPromptObservedAt);
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    [Fact]
    public async Task AuthenticatesFreshPortTradeQuantityPromptFromPresenceHeartbeat()
    {
        var directory = CreateEventLogDirectory();
        try
        {
            var commandPromptAt = DateTimeOffset.UtcNow.AddSeconds(-60);
            var heartbeatAt = DateTimeOffset.UtcNow;
            WriteEvents(
                directory,
                ConnectionEvent(3, commandPromptAt.AddSeconds(-1)),
                PromptEvent("Command", commandPromptAt),
                HeartbeatEvent(heartbeatAt, "PortTradeQuantity", heartbeatAt));
            var provider = CreateProvider(directory);

            var situation = await provider.CaptureSituationAsync("meridian");

            Assert.Equal(GamePresenceState.Connected, situation.Presence);
            Assert.Equal(GameAuthenticationState.Authenticated, situation.Authentication);
            Assert.Equal("PortTradeQuantity", situation.CurrentPrompt);
            Assert.Equal(heartbeatAt, situation.CurrentPromptObservedAt);
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    [Fact]
    public async Task HeartbeatAtLoginPromptClearsPreviouslyAuthenticatedCommandState()
    {
        var directory = CreateEventLogDirectory();
        try
        {
            var observedAt = DateTimeOffset.UtcNow;
            WriteEvents(
                directory,
                ConnectionEvent(3, observedAt.AddSeconds(-3)),
                PromptEvent("Command", observedAt.AddSeconds(-2)),
                HeartbeatEvent(observedAt, "Password", observedAt));
            var provider = CreateProvider(directory);

            var situation = await provider.CaptureSituationAsync("meridian");

            Assert.Equal(GameAuthenticationState.Unverified, situation.Authentication);
            Assert.Equal("Password", situation.CurrentPrompt);
            Assert.Equal(observedAt, situation.CurrentPromptObservedAt);
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    [Fact]
    public async Task WaitsForAnInProgressTrailingEventWriteToComplete()
    {
        var directory = CreateEventLogDirectory();
        try
        {
            var observedAt = DateTimeOffset.UtcNow;
            WriteEvents(
                directory,
                ConnectionEvent(3, observedAt.AddSeconds(-2)),
                PromptEvent("Command", observedAt.AddSeconds(-1)));
            string eventLogPath = Path.Combine(directory, "events-20260101.jsonl");
            string heartbeat = HeartbeatEvent(observedAt, "Command", observedAt);
            await using var writer = new FileStream(
                eventLogPath,
                FileMode.Append,
                FileAccess.Write,
                FileShare.ReadWrite);
            await writer.WriteAsync(System.Text.Encoding.UTF8.GetBytes(heartbeat[..^1]));
            await writer.FlushAsync();
            var provider = CreateProvider(directory);

            Task<GameSituationReport> capture = provider.CaptureSituationAsync("meridian");
            await Task.Delay(TimeSpan.FromMilliseconds(40));
            await writer.WriteAsync(System.Text.Encoding.UTF8.GetBytes("}\n"));
            await writer.FlushAsync();

            var situation = await capture;

            Assert.Equal(GamePresenceState.Connected, situation.Presence);
            Assert.Equal(GameAuthenticationState.Authenticated, situation.Authentication);
            Assert.Equal("Command", situation.CurrentPrompt);
            Assert.Equal(observedAt, situation.CurrentPromptObservedAt);
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    [Fact]
    public async Task IgnoresAnUnterminatedTrailingEventWhenTheWriteDoesNotCompleteInTime()
    {
        var directory = CreateEventLogDirectory();
        try
        {
            var observedAt = DateTimeOffset.UtcNow;
            var commandPromptAt = observedAt.AddSeconds(-1);
            WriteEvents(
                directory,
                ConnectionEvent(3, observedAt.AddSeconds(-2)),
                HeartbeatEvent(observedAt.AddSeconds(-1.5)),
                PromptEvent("Command", commandPromptAt));
            string eventLogPath = Path.Combine(directory, "events-20260101.jsonl");
            string heartbeat = HeartbeatEvent(observedAt, "Password");
            await using var writer = new FileStream(
                eventLogPath,
                FileMode.Append,
                FileAccess.Write,
                FileShare.ReadWrite);
            await writer.WriteAsync(System.Text.Encoding.UTF8.GetBytes(heartbeat[..^1]));
            await writer.FlushAsync();
            var provider = CreateProvider(directory);

            var situation = await provider.CaptureSituationAsync("meridian");

            Assert.Equal(GamePresenceState.Connected, situation.Presence);
            Assert.Equal(GameAuthenticationState.Authenticated, situation.Authentication);
            Assert.Equal("Command", situation.CurrentPrompt);
            Assert.Equal(commandPromptAt, situation.CurrentPromptObservedAt);
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    [Fact]
    public async Task ReturnsUnavailableWhenDisconnectedOrSectorChangedAfterStatus()
    {
        var directory = CreateEventLogDirectory();
        try
        {
            var observedAt = DateTimeOffset.UtcNow;
            var connected = ConnectionEvent(3, observedAt.AddSeconds(-3));
            var heartbeat = HeartbeatEvent(observedAt.AddSeconds(-2.5));
            var status = ShipStatusEvent(12, observedAt.AddSeconds(-2), 100_000, 300, "Scout", "Meridian");
            var provider = CreateProvider(directory);

            WriteEvents(directory, connected, heartbeat, status, SectorEvent(13, observedAt.AddSeconds(-1)));
            Assert.Null(await provider.CaptureSynchronizedSnapshotAsync("meridian"));

            WriteEvents(directory, connected, heartbeat, status, ConnectionEvent(4, observedAt.AddSeconds(-1)));
            Assert.Null(await provider.CaptureSynchronizedSnapshotAsync("meridian"));
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    [Fact]
    public async Task ReturnsUnavailableForStaleSnapshotAndUnknownAgent()
    {
        var directory = CreateEventLogDirectory();
        try
        {
            var observedAt = DateTimeOffset.UtcNow.AddSeconds(-31);
            WriteEvents(
                directory,
                ConnectionEvent(3, observedAt.AddSeconds(-1)),
                HeartbeatEvent(DateTimeOffset.UtcNow.AddSeconds(-1)),
                ShipStatusEvent(12, observedAt, 100_000, 300, "Scout", "Meridian"));
            var provider = CreateProvider(directory);

            Assert.Null(await provider.CaptureSynchronizedSnapshotAsync("meridian"));
            Assert.Equal(
                GameSynchronizationState.Stale,
                (await provider.CaptureSituationAsync("meridian")).Synchronization);
            Assert.Null(await provider.CaptureSynchronizedSnapshotAsync("other-agent"));
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    [Fact]
    public async Task DoesNotTreatLegacyPartialShipStatusEventsAsSynchronizedSnapshots()
    {
        var directory = CreateEventLogDirectory();
        try
        {
            var observedAt = DateTimeOffset.UtcNow;
            WriteEvents(
                directory,
                ConnectionEvent(3, observedAt.AddSeconds(-2)),
                HeartbeatEvent(observedAt.AddSeconds(-1.5)),
                PromptEvent("Command", observedAt.AddSeconds(-1)),
                ShipStatusEvent(12, observedAt, 100_000, 300, "Scout", "Meridian", kind: 6));
            var provider = CreateProvider(directory);

            var situation = await provider.CaptureSituationAsync("meridian");

            Assert.Equal(GamePresenceState.Connected, situation.Presence);
            Assert.Equal(GameAuthenticationState.Authenticated, situation.Authentication);
            Assert.Equal(GameSynchronizationState.Unavailable, situation.Synchronization);
            Assert.Null(situation.Snapshot);
            Assert.Contains("Credits", situation.UnknownFields);
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    [Fact]
    public async Task ReportsOptionalFieldsAsUnknownWhenTheSourceDidNotObserveThem()
    {
        var directory = CreateEventLogDirectory();
        try
        {
            var observedAt = DateTimeOffset.UtcNow;
            WriteEvents(
                directory,
                ConnectionEvent(3, observedAt.AddSeconds(-2)),
                HeartbeatEvent(observedAt.AddSeconds(-1.5)),
                PromptEvent("Command", observedAt.AddSeconds(-1)),
                ShipStatusEvent(
                    12,
                    observedAt,
                    100_000,
                    300,
                    "Scout",
                    "Meridian",
                    includeOptionalFields: false));
            var provider = CreateProvider(directory);

            var situation = await provider.CaptureSituationAsync("meridian");

            Assert.Equal(GameSynchronizationState.Healthy, situation.Synchronization);
            Assert.NotNull(situation.Snapshot);
            Assert.Null(situation.Snapshot.HoldsEmpty);
            Assert.Null(situation.Snapshot.Experience);
            Assert.Null(situation.Snapshot.Alignment);
            Assert.Null(situation.Snapshot.CorporationId);
            Assert.Contains("Holds", situation.UnknownFields);
            Assert.Contains("Experience", situation.UnknownFields);
            Assert.Contains("Alignment", situation.UnknownFields);
            Assert.Contains("Corporation status", situation.UnknownFields);
            Assert.Contains("Holo Scanner", situation.UnknownFields);
            Assert.Contains("Density Scanner", situation.UnknownFields);
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    [Fact]
    public async Task ReportsPresenceStaleWithoutARecentHeartbeat()
    {
        var directory = CreateEventLogDirectory();
        try
        {
            var observedAt = DateTimeOffset.UtcNow;
            WriteEvents(
                directory,
                ConnectionEvent(3, observedAt.AddSeconds(-1)),
                ShipStatusEvent(12, observedAt, 100_000, 300, "Scout", "Meridian"));
            var provider = CreateProvider(directory);

            var situation = await provider.CaptureSituationAsync("meridian");

            Assert.Equal(GamePresenceState.Stale, situation.Presence);
            Assert.Equal(GameAuthenticationState.Unverified, situation.Authentication);
            Assert.Equal(GameSynchronizationState.Unavailable, situation.Synchronization);
            Assert.Null(situation.Snapshot);
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    [Fact]
    public async Task ReportsPresenceStaleWhenTheLastHeartbeatExpires()
    {
        var directory = CreateEventLogDirectory();
        try
        {
            var observedAt = DateTimeOffset.UtcNow;
            WriteEvents(
                directory,
                ConnectionEvent(3, observedAt.AddSeconds(-20)),
                HeartbeatEvent(observedAt.AddSeconds(-16)),
                PromptEvent("Command", observedAt.AddSeconds(-2)),
                ShipStatusEvent(12, observedAt.AddSeconds(-1), 100_000, 300, "Scout", "Meridian"));
            var provider = CreateProvider(directory);

            var situation = await provider.CaptureSituationAsync("meridian");

            Assert.Equal(GamePresenceState.Stale, situation.Presence);
            Assert.Equal(GameAuthenticationState.Unverified, situation.Authentication);
            Assert.Equal(GameSynchronizationState.Unavailable, situation.Synchronization);
            Assert.Null(situation.Snapshot);
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    [Fact]
    public async Task ReturnsUnavailableWhenNoEventLogExistsYet()
    {
        var directory = CreateEventLogDirectory();
        try
        {
            var provider = CreateProvider(directory);

            Assert.Null(await provider.CaptureSynchronizedSnapshotAsync("meridian"));
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    [Fact]
    public async Task RejectsMalformedEventLogInsteadOfReturningAUsableSnapshot()
    {
        var directory = CreateEventLogDirectory();
        try
        {
            WriteEvents(directory, "{\"Kind\":");
            var provider = CreateProvider(directory);

            await Assert.ThrowsAsync<InvalidDataException>(() =>
                provider.CaptureSynchronizedSnapshotAsync("meridian"));
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    private static EventLogGameSnapshotProvider CreateProvider(string directory) =>
        new(new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["meridian"] = directory
        });

    private static string CreateEventLogDirectory()
    {
        var directory = Path.Combine(Path.GetTempPath(), $"game-state-{Guid.NewGuid():N}");
        Directory.CreateDirectory(directory);
        return directory;
    }

    private static void WriteEvents(string directory, params string[] events) =>
        File.WriteAllLines(Path.Combine(directory, "events-20260101.jsonl"), events);

    private static string ConnectionEvent(int kind, DateTimeOffset timestamp) =>
        JsonSerializer.Serialize(new { Timestamp = timestamp, Kind = kind });

    private static string HeartbeatEvent(
        DateTimeOffset timestamp,
        string? promptSurface = null,
        DateTimeOffset? promptObservedAt = null,
        bool promptIsCurrent = false)
    {
        var metadata = new Dictionary<string, string>
        {
            ["connected"] = "true"
        };
        if (promptObservedAt is { } observedAt)
        {
            metadata["promptObservedAt"] = observedAt.ToString("O");
        }
        if (promptIsCurrent)
        {
            metadata["promptIsCurrent"] = "true";
        }

        return JsonSerializer.Serialize(new
        {
            Timestamp = timestamp,
            Kind = 9,
            PromptSurface = promptSurface,
            Metadata = metadata
        });
    }

    private static string SectorEvent(int sector, DateTimeOffset timestamp) =>
        JsonSerializer.Serialize(new
        {
            Timestamp = timestamp,
            Kind = 5,
            CurrentSector = sector
        });

    private static string PromptEvent(string prompt, DateTimeOffset timestamp) =>
        JsonSerializer.Serialize(new
        {
            Timestamp = timestamp,
            Kind = 1,
            PromptSurface = prompt
        });

    private static string ServerLineEvent(string text, DateTimeOffset timestamp) =>
        JsonSerializer.Serialize(new
        {
            Timestamp = timestamp,
            Kind = 0,
            PlainText = text
        });

    private static string ShipStatusEvent(
        int sector,
        DateTimeOffset timestamp,
        long credits,
        int turns,
        string shipType,
        string traderName,
        int kind = 8,
        bool includeOptionalFields = true,
        string? scannerType = null)
    {
        var metadata = new Dictionary<string, string>
        {
            ["currentSector"] = sector.ToString(),
            ["credits"] = credits.ToString(),
            ["turns"] = turns.ToString(),
            ["fighters"] = "12",
            ["shields"] = "75",
            ["shipType"] = shipType,
            ["shipName"] = "Meridian",
            ["traderName"] = traderName
        };
        if (includeOptionalFields)
        {
            metadata["holdsEmpty"] = "18";
            metadata["holdsTotal"] = "50";
            metadata["fuelOre"] = "12";
            metadata["organics"] = "8";
            metadata["equipment"] = "12";
            metadata["experience"] = "1234";
            metadata["alignment"] = "250";
            metadata["corporationId"] = "3";
        }
        if (scannerType is not null)
        {
            metadata["scannerType"] = scannerType;
        }

        return JsonSerializer.Serialize(new
        {
            Timestamp = timestamp,
            Kind = kind,
            CurrentSector = sector,
            Metadata = metadata
        });
    }

    private static string PartialShipStatusEvent(
        int sector,
        DateTimeOffset timestamp,
        long credits,
        int turns,
        int fighters,
        string scannerType) =>
        JsonSerializer.Serialize(new
        {
            Timestamp = timestamp,
            Kind = 8,
            CurrentSector = sector,
            Metadata = new Dictionary<string, string>
            {
                ["currentSector"] = sector.ToString(),
                ["credits"] = credits.ToString(),
                ["turns"] = turns.ToString(),
                ["fighters"] = fighters.ToString(),
                ["scannerType"] = scannerType
            }
        });
}
