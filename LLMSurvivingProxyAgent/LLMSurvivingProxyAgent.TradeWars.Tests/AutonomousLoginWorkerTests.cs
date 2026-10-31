using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Text.Json.Nodes;
using LLMSurvivingProxyAgent.Core;
using LLMSurvivingProxyAgent.Runtime;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace LLMSurvivingProxyAgent.TradeWars.Tests;

public sealed class AutonomousLoginWorkerTests
{
    [Fact]
    public async Task DecoderRecognizesNameAndNewCharacterPrompts()
    {
        var events = new List<ProxyGameEvent>();
        var decoder = new TradeWarsTelnetTelemetryDecoder((gameEvent, _) =>
        {
            events.Add(gameEvent);
            return Task.CompletedTask;
        });
        var promptText =
            "Please enter your name (ENTER for none):\r\n" +
            "Server registered to: test server\r\n" +
            "[Pause]\r\n" +
            "Enter your choice:\r\n" +
            "Would you like to read today's log?\r\n" +
            "Show today's log? (Y/N) [N]\r\n" +
            "Do you wish to clear some avoids? (Y/N) [N]\r\n" +
            "Would you like to start a new character in this game? (Type Y or N)\r\n" +
            "Password:\r\n" +
            "Re-enter your password:\r\n" +
            "Repeat password to verify.\r\n" +
            "Do you wish to make up a new Alias for your Trader Name?\r\n" +
            "That alias would look like this in the game\r\n" +
            "What do you want to name your ship?\r\n" +
            "What do you want to name your home planet?\r\n";

        await decoder.ObserveServerBytesAsync(Encoding.ASCII.GetBytes(promptText));

        Assert.Equal(
            [
                "BBSName",
                "ServerRegistration",
                "Pause",
                "Menu",
                "DailyLog",
                "DailyLog",
                "ClearAvoids",
                "NewCharacter",
                "Password",
                "PasswordConfirmation",
                "PasswordConfirmation",
                "TraderAlias",
                "TraderAliasPreview",
                "ShipName",
                "HomePlanetName"
            ],
            events.Select(gameEvent => gameEvent.PromptSurface));
    }

    [Fact]
    public async Task ChoosesAndPersistsIdentityBeforeNewCharacterAndPassword()
    {
        var profilePath = CreateProfileFile();
        var proxyListener = new TcpListener(IPAddress.Loopback, 0);
        proxyListener.Start();
        var options = new AutonomousLoginOptions
        {
            Enabled = true,
            AgentId = "meridian",
            SelectedGame = "default",
            Games = new Dictionary<string, AutonomousLoginGameProfile>(StringComparer.OrdinalIgnoreCase)
            {
                ["default"] = new() { GameLetter = "E" },
                ["enhanced"] = new() { GameLetter = "H", Password = "OtherGame42" }
            }
        };
        var provider = new FakeLoginIdentityProvider();
        var logger = new CapturingLogger<AutonomousLoginWorker>();
        var worker = CreateWorker(
            proxyListener,
            profilePath,
            options,
            provider,
            logger);

        try
        {
            await worker.StartAsync(CancellationToken.None);
            using var client = await proxyListener.AcceptTcpClientAsync()
                .WaitAsync(TimeSpan.FromSeconds(5));
            var stream = client.GetStream();

            await stream.WriteAsync(new byte[] { 255, 253 });
            await stream.WriteAsync(new byte[] { 246, 255, 251 });
            await stream.WriteAsync(new byte[] { 1 });
            var negotiationResponse = new byte[6];
            await stream.ReadExactlyAsync(negotiationResponse)
                .AsTask()
                .WaitAsync(TimeSpan.FromSeconds(5));
            Assert.Equal(new byte[] { 255, 252, 246, 255, 254, 1 }, negotiationResponse);

            await SendPromptAndExpectAsync(
                stream,
                "Please enter your name (ENTER for none):\r\n",
                "Meridian8\r");
            var savedBeforeGameSelection = await ReadProfileAsync(profilePath);
            Assert.Equal("Meridian8", savedBeforeGameSelection["AutonomousLogin"]!["LoginName"]!.GetValue<string>());
            Assert.Equal("StarMap42", savedBeforeGameSelection["AutonomousLogin"]!["Games"]!["default"]!["TraderAlias"]!.GetValue<string>());
            Assert.Equal(string.Empty, savedBeforeGameSelection["AutonomousLogin"]!["Games"]!["default"]!["Password"]!.GetValue<string>());
            Assert.Equal(["login-name", "trader-alias"], provider.Calls);
            await SendPromptAndExpectAsync(
                stream,
                "Server registered to: test server\r\n",
                "E");
            await SendPromptAndExpectAsync(stream, "[Pause]\r\n", "\r");
            await SendPromptAndExpectAsync(stream, "Enter your choice:\r\n", "T\r");
            await SendPromptAndExpectAsync(stream, "Show today's log? (Y/N) [N]\r\n", "\r");
            await SendPromptAndExpectAsync(stream, "[Pause]\r\n", "\r");
            await SendPromptAndExpectAsync(
                stream,
                "Would you like to start a new character in this game? (Type Y or N)\r\n",
                "Y");
            Assert.Equal(["login-name", "trader-alias", "game-password"], provider.Calls);

            var savedAfterNewCharacterOffer = await ReadProfileAsync(profilePath);
            Assert.Equal("Meridian8", savedAfterNewCharacterOffer["AutonomousLogin"]!["LoginName"]!.GetValue<string>());
            Assert.Equal("StarMap42", savedAfterNewCharacterOffer["AutonomousLogin"]!["Games"]!["default"]!["TraderAlias"]!.GetValue<string>());
            Assert.Equal("NovaPass42", savedAfterNewCharacterOffer["AutonomousLogin"]!["Games"]!["default"]!["Password"]!.GetValue<string>());
            Assert.Equal("OtherGame42", savedAfterNewCharacterOffer["AutonomousLogin"]!["Games"]!["enhanced"]!["Password"]!.GetValue<string>());

            await stream.WriteAsync(Encoding.ASCII.GetBytes("Server registered to: test server\r\n"));
            await SendPromptAndExpectAsync(stream, "Password:\r\n", "NovaPass42\r");
            await SendPromptAndExpectAsync(stream, "Repeat password to verify.\r\n", "NovaPass42\r");
            await stream.WriteAsync(Encoding.ASCII.GetBytes("Password?\r\n"));
            await Task.Delay(100);
            Assert.False(stream.DataAvailable);
            await SendPromptAndExpectAsync(
                stream,
                "Do you wish to make up a new Alias for your Trader Name?\r\n",
                "NStarMap42\r");
            await SendPromptAndExpectAsync(
                stream,
                "That alias would look like this in the game\r\n",
                "Y.\rY");
            await stream.WriteAsync(Encoding.ASCII.GetBytes("What do you want to name your ship?\r\n"));
            await Task.Delay(100);
            Assert.False(stream.DataAvailable);
            await SendPromptAndExpectAsync(
                stream,
                "What do you want to name your home planet?\r\n",
                ".\rY");
            await stream.WriteAsync(Encoding.ASCII.GetBytes(
                "Command [TL=00:00:00]:[1] (?=Help)? : "));

            await Task.Delay(100);
            Assert.False(stream.DataAvailable);
        }
        finally
        {
            await worker.StopAsync(CancellationToken.None);
            proxyListener.Stop();
            Directory.Delete(Path.GetDirectoryName(profilePath)!, recursive: true);
        }

        Assert.Equal(["login-name", "trader-alias", "game-password"], provider.Calls);
        Assert.Equal("Meridian8", provider.Contexts[2].ServerLoginName);
        Assert.Equal("StarMap42", provider.Contexts[2].TraderAlias);
        Assert.DoesNotContain(
            logger.Messages,
            message => message.Contains("Meridian8", StringComparison.Ordinal) ||
                       message.Contains("StarMap42", StringComparison.Ordinal) ||
                       message.Contains("NovaPass42", StringComparison.Ordinal));
    }

    [Fact]
    public async Task ExistingCharacterUsesSavedPasswordWithoutGeneratingIdentityOrCreatingCharacter()
    {
        var proxyListener = new TcpListener(IPAddress.Loopback, 0);
        proxyListener.Start();
        var options = CreateConfiguredOptions(
            "existing-trader",
            "KnownAlias",
            "ExistingPass42");
        var provider = new FakeLoginIdentityProvider();
        var worker = CreateWorker(
            proxyListener,
            Path.Combine(Path.GetTempPath(), "unused-login-profile.json"),
            options,
            provider,
            NullLogger<AutonomousLoginWorker>.Instance);

        try
        {
            await worker.StartAsync(CancellationToken.None);
            using var client = await proxyListener.AcceptTcpClientAsync()
                .WaitAsync(TimeSpan.FromSeconds(5));
            var stream = client.GetStream();

            await SendPromptAndExpectAsync(
                stream,
                "Please enter your name (ENTER for none):\r\n",
                "existing-trader\r");
            await SendPromptAndExpectAsync(
                stream,
                "Server registered to: test server\r\n",
                "E");
            await SendPromptAndExpectAsync(stream, "Enter your choice:\r\n", "T\r");
            await SendPromptAndExpectAsync(stream, "Password:\r\n", "ExistingPass42\r");
            await SendPromptAndExpectAsync(
                stream,
                "Do you wish to clear some avoids? (Y/N) [N]\r\n",
                "N");
            await stream.WriteAsync(Encoding.ASCII.GetBytes(
                "Command [TL=00:00:00]:[1] (?=Help)? : "));

            await Task.Delay(100);
            Assert.False(stream.DataAvailable);
        }
        finally
        {
            await worker.StopAsync(CancellationToken.None);
            proxyListener.Stop();
        }

        Assert.Empty(provider.Calls);
    }

    [Fact]
    public async Task DoesNotGeneratePasswordWhenExistingCharacterPasswordIsMissing()
    {
        var proxyListener = new TcpListener(IPAddress.Loopback, 0);
        proxyListener.Start();
        var options = CreateConfiguredOptions("existing-trader", "KnownAlias", string.Empty);
        var provider = new FakeLoginIdentityProvider();
        var worker = CreateWorker(
            proxyListener,
            Path.Combine(Path.GetTempPath(), "unused-login-profile.json"),
            options,
            provider,
            NullLogger<AutonomousLoginWorker>.Instance);

        try
        {
            await worker.StartAsync(CancellationToken.None);
            using var client = await proxyListener.AcceptTcpClientAsync()
                .WaitAsync(TimeSpan.FromSeconds(5));
            var stream = client.GetStream();
            await stream.WriteAsync(Encoding.ASCII.GetBytes(
                "Please enter your name (ENTER for none):\r\n"));
            var name = Encoding.ASCII.GetBytes("existing-trader\r");
            var actualName = new byte[name.Length];
            await stream.ReadExactlyAsync(actualName).AsTask().WaitAsync(TimeSpan.FromSeconds(5));
            Assert.Equal(name, actualName);

            await stream.WriteAsync(Encoding.ASCII.GetBytes(
                "Server registered to: test server\r\n"));
            var letter = new byte[1];
            await stream.ReadExactlyAsync(letter).AsTask().WaitAsync(TimeSpan.FromSeconds(5));
            Assert.Equal(Encoding.ASCII.GetBytes("E"), letter);
            await stream.WriteAsync(Encoding.ASCII.GetBytes("Enter your choice:\r\n"));
            var trade = new byte[2];
            await stream.ReadExactlyAsync(trade).AsTask().WaitAsync(TimeSpan.FromSeconds(5));
            Assert.Equal(Encoding.ASCII.GetBytes("T\r"), trade);

            await stream.WriteAsync(Encoding.ASCII.GetBytes("Password:\r\n"));
            var result = new byte[1];
            var read = await stream.ReadAsync(result).AsTask().WaitAsync(TimeSpan.FromSeconds(5));
            Assert.Equal(0, read);
        }
        finally
        {
            await worker.StopAsync(CancellationToken.None);
            proxyListener.Stop();
        }

        Assert.Empty(provider.Calls);
    }

    [Fact]
    public async Task StopsAfterTheServerRepeatsAnExistingCharacterPasswordPrompt()
    {
        var proxyListener = new TcpListener(IPAddress.Loopback, 0);
        proxyListener.Start();
        var options = CreateConfiguredOptions("test-trader", "KnownAlias", "local-test-password");
        var worker = CreateWorker(
            proxyListener,
            Path.Combine(Path.GetTempPath(), "unused-login-profile.json"),
            options,
            new FakeLoginIdentityProvider(),
            NullLogger<AutonomousLoginWorker>.Instance);

        try
        {
            await worker.StartAsync(CancellationToken.None);
            using var client = await proxyListener.AcceptTcpClientAsync()
                .WaitAsync(TimeSpan.FromSeconds(5));
            var stream = client.GetStream();

            await SendPromptAndExpectAsync(
                stream,
                "Please enter your name (ENTER for none):\r\n",
                "test-trader\r");
            await SendPromptAndExpectAsync(
                stream,
                "Server registered to: test server\r\n",
                "E");
            await SendPromptAndExpectAsync(stream, "Enter your choice:\r\n", "T\r");
            await SendPromptAndExpectAsync(stream, "Password:\r\n", "local-test-password\r");
            await stream.WriteAsync(Encoding.ASCII.GetBytes("Password:\r\n"));
            var result = new byte[1];
            var read = await stream.ReadAsync(result).AsTask().WaitAsync(TimeSpan.FromSeconds(5));
            Assert.Equal(0, read);
        }
        finally
        {
            await worker.StopAsync(CancellationToken.None);
            proxyListener.Stop();
        }
    }

    private static AutonomousLoginWorker CreateWorker(
        TcpListener proxyListener,
        string profilePath,
        AutonomousLoginOptions options,
        IAutonomousLoginIdentityProvider identityProvider,
        ILogger<AutonomousLoginWorker> logger)
    {
        var port = ((IPEndPoint)proxyListener.LocalEndpoint).Port;
        var listener = new GameProxyListenerOptions
        {
            AgentId = "meridian",
            ListenAddress = IPAddress.Loopback.ToString(),
            ListenPort = port,
            UpstreamHost = "192.168.4.75",
            UpstreamPort = 2002,
            EventLogDirectory = Path.GetTempPath()
        };
        return new AutonomousLoginWorker(
            listener,
            options,
            options.SelectedGame,
            options.Games[options.SelectedGame],
            "qwen3:8b",
            new Uri("http://localhost:11434/"),
            identityProvider,
            new AutonomousLoginCredentialStore(profilePath),
            new ProxyGameCommandBroker(),
            logger);
    }

    private static AutonomousLoginOptions CreateConfiguredOptions(
        string loginName,
        string traderAlias,
        string password) =>
        new()
        {
            Enabled = true,
            AgentId = "meridian",
            LoginName = loginName,
            SelectedGame = "default",
            Games = new Dictionary<string, AutonomousLoginGameProfile>(StringComparer.OrdinalIgnoreCase)
            {
                ["default"] = new()
                {
                    GameLetter = "E",
                    TraderAlias = traderAlias,
                    Password = password
                }
            }
        };

    private static string CreateProfileFile()
    {
        var directory = Path.Combine(Path.GetTempPath(), $"tw-login-{Guid.NewGuid():N}");
        Directory.CreateDirectory(directory);
        var path = Path.Combine(directory, "game-login.json");
        File.WriteAllText(
            path,
            """
            {
              "AutonomousLogin": {
                "Enabled": true,
                "AgentId": "meridian",
                "LoginName": "",
                "SelectedGame": "default",
                "Games": {
                  "default": { "GameLetter": "E", "TraderAlias": "", "Password": "" },
                  "enhanced": { "GameLetter": "H", "TraderAlias": "", "Password": "OtherGame42" }
                }
              }
            }
            """);
        return path;
    }

    private static async Task<JsonNode> ReadProfileAsync(string path) =>
        JsonNode.Parse(await File.ReadAllTextAsync(path))
        ?? throw new InvalidDataException("Test profile did not contain JSON.");

    private static async Task SendPromptAndExpectAsync(
        NetworkStream stream,
        string prompt,
        string expectedInput)
    {
        await stream.WriteAsync(Encoding.ASCII.GetBytes(prompt));
        var expected = Encoding.ASCII.GetBytes(expectedInput);
        var actual = new byte[expected.Length];
        await stream.ReadExactlyAsync(actual)
            .AsTask()
            .WaitAsync(TimeSpan.FromSeconds(5));
        Assert.Equal(expected, actual);
    }

    private sealed class FakeLoginIdentityProvider : IAutonomousLoginIdentityProvider
    {
        public List<string> Calls { get; } = [];
        public List<AutonomousLoginIdentityContext> Contexts { get; } = [];

        public Task<string> ChooseServerLoginNameAsync(
            ModelConfiguration configuration,
            AutonomousLoginIdentityContext context,
            CancellationToken cancellationToken = default)
        {
            Calls.Add("login-name");
            Contexts.Add(context);
            return Task.FromResult("Meridian8");
        }

        public Task<string> ChooseTraderAliasAsync(
            ModelConfiguration configuration,
            AutonomousLoginIdentityContext context,
            CancellationToken cancellationToken = default)
        {
            Calls.Add("trader-alias");
            Contexts.Add(context);
            return Task.FromResult("StarMap42");
        }

        public Task<string> GenerateGamePasswordAsync(
            ModelConfiguration configuration,
            AutonomousLoginIdentityContext context,
            CancellationToken cancellationToken = default)
        {
            Calls.Add("game-password");
            Contexts.Add(context);
            return Task.FromResult("NovaPass42");
        }
    }

    private sealed class CapturingLogger<T> : ILogger<T>
    {
        public List<string> Messages { get; } = [];

        public IDisposable? BeginScope<TState>(TState state)
            where TState : notnull =>
            NullScope.Instance;

        public bool IsEnabled(LogLevel logLevel) => true;

        public void Log<TState>(
            LogLevel logLevel,
            EventId eventId,
            TState state,
            Exception? exception,
            Func<TState, Exception?, string> formatter)
        {
            var message = formatter(state, exception);
            Messages.Add(exception is null ? message : $"{message}{Environment.NewLine}{exception}");
        }
    }

    private sealed class NullScope : IDisposable
    {
        public static NullScope Instance { get; } = new();

        public void Dispose()
        {
        }
    }
}
