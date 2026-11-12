using LLMSurvivingProxyAgent.Core;
using LLMSurvivingProxyAgent.Runtime;

namespace MTC;

internal sealed class EmbeddedMtcAgentSessionController(
    string agentId,
    IAgentStore agentStore,
    IAutonomousTestWorkerController workerController,
    IAgentLearningSessionController learningController,
    MtcGameCommandSender commandSender,
    Func<bool> isConnected,
    Func<bool> hasSessionDatabase,
    Func<Task<bool>> canConnectAsync,
    Func<Task> configureConnectionAsync,
    Func<Task<bool>> loadConnectionAsync,
    Func<Task> connectAsync,
    Func<Task> disconnectAsync,
    Func<Task> publishCurrentPromptAsync) : IAgentSessionController
{
    private readonly SemaphoreSlim _lifecycle = new(1, 1);

    public bool IsAvailable => true;

    public bool SupportsAgent(string requestedAgentId) =>
        string.Equals(requestedAgentId, agentId, StringComparison.OrdinalIgnoreCase);

    public async Task<AgentSessionControlStatus> GetStatusAsync(
        string requestedAgentId,
        CancellationToken cancellationToken = default)
    {
        ValidateAgent(requestedAgentId);
        cancellationToken.ThrowIfCancellationRequested();
        bool connected = isConnected();
        return new AgentSessionControlStatus(
            connected,
            workerController.IsRunning,
            learningController.IsRunning,
            connected || await canConnectAsync().ConfigureAwait(false));
    }

    public async Task ConfigureConnectionAsync(
        string requestedAgentId,
        CancellationToken cancellationToken = default)
    {
        ValidateAgent(requestedAgentId);
        if (isConnected())
        {
            throw new InvalidOperationException(
                "Disconnect the current MTC session before changing its connection profile.");
        }

        await configureConnectionAsync().WaitAsync(cancellationToken).ConfigureAwait(false);
    }

    public async Task<bool> LoadSavedConnectionAsync(
        string requestedAgentId,
        CancellationToken cancellationToken = default)
    {
        ValidateAgent(requestedAgentId);
        if (isConnected())
        {
            throw new InvalidOperationException(
                "Disconnect the current MTC session before loading another connection profile.");
        }

        return await loadConnectionAsync().WaitAsync(cancellationToken).ConfigureAwait(false);
    }

    public async Task ConnectAsync(
        string requestedAgentId,
        CancellationToken cancellationToken = default)
    {
        ValidateAgent(requestedAgentId);
        if (isConnected())
        {
            return;
        }
        if (!await canConnectAsync().ConfigureAwait(false))
        {
            throw new InvalidOperationException(
                "Configure or load a game connection in MTC before connecting.");
        }

        await connectAsync().WaitAsync(cancellationToken).ConfigureAwait(false);
        if (!isConnected())
        {
            throw new InvalidOperationException(
                "MTC did not establish a game connection. Check the terminal for connection details.");
        }
    }

    public async Task DisconnectAsync(
        string requestedAgentId,
        CancellationToken cancellationToken = default)
    {
        ValidateAgent(requestedAgentId);
        await _lifecycle.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            await StopAgentCoreAsync(cancellationToken).ConfigureAwait(false);
            if (isConnected())
            {
                await disconnectAsync().WaitAsync(cancellationToken).ConfigureAwait(false);
            }
        }
        finally
        {
            _lifecycle.Release();
        }
    }

    public async Task StartAgentAsync(
        string requestedAgentId,
        CancellationToken cancellationToken = default)
    {
        ValidateAgent(requestedAgentId);
        await _lifecycle.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            if (workerController.IsRunning && !learningController.IsRunning)
            {
                return;
            }
            if (!isConnected())
            {
                throw new InvalidOperationException(
                    "Connect the MTC game terminal before starting Meridian.");
            }
            if (!hasSessionDatabase())
            {
                throw new InvalidOperationException(
                    "The MTC game database is not open. Check whether another MTC process is using this game profile.");
            }

            await learningController.StopAsync(cancellationToken).ConfigureAwait(false);

            var agent = await agentStore.GetAgentAsync(agentId, cancellationToken).ConfigureAwait(false)
                ?? throw new InvalidOperationException($"Agent '{agentId}' is not configured.");
            ArgumentException.ThrowIfNullOrWhiteSpace(agent.PreferredModel);

            if (workerController.IsRunning)
            {
                return;
            }

            try
            {
                commandSender.SetCommandsApproved(true);
                commandSender.SetLoginInputApproved(true);
                await workerController.StartAsync(agent.PreferredModel, cancellationToken)
                    .ConfigureAwait(false);
                await commandSender.RefreshShipInformationIfAtCommandPromptAsync(
                    agentId,
                    cancellationToken).ConfigureAwait(false);
                await publishCurrentPromptAsync().WaitAsync(cancellationToken).ConfigureAwait(false);
            }
            catch
            {
                commandSender.SetCommandsApproved(false);
                commandSender.SetLoginInputApproved(false);
                await workerController.StopAsync(CancellationToken.None).ConfigureAwait(false);
                throw;
            }
        }
        finally
        {
            _lifecycle.Release();
        }
    }

    public async Task StopAgentAsync(
        string requestedAgentId,
        CancellationToken cancellationToken = default)
    {
        ValidateAgent(requestedAgentId);
        await _lifecycle.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            await StopAgentCoreAsync(cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            _lifecycle.Release();
        }
    }

    public async Task StartLearningAsync(
        string requestedAgentId,
        CancellationToken cancellationToken = default)
    {
        ValidateAgent(requestedAgentId);
        await _lifecycle.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            if (learningController.IsRunning)
            {
                return;
            }
            if (!isConnected())
            {
                throw new InvalidOperationException(
                    "Connect the MTC game terminal before starting Learn mode.");
            }

            await StopAgentCoreAsync(cancellationToken).ConfigureAwait(false);
            await learningController.StartAsync(agentId, cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            commandSender.SetCommandsApproved(false);
            commandSender.SetLoginInputApproved(false);
            _lifecycle.Release();
        }
    }

    public async Task StopLearningAsync(
        string requestedAgentId,
        CancellationToken cancellationToken = default)
    {
        ValidateAgent(requestedAgentId);
        await _lifecycle.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            await learningController.StopAsync(cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            _lifecycle.Release();
        }
    }

    public async Task RefreshCimAsync(
        string requestedAgentId,
        CancellationToken cancellationToken = default)
    {
        ValidateAgent(requestedAgentId);
        await _lifecycle.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            if (!isConnected())
            {
                throw new InvalidOperationException(
                    "Connect the MTC game terminal before refreshing CIM.");
            }
            if (workerController.IsRunning || learningController.IsRunning)
            {
                throw new InvalidOperationException(
                    "Stop Meridian or Learn mode before refreshing CIM to prevent concurrent game activity.");
            }

            commandSender.SetCommandsApproved(true);
            try
            {
                await commandSender.RefreshCimAsync(agentId, cancellationToken)
                    .ConfigureAwait(false);
            }
            finally
            {
                commandSender.SetCommandsApproved(false);
            }
        }
        finally
        {
            _lifecycle.Release();
        }
    }

    private async Task StopAgentCoreAsync(CancellationToken cancellationToken)
    {
        commandSender.SetCommandsApproved(false);
        commandSender.SetLoginInputApproved(false);
        try
        {
            await workerController.StopAsync(cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            await learningController.StopAsync(cancellationToken).ConfigureAwait(false);
        }
    }

    private void ValidateAgent(string requestedAgentId)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(requestedAgentId);
        if (!string.Equals(requestedAgentId, agentId, StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException(
                $"Agent '{requestedAgentId}' is not attached to the active MTC session.");
        }
    }
}
