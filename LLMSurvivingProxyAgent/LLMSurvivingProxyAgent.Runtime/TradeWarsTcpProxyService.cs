using System.Buffers;
using System.Net;
using System.Net.Sockets;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace LLMSurvivingProxyAgent.Runtime;

public sealed class TradeWarsTcpProxyService(
    IReadOnlyList<GameProxyListenerOptions> listeners,
    ProxyGameEventHub eventHub,
    ProxyGameCommandBroker commandBroker,
    ILogger<TradeWarsTcpProxyService> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var listenerTasks = listeners.Select(listener => RunListenerAsync(listener, stoppingToken));
        await Task.WhenAll(listenerTasks).ConfigureAwait(false);
    }

    private async Task RunListenerAsync(
        GameProxyListenerOptions options,
        CancellationToken stoppingToken)
    {
        var listenAddress = Validate(options);
        var listener = new TcpListener(listenAddress, options.ListenPort);
        await using var eventLog = new ProxyEventLogWriter(options.EventLogDirectory);
        listener.Start();
        logger.LogInformation(
            "TradeWars proxy for agent {AgentId} listening on {Address}:{Port} and forwarding to {UpstreamHost}:{UpstreamPort}.",
            options.AgentId,
            listenAddress,
            options.ListenPort,
            options.UpstreamHost,
            options.UpstreamPort);

        Task? activeSession = null;
        try
        {
            while (!stoppingToken.IsCancellationRequested)
            {
                var client = await listener.AcceptTcpClientAsync(stoppingToken).ConfigureAwait(false);
                if (activeSession is { IsCompleted: true })
                {
                    await activeSession.ConfigureAwait(false);
                    activeSession = null;
                }
                if (activeSession is { IsCompleted: false })
                {
                    logger.LogWarning(
                        "Rejected an extra client for agent {AgentId}; only one game client may use this proxy listener.",
                        options.AgentId);
                    client.Dispose();
                    continue;
                }

                activeSession = HandleClientAsync(client, options, eventLog, stoppingToken);
            }
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
        {
        }
        finally
        {
            listener.Stop();
        }

        if (activeSession is not null)
        {
            await ObserveSessionEndAsync(activeSession, options.AgentId).ConfigureAwait(false);
        }
    }

    private async Task HandleClientAsync(
        TcpClient client,
        GameProxyListenerOptions options,
        ProxyEventLogWriter eventLog,
        CancellationToken stoppingToken)
    {
        using (client)
        using (var upstream = new TcpClient())
        {
            client.NoDelay = true;
            upstream.NoDelay = true;
            try
            {
                await upstream.ConnectAsync(
                    options.UpstreamHost,
                    options.UpstreamPort,
                    stoppingToken).ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                return;
            }
            catch (SocketException exception)
            {
                logger.LogError(
                    exception,
                    "TradeWars proxy for agent {AgentId} could not connect to {UpstreamHost}:{UpstreamPort}.",
                    options.AgentId,
                    options.UpstreamHost,
                    options.UpstreamPort);
                return;
            }

            using var sessionCancellation = CancellationTokenSource.CreateLinkedTokenSource(stoppingToken);
            using var upstreamWriteLock = new SemaphoreSlim(1, 1);
            var commandSession = new ProxyGameCommandSession(
                upstream.GetStream(),
                upstreamWriteLock,
                sessionCancellation.Token,
                command => TryWriteEventAsync(
                    eventLog,
                    new ProxyGameEvent(
                        DateTimeOffset.UtcNow,
                        "AgentCommandSent",
                        Metadata: new Dictionary<string, string>(StringComparer.Ordinal)
                        {
                            ["command"] = command.ToString()
                        }),
                    options.AgentId,
                    CancellationToken.None));
            commandBroker.Register(options.AgentId, commandSession);
            try
            {
                await EmitProxyEventAsync(
                    eventLog,
                    options.AgentId,
                    new ProxyGameEvent(DateTimeOffset.UtcNow, "Connected"),
                    stoppingToken).ConfigureAwait(false);

                var decoder = new TradeWarsTelnetTelemetryDecoder(
                    (gameEvent, cancellationToken) => EmitProxyEventAsync(
                        eventLog,
                        options.AgentId,
                        gameEvent,
                        cancellationToken));
                var heartbeatTask = WriteHeartbeatsAsync(
                    eventLog,
                    options.AgentId,
                    sessionCancellation.Token);
                var clientToServer = PumpAsync(
                    client.GetStream(),
                    upstream.GetStream(),
                    observer: null,
                    sessionCancellation.Token,
                    upstreamWriteLock);
                var serverToClient = PumpAsync(
                    upstream.GetStream(),
                    client.GetStream(),
                    decoder.ObserveServerBytesAsync,
                    sessionCancellation.Token);

                await Task.WhenAny(clientToServer, serverToClient).ConfigureAwait(false);
                sessionCancellation.Cancel();
                client.Dispose();
                upstream.Dispose();
                await ObserveSessionEndAsync(clientToServer, options.AgentId).ConfigureAwait(false);
                await ObserveSessionEndAsync(serverToClient, options.AgentId).ConfigureAwait(false);
                await ObserveSessionEndAsync(heartbeatTask, options.AgentId).ConfigureAwait(false);
            }
            finally
            {
                commandBroker.Unregister(options.AgentId, commandSession);
            }

            await EmitProxyEventAsync(
                eventLog,
                options.AgentId,
                new ProxyGameEvent(DateTimeOffset.UtcNow, "Disconnected"),
                CancellationToken.None).ConfigureAwait(false);
        }
    }

    private async Task EmitProxyEventAsync(
        ProxyEventLogWriter eventLog,
        string agentId,
        ProxyGameEvent gameEvent,
        CancellationToken cancellationToken)
    {
        await TryWriteEventAsync(eventLog, gameEvent, agentId, cancellationToken).ConfigureAwait(false);
        if (!eventHub.TryPublish(new ProxySessionEvent(agentId, gameEvent)))
        {
            logger.LogWarning(
                "Proxy event queue is full; event {EventKind} for agent {AgentId} was not delivered to the autonomous test worker.",
                gameEvent.Kind,
                agentId);
        }
    }

    private async Task TryWriteEventAsync(
        ProxyEventLogWriter eventLog,
        ProxyGameEvent gameEvent,
        string agentId,
        CancellationToken cancellationToken)
    {
        try
        {
            await eventLog.WriteAsync(gameEvent, cancellationToken).ConfigureAwait(false);
        }
        catch (IOException exception)
        {
            logger.LogError(
                exception,
                "Proxy telemetry could not be persisted for agent {AgentId}; relay traffic will continue without that event.",
                agentId);
        }
        catch (UnauthorizedAccessException exception)
        {
            logger.LogError(
                exception,
                "Proxy telemetry path is not writable for agent {AgentId}; relay traffic will continue without that event.",
                agentId);
        }
    }

    private async Task WriteHeartbeatsAsync(
        ProxyEventLogWriter eventLog,
        string agentId,
        CancellationToken cancellationToken)
    {
        using var timer = new PeriodicTimer(TimeSpan.FromSeconds(5));
        while (await timer.WaitForNextTickAsync(cancellationToken).ConfigureAwait(false))
        {
            await TryWriteEventAsync(
                eventLog,
                new ProxyGameEvent(
                    DateTimeOffset.UtcNow,
                    "PresenceHeartbeat",
                    Metadata: new Dictionary<string, string>(StringComparer.Ordinal)
                    {
                        ["connected"] = "true"
                    }),
                agentId,
                cancellationToken).ConfigureAwait(false);
        }
    }

    private async Task PumpAsync(
        NetworkStream source,
        NetworkStream destination,
        Func<ReadOnlyMemory<byte>, CancellationToken, Task>? observer,
        CancellationToken cancellationToken,
        SemaphoreSlim? destinationWriteLock = null)
    {
        var buffer = ArrayPool<byte>.Shared.Rent(8192);
        try
        {
            while (true)
            {
                var count = await source.ReadAsync(buffer.AsMemory(), cancellationToken).ConfigureAwait(false);
                if (count == 0)
                {
                    return;
                }

                var data = buffer.AsMemory(0, count);
                if (destinationWriteLock is null)
                {
                    await destination.WriteAsync(data, cancellationToken).ConfigureAwait(false);
                }
                else
                {
                    await destinationWriteLock.WaitAsync(cancellationToken).ConfigureAwait(false);
                    try
                    {
                        await destination.WriteAsync(data, cancellationToken).ConfigureAwait(false);
                    }
                    finally
                    {
                        destinationWriteLock.Release();
                    }
                }
                if (observer is not null)
                {
                    await observer(data, cancellationToken).ConfigureAwait(false);
                }
            }
        }
        finally
        {
            ArrayPool<byte>.Shared.Return(buffer, clearArray: true);
        }
    }

    private async Task ObserveSessionEndAsync(Task task, string agentId)
    {
        try
        {
            await task.ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
        }
        catch (IOException exception)
        {
            logger.LogDebug(exception, "A TradeWars proxy stream ended for agent {AgentId}.", agentId);
        }
        catch (SocketException exception)
        {
            logger.LogDebug(exception, "A TradeWars proxy socket closed for agent {AgentId}.", agentId);
        }
        catch (ObjectDisposedException)
        {
        }
    }

    private static IPAddress Validate(GameProxyListenerOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);
        ArgumentException.ThrowIfNullOrWhiteSpace(options.AgentId);
        ArgumentException.ThrowIfNullOrWhiteSpace(options.UpstreamHost);
        ArgumentException.ThrowIfNullOrWhiteSpace(options.EventLogDirectory);
        if (!IPAddress.TryParse(options.ListenAddress, out var address) ||
            !IPAddress.IsLoopback(address))
        {
            throw new InvalidOperationException(
                $"GameProxy listener '{options.AgentId}' must bind to a loopback IP address.");
        }
        if (options.ListenPort is < 1 or > 65535 || options.UpstreamPort is < 1 or > 65535)
        {
            throw new InvalidOperationException(
                $"GameProxy listener '{options.AgentId}' must use valid TCP ports.");
        }

        return address;
    }
}
