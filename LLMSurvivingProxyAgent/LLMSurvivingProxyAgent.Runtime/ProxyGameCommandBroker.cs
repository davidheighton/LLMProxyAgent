using System.Collections.Concurrent;
using System.Net.Sockets;
using LLMSurvivingProxyAgent.Core;

namespace LLMSurvivingProxyAgent.Runtime;

public sealed class ProxyGameCommandBroker : IGameCommandSender
{
    private readonly ConcurrentDictionary<string, ProxyGameCommandSession> _sessions =
        new(StringComparer.OrdinalIgnoreCase);
    private readonly object _registrationLock = new();

    public bool HasActiveSession(string agentId)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(agentId);
        return _sessions.ContainsKey(agentId);
    }

    public Task SendAsync(
        string agentId,
        GameCommand command,
        CancellationToken cancellationToken = default) =>
        SendAsync(agentId, new GameCommandRequest(command), cancellationToken);

    public Task SendAsync(
        string agentId,
        GameCommandRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(agentId);
        ArgumentNullException.ThrowIfNull(request);
        if (!Enum.IsDefined(request.Command))
        {
            throw new ArgumentOutOfRangeException(nameof(request));
        }
        if (!_sessions.TryGetValue(agentId, out var session))
        {
            throw new InvalidOperationException(
                $"Agent '{agentId}' has no active game-proxy session.");
        }

        return session.SendAsync(request, cancellationToken);
    }

    internal void Register(string agentId, ProxyGameCommandSession session)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(agentId);
        ArgumentNullException.ThrowIfNull(session);
        lock (_registrationLock)
        {
            if (!_sessions.TryAdd(agentId, session))
            {
                throw new InvalidOperationException(
                    $"Agent '{agentId}' already has an active game-proxy command session.");
            }
        }
    }

    internal void Unregister(string agentId, ProxyGameCommandSession session)
    {
        lock (_registrationLock)
        {
            if (_sessions.TryGetValue(agentId, out var current) &&
                ReferenceEquals(current, session))
            {
                _sessions.TryRemove(agentId, out _);
            }
        }
    }
}

internal sealed class ProxyGameCommandSession(
    NetworkStream upstream,
    SemaphoreSlim upstreamWriteLock,
    CancellationToken sessionCancellation,
    Func<GameCommand, Task> commandSent)
{
    private static readonly byte[] ShipInformationRequest = [(byte)'I', (byte)'\r'];
    private static readonly byte[] CurrentSectorDisplayRequest = [(byte)'D', (byte)'\r'];
    private static readonly byte[] NavPointSelectionExit = [(byte)'Q', (byte)'\r'];

    public async Task SendAsync(GameCommandRequest request, CancellationToken cancellationToken)
    {
        var payload = request.Command switch
        {
            GameCommand.RequestShipInformation when request.TargetSector is null => ShipInformationRequest,
            GameCommand.DisplayCurrentSector when request.TargetSector is null => CurrentSectorDisplayRequest,
            GameCommand.ExitNavPointSelection when
                request.TargetSector is null && request.Product is null => NavPointSelectionExit,
            GameCommand.MoveToSector when request.TargetSector is > 0 and <= 65535 =>
                System.Text.Encoding.ASCII.GetBytes($"M{request.TargetSector.Value}\r"),
            _ => throw new ArgumentOutOfRangeException(nameof(request))
        };
        using var linkedCancellation = CancellationTokenSource.CreateLinkedTokenSource(
            sessionCancellation,
            cancellationToken);

        await upstreamWriteLock.WaitAsync(linkedCancellation.Token).ConfigureAwait(false);
        try
        {
            await upstream.WriteAsync(payload, linkedCancellation.Token).ConfigureAwait(false);
        }
        finally
        {
            upstreamWriteLock.Release();
        }

        await commandSent(request.Command).ConfigureAwait(false);
    }
}
