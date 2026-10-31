using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Text.Json;
using LLMSurvivingProxyAgent.Core;
using LLMSurvivingProxyAgent.Runtime;
using Microsoft.Extensions.Logging.Abstractions;

namespace LLMSurvivingProxyAgent.TradeWars.Tests;

public sealed class TradeWarsTcpProxyTests
{
    [Theory]
    [InlineData("Citadel command [TL=100]", "Citadel")]
    [InlineData("Planet command [TL=100]", "Planet")]
    public void ClassifiesPlanetLocationPromptsBeforeTheGenericCommandPrompt(
        string prompt,
        string expectedSurface)
    {
        Assert.True(TradeWarsTelnetTelemetryDecoder.TryGetPromptSurface(prompt, out string surface));
        Assert.Equal(expectedSurface, surface);
    }

    [Fact]
    public async Task IgnoresNavPointPreviewSectorAndRecognizesSafeExitPrompt()
    {
        var events = new List<ProxyGameEvent>();
        var decoder = new TradeWarsTelnetTelemetryDecoder((gameEvent, _) =>
        {
            events.Add(gameEvent);
            return Task.CompletedTask;
        });

        await decoder.ObserveServerBytesAsync(Encoding.ASCII.GetBytes(
            "<Set Course to NavPoint>\r\n" +
            "Sector: 933 in Unknown Space\r\n" +
            "Choose NavPoint (?=Help) [Q] :\r\n"));
        await decoder.ObserveServerBytesAsync(Encoding.ASCII.GetBytes(
            "Current Sector: 4\r\n" +
            "Command [TL=00:00:00]:[4] (?=Help)? :"));

        Assert.DoesNotContain(
            events,
            gameEvent => gameEvent.Kind == "CurrentSectorChanged" &&
                gameEvent.CurrentSector == 933);
        Assert.Contains(
            events,
            gameEvent => gameEvent.Kind == "ServerPrompt" &&
                gameEvent.PromptSurface == "NavPointSelection");
        Assert.Contains(
            events,
            gameEvent => gameEvent.Kind == "CurrentSectorChanged" &&
                gameEvent.CurrentSector == 4);
    }

    [Fact]
    public async Task CommandBrokerRejectsSendingWithoutAnActiveProxySession()
    {
        var commandBroker = new ProxyGameCommandBroker();

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            commandBroker.SendAsync("meridian", GameCommand.RequestShipInformation));
    }

    [Fact]
    public async Task RelaysTelnetBytesUnchangedInBothDirectionsAndLogsLifecycle()
    {
        var upstreamListener = new TcpListener(IPAddress.Loopback, 0);
        upstreamListener.Start();
        var upstreamPort = ((IPEndPoint)upstreamListener.LocalEndpoint).Port;
        var proxyPort = GetAvailablePort();
        var eventDirectory = CreateTemporaryDirectory();
        var proxy = new TradeWarsTcpProxyService(
            [
                new GameProxyListenerOptions
                {
                    AgentId = "meridian",
                    ListenAddress = IPAddress.Loopback.ToString(),
                    ListenPort = proxyPort,
                    UpstreamHost = IPAddress.Loopback.ToString(),
                    UpstreamPort = upstreamPort,
                    EventLogDirectory = eventDirectory
                }
            ],
            new ProxyGameEventHub(),
            new ProxyGameCommandBroker(),
            NullLogger<TradeWarsTcpProxyService>.Instance);

        try
        {
            await proxy.StartAsync(CancellationToken.None);
            using var client = new TcpClient();
            await client.ConnectAsync(IPAddress.Loopback, proxyPort);
            using var upstream = await upstreamListener.AcceptTcpClientAsync()
                .WaitAsync(TimeSpan.FromSeconds(5));

            var clientPayload = new byte[] { 255, 251, 1, (byte)'T', (byte)'W', (byte)'X', 13 };
            await client.GetStream().WriteAsync(clientPayload);
            var relayedClientPayload = new byte[clientPayload.Length];
            await upstream.GetStream().ReadExactlyAsync(relayedClientPayload)
                .AsTask()
                .WaitAsync(TimeSpan.FromSeconds(5));
            Assert.Equal(clientPayload, relayedClientPayload);

            var serverText = Encoding.ASCII.GetBytes("raw-server-payload");
            var serverPayload = new byte[] { 255, 253, 1 }.Concat(serverText).ToArray();
            await upstream.GetStream().WriteAsync(serverPayload);
            var relayedServerPayload = new byte[serverPayload.Length];
            await client.GetStream().ReadExactlyAsync(relayedServerPayload)
                .AsTask()
                .WaitAsync(TimeSpan.FromSeconds(5));
            Assert.Equal(serverPayload, relayedServerPayload);
        }
        finally
        {
            await proxy.StopAsync(CancellationToken.None);
            upstreamListener.Stop();
        }

        try
        {
            var eventFile = Directory.GetFiles(eventDirectory, "events-*.jsonl").Single();
            var events = await File.ReadAllTextAsync(eventFile);
            Assert.Contains("\"Kind\":\"Connected\"", events, StringComparison.Ordinal);
            Assert.Contains("\"Kind\":\"Disconnected\"", events, StringComparison.Ordinal);
            Assert.DoesNotContain("TWX", events, StringComparison.Ordinal);
            Assert.DoesNotContain("raw-server-payload", events, StringComparison.Ordinal);
        }
        finally
        {
            Directory.Delete(eventDirectory, recursive: true);
        }
    }

    [Fact]
    public async Task SendsOnlyTypedAllowlistedCommandsThroughTheActiveSession()
    {
        var upstreamListener = new TcpListener(IPAddress.Loopback, 0);
        upstreamListener.Start();
        var upstreamPort = ((IPEndPoint)upstreamListener.LocalEndpoint).Port;
        var proxyPort = GetAvailablePort();
        var eventDirectory = CreateTemporaryDirectory();
        var commandBroker = new ProxyGameCommandBroker();
        var eventHub = new ProxyGameEventHub();
        var proxy = new TradeWarsTcpProxyService(
            [
                new GameProxyListenerOptions
                {
                    AgentId = "meridian",
                    ListenAddress = IPAddress.Loopback.ToString(),
                    ListenPort = proxyPort,
                    UpstreamHost = IPAddress.Loopback.ToString(),
                    UpstreamPort = upstreamPort,
                    EventLogDirectory = eventDirectory
                }
            ],
            eventHub,
            commandBroker,
            NullLogger<TradeWarsTcpProxyService>.Instance);

        try
        {
            await proxy.StartAsync(CancellationToken.None);
            using var client = new TcpClient();
            await client.ConnectAsync(IPAddress.Loopback, proxyPort);
            using var upstream = await upstreamListener.AcceptTcpClientAsync()
                .WaitAsync(TimeSpan.FromSeconds(5));
            await using var events = eventHub.ReadAllAsync().GetAsyncEnumerator();
            Assert.True(await events.MoveNextAsync().AsTask().WaitAsync(TimeSpan.FromSeconds(5)));
            Assert.Equal("Connected", events.Current.Event.Kind);
            Assert.True(commandBroker.HasActiveSession("meridian"));

            await commandBroker.SendAsync("meridian", GameCommand.RequestShipInformation);

            var received = new byte[2];
            await upstream.GetStream().ReadExactlyAsync(received)
                .AsTask()
                .WaitAsync(TimeSpan.FromSeconds(5));
            Assert.Equal(new byte[] { (byte)'I', 13 }, received);

            await commandBroker.SendAsync("meridian", GameCommand.DisplayCurrentSector);
            await upstream.GetStream().ReadExactlyAsync(received)
                .AsTask()
                .WaitAsync(TimeSpan.FromSeconds(5));
            Assert.Equal(new byte[] { (byte)'D', 13 }, received);

            await commandBroker.SendAsync(
                "meridian",
                new GameCommandRequest(GameCommand.MoveToSector, 3));
            var movement = new byte[3];
            await upstream.GetStream().ReadExactlyAsync(movement)
                .AsTask()
                .WaitAsync(TimeSpan.FromSeconds(5));
            Assert.Equal(new byte[] { (byte)'M', (byte)'3', 13 }, movement);

            await commandBroker.SendAsync(
                "meridian",
                new GameCommandRequest(GameCommand.ExitNavPointSelection));
            await upstream.GetStream().ReadExactlyAsync(received)
                .AsTask()
                .WaitAsync(TimeSpan.FromSeconds(5));
            Assert.Equal(new byte[] { (byte)'Q', 13 }, received);
        }
        finally
        {
            await proxy.StopAsync(CancellationToken.None);
            upstreamListener.Stop();
        }
        Assert.False(commandBroker.HasActiveSession("meridian"));

        try
        {
            var events = await File.ReadAllTextAsync(
                Directory.GetFiles(eventDirectory, "events-*.jsonl").Single());
            Assert.Contains("\"Kind\":\"AgentCommandSent\"", events, StringComparison.Ordinal);
            Assert.Contains("\"command\":\"RequestShipInformation\"", events, StringComparison.Ordinal);
            Assert.Contains("\"command\":\"DisplayCurrentSector\"", events, StringComparison.Ordinal);
            Assert.Contains("\"command\":\"MoveToSector\"", events, StringComparison.Ordinal);
            Assert.Contains("\"command\":\"ExitNavPointSelection\"", events, StringComparison.Ordinal);
        }
        finally
        {
            Directory.Delete(eventDirectory, recursive: true);
        }
    }

    [Fact]
    public async Task DecodesCompleteShipInformationIntoTrustedSnapshotEvents()
    {
        var directory = CreateTemporaryDirectory();
        try
        {
            await using var writer = new ProxyEventLogWriter(directory);
            await writer.WriteAsync(new ProxyGameEvent(DateTimeOffset.UtcNow, "Connected"));
            await writer.WriteAsync(new ProxyGameEvent(
                DateTimeOffset.UtcNow,
                "PresenceHeartbeat",
                Metadata: new Dictionary<string, string> { ["connected"] = "true" }));

            var decoder = new TradeWarsTelnetTelemetryDecoder(
                (gameEvent, cancellationToken) => writer.WriteAsync(gameEvent, cancellationToken));
            var serverText =
                "\xFF\xFB\x01\x1B[1;35m<Info>\x1B[0m\r\n" +
                "Trader Name : Civilian Meridian\r\n" +
                "Rank and Exp : 0 points, Alignment=0 Neutral\r\n" +
                "Ship Name : Meridian One\r\n" +
                "Ship Info : Mainstay Ltd Merchant Cruiser Ported=0 Kills=0\r\n" +
                "Turns to Warp : 3\r\n" +
                "Current Sector : 1\r\n" +
                "Turns left : 65520\r\n" +
                "Total Holds : 20 - Empty=20\r\n" +
                "Fighters : 30\r\n" +
                "Credits : 300\r\n" +
                "\x1B[35mCommand [TL=00:00:00]:[1] (?=Help)? : \x1B[0m";
            var bytes = Encoding.Latin1.GetBytes(serverText);
            foreach (var chunk in Split(bytes, 7))
            {
                await decoder.ObserveServerBytesAsync(chunk);
            }

            await writer.WriteAsync(new ProxyGameEvent(
                DateTimeOffset.UtcNow,
                "PresenceHeartbeat",
                Metadata: new Dictionary<string, string> { ["connected"] = "true" }));

            var provider = new EventLogGameSnapshotProvider(
                new Dictionary<string, string> { ["meridian"] = directory });
            var situation = await provider.CaptureSituationAsync("meridian");

            Assert.Equal(GamePresenceState.Connected, situation.Presence);
            Assert.Equal(GameAuthenticationState.Authenticated, situation.Authentication);
            Assert.Equal(GameSynchronizationState.Healthy, situation.Synchronization);
            Assert.NotNull(situation.Snapshot);
            Assert.Equal(1, situation.Snapshot.CurrentSector);
            Assert.Equal(300, situation.Snapshot.SynchronizedCredits);
            Assert.Equal(65520, situation.Snapshot.Turns);
            Assert.Equal("Meridian One", situation.Snapshot.Ship?.Name);
            Assert.Equal("Mainstay Ltd Merchant Cruiser", situation.Snapshot.Ship?.ShipType);
            Assert.Equal(string.Empty, situation.Snapshot.Ship?.Owner);
            Assert.Equal(30, situation.Snapshot.Ship?.Fighters);
            Assert.Equal(20, situation.Snapshot.HoldsTotal);
            Assert.Equal(20, situation.Snapshot.HoldsEmpty);
            Assert.Equal(0, situation.Snapshot.Experience);
            Assert.Equal(0, situation.Snapshot.Alignment);
            Assert.Contains("Corporation status", situation.UnknownFields);

            var eventLog = await File.ReadAllTextAsync(
                Directory.GetFiles(directory, "events-*.jsonl").Single());
            Assert.DoesNotContain("\"traderName\"", eventLog, StringComparison.Ordinal);
            Assert.DoesNotContain("Civilian Meridian", eventLog, StringComparison.Ordinal);
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    [Theory]
    [InlineData("None", false)]
    [InlineData("Holo", true)]
    public async Task QuickStatsScannerStatusSurvivesFinalShipInformationResponse(
        string scannerType,
        bool expectedScannerInstalled)
    {
        var directory = CreateTemporaryDirectory();
        try
        {
            await using var writer = new ProxyEventLogWriter(directory);
            await writer.WriteAsync(new ProxyGameEvent(DateTimeOffset.UtcNow, "Connected"));
            await writer.WriteAsync(new ProxyGameEvent(
                DateTimeOffset.UtcNow,
                "PresenceHeartbeat",
                Metadata: new Dictionary<string, string> { ["connected"] = "true" }));

            var decoder = new TradeWarsTelnetTelemetryDecoder(
                (gameEvent, cancellationToken) => writer.WriteAsync(gameEvent, cancellationToken));
            const string shipInformation =
                "<Info>\r\n" +
                "Trader Name : Meridian\r\n" +
                "Ship Name : Meridian One\r\n" +
                "Ship Info : Seeschlange Merchant Cruiser Ported=233 Kills=0\r\n" +
                "Current Sector : 551\r\n" +
                "Turns left : 65520\r\n" +
                "Total Holds : 20 - Empty=20\r\n" +
                "Fighters : 30\r\n" +
                "Credits : 36017\r\n" +
                "Command [TL=00:00:00]:[551] (?=Help)? : \r\n";
            string quickStats =
                "Sect 551³Turns 65520³Creds 36017³Figs 30³Shlds 0³Hlds 20³" +
                "Ore 0³Org 0³Equ 0³Col 0³LRS " + scannerType +
                "³Aln 0³Exp 0³Corp 0³Ship 3 MerCru³\r\n";

            foreach (var chunk in Split(Encoding.Latin1.GetBytes(shipInformation + quickStats + shipInformation), 7))
            {
                await decoder.ObserveServerBytesAsync(chunk);
            }

            await writer.WriteAsync(new ProxyGameEvent(
                DateTimeOffset.UtcNow,
                "PresenceHeartbeat",
                Metadata: new Dictionary<string, string> { ["connected"] = "true" }));

            var provider = new EventLogGameSnapshotProvider(
                new Dictionary<string, string> { ["meridian"] = directory });
            var situation = await provider.CaptureSituationAsync("meridian");

            Assert.Equal(GameSynchronizationState.Healthy, situation.Synchronization);
            Assert.NotNull(situation.Snapshot);
            Assert.Equal(551, situation.Snapshot.CurrentSector);
            Assert.Equal((bool?)expectedScannerInstalled, situation.Snapshot.HoloScannerInstalled);
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    [Fact]
    public async Task DecodesCargoBreakdownFromShipInformation()
    {
        var events = new List<ProxyGameEvent>();
        var decoder = new TradeWarsTelnetTelemetryDecoder((gameEvent, _) =>
        {
            events.Add(gameEvent);
            return Task.CompletedTask;
        });
        var serverText =
            "\xFF\xFB\x01\x1B[1;35m<Info>\x1B[0m\r\n" +
            "Ship Name : Meridian One\r\n" +
            "Ship Info : Mainstay Ltd Merchant Cruiser Ported=0 Kills=0\r\n" +
            "Current Sector : 41\r\n" +
            "Turns left : 65476\r\n" +
            "Total Holds : 20 - Organics=20\r\n" +
            "Fighters : 30\r\n" +
            "Credits : 730\r\n" +
            "\x1B[35mCommand [TL=00:00:00]:[1] (?=Help)? : \x1B[0m";

        await decoder.ObserveServerBytesAsync(Encoding.Latin1.GetBytes(serverText));

        ProxyGameEvent status = Assert.Single(
            events,
            gameEvent => gameEvent.Kind == "SynchronizedShipStatus");
        Assert.Equal("20", status.Metadata!["organics"]);
        Assert.Equal("0", status.Metadata["fuelOre"]);
        Assert.Equal("0", status.Metadata["equipment"]);
        Assert.Equal("0", status.Metadata["holdsEmpty"]);
        Assert.Equal("20", status.Metadata["holdsTotal"]);
    }

    [Fact]
    public async Task DoesNotEmitASynchronizedStatusForAnIncompleteShipInformationScreen()
    {
        var events = new List<ProxyGameEvent>();
        var decoder = new TradeWarsTelnetTelemetryDecoder((gameEvent, _) =>
        {
            events.Add(gameEvent);
            return Task.CompletedTask;
        });
        var partial = Encoding.ASCII.GetBytes(
            "<Info>\r\nCurrent Sector : 1\r\nCredits : 300\r\nCommand [TL=");

        await decoder.ObserveServerBytesAsync(partial);

        Assert.DoesNotContain(events, gameEvent => gameEvent.Kind == "SynchronizedShipStatus");
    }

    [Fact]
    public async Task SerializesConcurrentProxyAndAgentEventLogAppends()
    {
        var directory = CreateTemporaryDirectory();
        var path = Path.Combine(directory, $"events-{DateTime.UtcNow:yyyyMMdd}.jsonl");
        await using var proxyWriter = new ProxyEventLogWriter(directory);

        try
        {
            var writes = Enumerable.Range(0, 200).Select(index =>
            {
                string payload = new string((char)('a' + index % 26), 2048);
                if (index % 2 == 0)
                {
                    return proxyWriter.WriteAsync(new ProxyGameEvent(
                        DateTimeOffset.UtcNow,
                        "ServerLine",
                        Metadata: new Dictionary<string, string>
                        {
                            ["index"] = index.ToString(),
                            ["payload"] = payload
                        }));
                }

                string line = JsonSerializer.Serialize(new
                {
                    Timestamp = DateTimeOffset.UtcNow,
                    Kind = "ServerPrompt",
                    Index = index,
                    Payload = payload
                });
                return Task.Run(() => EventLogFileAppender.AppendLine(path, line));
            });

            await Task.WhenAll(writes);

            string[] lines = await File.ReadAllLinesAsync(path);
            Assert.Equal(200, lines.Length);
            var indexes = new HashSet<int>();
            foreach (string line in lines)
            {
                using JsonDocument document = JsonDocument.Parse(line);
                JsonElement root = document.RootElement;
                if (root.TryGetProperty("Index", out JsonElement index))
                {
                    indexes.Add(index.GetInt32());
                }
                else
                {
                    indexes.Add(int.Parse(root.GetProperty("Metadata").GetProperty("index").GetString()!));
                }
            }

            Assert.Equal(200, indexes.Count);
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    private static IEnumerable<ReadOnlyMemory<byte>> Split(byte[] bytes, int size)
    {
        for (var offset = 0; offset < bytes.Length; offset += size)
        {
            yield return bytes.AsMemory(offset, Math.Min(size, bytes.Length - offset));
        }
    }

    private static int GetAvailablePort()
    {
        var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        var port = ((IPEndPoint)listener.LocalEndpoint).Port;
        listener.Stop();
        return port;
    }

    private static string CreateTemporaryDirectory()
    {
        var directory = Path.Combine(Path.GetTempPath(), $"tw-proxy-{Guid.NewGuid():N}");
        Directory.CreateDirectory(directory);
        return directory;
    }
}
