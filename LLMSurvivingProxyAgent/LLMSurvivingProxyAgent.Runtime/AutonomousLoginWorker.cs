using System.Buffers;
using System.Net;
using System.Net.Sockets;
using System.Threading.Channels;
using LLMSurvivingProxyAgent.Core;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace LLMSurvivingProxyAgent.Runtime;

public sealed class AutonomousLoginWorker(
    GameProxyListenerOptions listener,
    AutonomousLoginOptions loginOptions,
    string selectedGame,
    AutonomousLoginGameProfile gameProfile,
    string modelName,
    Uri modelEndpoint,
    IAutonomousLoginIdentityProvider identityProvider,
    AutonomousLoginCredentialStore credentialStore,
    ProxyGameCommandBroker commandBroker,
    ILogger<AutonomousLoginWorker> logger) : BackgroundService
{
    private static readonly TimeSpan RetryDelay = TimeSpan.FromSeconds(10);
    private static readonly TimeSpan PromptTimeout = TimeSpan.FromSeconds(30);
    private const int MaximumIdentityAttempts = 3;

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        if (!IPAddress.TryParse(listener.ListenAddress, out var listenAddress) ||
            !IPAddress.IsLoopback(listenAddress))
        {
            throw new InvalidOperationException(
                $"Autonomous login for agent '{listener.AgentId}' requires a loopback proxy listener.");
        }
        if (!modelEndpoint.IsLoopback)
        {
            throw new InvalidOperationException(
                "Autonomous login identity generation requires a loopback Ollama endpoint.");
        }
        ArgumentException.ThrowIfNullOrWhiteSpace(modelName);

        while (!stoppingToken.IsCancellationRequested)
        {
            if (commandBroker.HasActiveSession(listener.AgentId))
            {
                await Task.Delay(RetryDelay, stoppingToken).ConfigureAwait(false);
                continue;
            }

            try
            {
                using var client = new TcpClient(listenAddress.AddressFamily);
                await client.ConnectAsync(listenAddress, listener.ListenPort, stoppingToken)
                    .ConfigureAwait(false);
                var result = await RunSessionAsync(client, stoppingToken).ConfigureAwait(false);
                if (result == LoginSessionResult.TerminalFailure)
                {
                    logger.LogError(
                        "Autonomous login stopped for agent {AgentId} because authentication or the expected login flow was not confirmed. Verify the configured game and server prompts before restarting.",
                        listener.AgentId);
                    return;
                }
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                return;
            }
            catch (InvalidDataException exception)
            {
                logger.LogError(
                    exception,
                    "Autonomous login stopped for agent {AgentId} because the identity or server prompt flow was invalid.",
                    listener.AgentId);
                return;
            }
            catch (HttpRequestException exception)
            {
                logger.LogError(
                    exception,
                    "Autonomous login identity generation failed for agent {AgentId}; no credentials were sent.",
                    listener.AgentId);
                return;
            }
            catch (SocketException exception)
            {
                logger.LogWarning(
                    exception,
                    "Autonomous login could not connect to the local proxy for agent {AgentId}; it will retry.",
                    listener.AgentId);
            }
            catch (IOException exception)
            {
                logger.LogWarning(
                    exception,
                    "Autonomous login connection or local credential persistence failed for agent {AgentId}; it will retry.",
                    listener.AgentId);
            }

            await Task.Delay(RetryDelay, stoppingToken).ConfigureAwait(false);
        }
    }

    private async Task<LoginSessionResult> RunSessionAsync(
        TcpClient client,
        CancellationToken stoppingToken)
    {
        client.NoDelay = true;
        var stream = client.GetStream();
        var prompts = Channel.CreateBounded<ProxyGameEvent>(new BoundedChannelOptions(16)
        {
            SingleReader = true,
            SingleWriter = true,
            FullMode = BoundedChannelFullMode.DropWrite
        });
        var sequence = new TradeWarsLoginPromptSequence(gameProfile.GameLetter);
        var modelConfiguration = new ModelConfiguration("Ollama", modelName, modelEndpoint);
        var authenticated = 0;
        var decoder = new TradeWarsTelnetTelemetryDecoder((gameEvent, cancellationToken) =>
        {
            if (gameEvent.Kind == "ServerPrompt" &&
                Volatile.Read(ref authenticated) == 0)
            {
                prompts.Writer.TryWrite(gameEvent);
            }

            return Task.CompletedTask;
        });
        using var serverWriteLock = new SemaphoreSlim(1, 1);
        var telnetNegotiator = new TradeWarsTelnetClientNegotiator();
        using var sessionCancellation = CancellationTokenSource.CreateLinkedTokenSource(stoppingToken);
        var receiveTask = ReceiveServerBytesAsync(
            stream,
            decoder,
            prompts.Writer,
            telnetNegotiator,
            serverWriteLock,
            sessionCancellation.Token);

        try
        {
            while (true)
            {
                ProxyGameEvent prompt;
                using (var timeout = CancellationTokenSource.CreateLinkedTokenSource(stoppingToken))
                {
                    timeout.CancelAfter(PromptTimeout);
                    try
                    {
                        prompt = await prompts.Reader.ReadAsync(timeout.Token).ConfigureAwait(false);
                    }
                    catch (OperationCanceledException) when (!stoppingToken.IsCancellationRequested)
                    {
                        if (sequence.ShouldStopAfterProgress)
                        {
                            return LoginSessionResult.TerminalFailure;
                        }

                        logger.LogWarning(
                            "Autonomous login did not receive the next expected prompt for agent {AgentId}; it will retry.",
                            listener.AgentId);
                        return LoginSessionResult.Retry;
                    }
                }

                var surface = prompt.PromptSurface ?? string.Empty;
                if (surface == "BBSName")
                {
                    await EnsureIdentityNameAsync(
                        modelConfiguration,
                        sequence,
                        stoppingToken).ConfigureAwait(false);
                }
                else if (surface == "NewCharacter")
                {
                    await EnsureNewGameCredentialsAsync(
                        modelConfiguration,
                        sequence,
                        stoppingToken).ConfigureAwait(false);
                }

                var result = await sequence.HandlePromptAsync(
                    surface,
                    (bytes, cancellationToken) => WriteServerBytesAsync(
                        stream,
                        serverWriteLock,
                        bytes,
                        cancellationToken),
                    stoppingToken).ConfigureAwait(false);
                if (result == LoginPromptResult.Authenticated)
                {
                    Volatile.Write(ref authenticated, 1);
                    logger.LogInformation(
                        "Autonomous login authenticated agent {AgentId} in the configured game profile.",
                        listener.AgentId);
                    await commandBroker.SendAsync(
                        listener.AgentId,
                        new GameCommandRequest(GameCommand.RequestShipInformation),
                        stoppingToken).ConfigureAwait(false);
                    logger.LogInformation(
                        "Requested the initial ship snapshot for agent {AgentId} after authentication.",
                        listener.AgentId);
                    await receiveTask.ConfigureAwait(false);
                    return LoginSessionResult.Authenticated;
                }
            }
        }
        catch (ChannelClosedException exception)
        {
            if (sequence.ShouldStopAfterProgress)
            {
                return LoginSessionResult.TerminalFailure;
            }

            throw new IOException(
                "The local proxy connection ended before an authenticated prompt was observed.",
                exception);
        }
        finally
        {
            sessionCancellation.Cancel();
            client.Dispose();
            try
            {
                await receiveTask.ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
            }
            catch (IOException)
            {
            }
            catch (SocketException)
            {
            }
            catch (ObjectDisposedException)
            {
            }
            prompts.Writer.TryComplete();
        }
    }

    private async Task EnsureIdentityNameAsync(
        ModelConfiguration modelConfiguration,
        TradeWarsLoginPromptSequence sequence,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(loginOptions.LoginName))
        {
            var loginName = ValidateGeneratedName(
                await identityProvider.ChooseServerLoginNameAsync(
                    modelConfiguration,
                    CreateIdentityContext(),
                    cancellationToken).ConfigureAwait(false),
                "server login name",
                maximumLength: 20);
            await credentialStore.SaveLoginNameAsync(loginName, cancellationToken).ConfigureAwait(false);
            loginOptions.LoginName = loginName;
        }

        if (string.IsNullOrWhiteSpace(gameProfile.TraderAlias))
        {
            var traderAlias = await ChooseDistinctTraderAliasAsync(
                modelConfiguration,
                cancellationToken).ConfigureAwait(false);
            await credentialStore.SaveTraderAliasAsync(
                selectedGame,
                traderAlias,
                cancellationToken).ConfigureAwait(false);
            gameProfile.TraderAlias = traderAlias;
        }

        sequence.SetLoginName(loginOptions.LoginName);
        sequence.SetTraderAlias(gameProfile.TraderAlias);
        if (!string.IsNullOrWhiteSpace(gameProfile.Password))
        {
            sequence.SetExistingGamePassword(gameProfile.Password);
        }
    }

    private async Task<string> ChooseDistinctTraderAliasAsync(
        ModelConfiguration modelConfiguration,
        CancellationToken cancellationToken)
    {
        for (var attempt = 0; attempt < MaximumIdentityAttempts; attempt++)
        {
            var alias = ValidateGeneratedName(
                await identityProvider.ChooseTraderAliasAsync(
                    modelConfiguration,
                    CreateIdentityContext(),
                    cancellationToken).ConfigureAwait(false),
                "trader alias",
                maximumLength: 20);
            var isUnique = !string.Equals(alias, loginOptions.LoginName, StringComparison.OrdinalIgnoreCase) &&
                           !loginOptions.Games.Any(profile =>
                               !string.Equals(profile.Key, selectedGame, StringComparison.OrdinalIgnoreCase) &&
                               string.Equals(profile.Value.TraderAlias, alias, StringComparison.OrdinalIgnoreCase));
            if (isUnique)
            {
                return alias;
            }
        }

        throw new InvalidDataException("The model did not generate a trader alias distinct from its login name.");
    }

    private async Task EnsureNewGameCredentialsAsync(
        ModelConfiguration modelConfiguration,
        TradeWarsLoginPromptSequence sequence,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(gameProfile.TraderAlias))
        {
            throw new InvalidDataException(
                "The server offered a new character before the model identity was selected.");
        }

        if (string.IsNullOrWhiteSpace(gameProfile.Password))
        {
            var password = await GenerateUniqueGamePasswordAsync(
                modelConfiguration,
                cancellationToken).ConfigureAwait(false);
            await credentialStore.SaveGameCredentialsAsync(
                selectedGame,
                gameProfile.TraderAlias,
                password,
                cancellationToken).ConfigureAwait(false);
            gameProfile.Password = password;
        }

        string shipName = string.IsNullOrWhiteSpace(gameProfile.ShipName)
            ? ValidateGeneratedName(
                await identityProvider.ChooseShipNameAsync(
                    modelConfiguration,
                    CreateIdentityContext(),
                    cancellationToken).ConfigureAwait(false),
                "ship name",
                maximumLength: 30,
                allowSpaces: true)
            : gameProfile.ShipName;
        string homePlanetName = string.IsNullOrWhiteSpace(gameProfile.HomePlanetName)
            ? ValidateGeneratedName(
                await identityProvider.ChooseHomePlanetNameAsync(
                    modelConfiguration,
                    CreateIdentityContext(),
                    cancellationToken).ConfigureAwait(false),
                "home planet name",
                maximumLength: 30,
                allowSpaces: true)
            : gameProfile.HomePlanetName;
        if (string.IsNullOrWhiteSpace(gameProfile.ShipName) ||
            string.IsNullOrWhiteSpace(gameProfile.HomePlanetName))
        {
            await credentialStore.SaveCharacterNamesAsync(
                selectedGame,
                shipName,
                homePlanetName,
                cancellationToken).ConfigureAwait(false);
            gameProfile.ShipName = shipName;
            gameProfile.HomePlanetName = homePlanetName;
        }

        sequence.SetNewCharacterCredentials(
            gameProfile.TraderAlias,
            gameProfile.Password,
            shipName,
            homePlanetName);
    }

    private async Task<string> GenerateUniqueGamePasswordAsync(
        ModelConfiguration modelConfiguration,
        CancellationToken cancellationToken)
    {
        var context = CreateIdentityContext();
        for (var attempt = 0; attempt < MaximumIdentityAttempts; attempt++)
        {
            var password = ValidateGeneratedValue(
                await identityProvider.GenerateGamePasswordAsync(
                    modelConfiguration,
                    context,
                    cancellationToken).ConfigureAwait(false),
                "game password",
                minimumLength: 8,
                maximumLength: 16);
            if (string.Equals(password, loginOptions.LoginName, StringComparison.OrdinalIgnoreCase) ||
                string.Equals(password, gameProfile.TraderAlias, StringComparison.OrdinalIgnoreCase) ||
                loginOptions.Games.Any(profile =>
                    !string.Equals(profile.Key, selectedGame, StringComparison.OrdinalIgnoreCase) &&
                    string.Equals(profile.Value.Password, password, StringComparison.Ordinal)))
            {
                continue;
            }

            return password;
        }

        throw new InvalidDataException("The model did not generate a unique password for this game.");
    }

    private AutonomousLoginIdentityContext CreateIdentityContext() =>
        new(
            listener.UpstreamHost,
            listener.UpstreamPort,
            selectedGame,
            gameProfile.GameLetter,
            loginOptions.LoginName,
            gameProfile.TraderAlias);

    private static string ValidateGeneratedValue(
        string value,
        string valueName,
        int minimumLength,
        int maximumLength)
    {
        if (string.IsNullOrWhiteSpace(value) ||
            value.Length < minimumLength ||
            value.Length > maximumLength ||
            value.Any(character => !char.IsAsciiLetterOrDigit(character)))
        {
            throw new InvalidDataException(
                $"The model generated an invalid {valueName}; it must be {minimumLength}-{maximumLength} ASCII letters or digits.");
        }

        return value;
    }

    private static string ValidateGeneratedName(
        string value,
        string valueName,
        int maximumLength,
        bool allowSpaces = false)
    {
        if (string.IsNullOrWhiteSpace(value) ||
            value.Length < 3 ||
            value.Length > maximumLength ||
            value != value.Trim() ||
            value.Any(character =>
                !char.IsAsciiLetter(character) && (!allowSpaces || character != ' ')))
        {
            string allowedCharacters = allowSpaces ? "ASCII letters and spaces" : "ASCII letters";
            throw new InvalidDataException(
                $"The model generated an invalid {valueName}; it must be 3-{maximumLength} {allowedCharacters}.");
        }

        return value;
    }

    private static async Task ReceiveServerBytesAsync(
        NetworkStream stream,
        TradeWarsTelnetTelemetryDecoder decoder,
        ChannelWriter<ProxyGameEvent> promptWriter,
        TradeWarsTelnetClientNegotiator telnetNegotiator,
        SemaphoreSlim serverWriteLock,
        CancellationToken cancellationToken)
    {
        var buffer = ArrayPool<byte>.Shared.Rent(4096);
        try
        {
            while (true)
            {
                var count = await stream.ReadAsync(buffer.AsMemory(), cancellationToken)
                    .ConfigureAwait(false);
                if (count == 0)
                {
                    break;
                }

                var responses = telnetNegotiator.CreateResponses(buffer.AsSpan(0, count));
                if (responses.Length > 0)
                {
                    await WriteServerBytesAsync(
                        stream,
                        serverWriteLock,
                        responses,
                        cancellationToken).ConfigureAwait(false);
                }

                await decoder.ObserveServerBytesAsync(
                    buffer.AsMemory(0, count),
                    cancellationToken).ConfigureAwait(false);
            }
        }
        finally
        {
            ArrayPool<byte>.Shared.Return(buffer, clearArray: true);
            promptWriter.TryComplete();
        }
    }

    private static async Task WriteServerBytesAsync(
        NetworkStream stream,
        SemaphoreSlim writeLock,
        ReadOnlyMemory<byte> bytes,
        CancellationToken cancellationToken)
    {
        await writeLock.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            await stream.WriteAsync(bytes, cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            writeLock.Release();
        }
    }

    private enum LoginSessionResult
    {
        Retry,
        Authenticated,
        TerminalFailure
    }
}
