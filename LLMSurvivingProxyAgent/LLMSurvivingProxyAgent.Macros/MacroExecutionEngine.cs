using LLMSurvivingProxyAgent.Core;
using LLMSurvivingProxyAgent.TradeWars;

namespace LLMSurvivingProxyAgent.Macros;

public sealed class MacroExecutionEngine(
    MacroRegistry registry,
    IMacroTelemetryStore telemetryStore,
    IAgentExperienceRecorder? experienceRecorder = null,
    ISynchronizedGameSnapshotProvider? snapshotProvider = null)
{
    public async Task<MacroExecutionRecord> ExecuteAsync(
        string agentId,
        string macroId,
        int? currentSector,
        IWorldStateStore worldStateStore,
        NavigationService navigationService,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(agentId);
        ArgumentNullException.ThrowIfNull(worldStateStore);
        ArgumentNullException.ThrowIfNull(navigationService);
        if (snapshotProvider is not null && experienceRecorder is null)
        {
            throw new InvalidOperationException(
                "Synchronized snapshots require an agent experience recorder so the observations are persisted.");
        }

        var macro = registry.GetRequired(macroId);
        var beforeSnapshot = await CaptureSnapshotAsync(agentId, cancellationToken);
        var startTime = DateTimeOffset.UtcNow;
        if (beforeSnapshot is not null &&
            (beforeSnapshot.ObservedAt > startTime ||
             currentSector is { } suppliedSector &&
             beforeSnapshot.CurrentSector is { } observedSector &&
             suppliedSector != observedSector))
        {
            throw new InvalidDataException(
                "The synchronized pre-execution snapshot is future-dated or conflicts with the supplied sector.");
        }
        currentSector ??= beforeSnapshot?.CurrentSector;
        var context = new MacroContext(agentId, currentSector, worldStateStore, navigationService);
        MacroExecutionRecord record;

        try
        {
            var check = await macro.CanExecuteAsync(context, cancellationToken);
            MacroResult result;
            if (!check.CanExecute)
            {
                result = new MacroResult(
                    MacroExecutionStatus.Blocked,
                    "Macro could not execute.",
                    string.Empty,
                    []);
            }
            else
            {
                var estimate = await macro.EstimateOutcomeAsync(context, cancellationToken);
                result = await macro.ExecuteAsync(context, cancellationToken);
                if (!string.IsNullOrWhiteSpace(estimate))
                {
                    result = result with
                    {
                        Notes = JoinNotes(estimate, result.Notes)
                    };
                }
            }

            record = new MacroExecutionRecord(
                Guid.NewGuid().ToString("N"),
                agentId,
                macro.Descriptor.Id,
                macro.Descriptor.Name,
                macro.Descriptor.Category,
                startTime,
                DateTimeOffset.UtcNow,
                result.Status,
                result.Summary,
                JoinNotes(check.Notes, result.Notes),
                result.PlanSectors);
        }
        catch (Exception executionException)
        {
            var failureRecord = new MacroExecutionRecord(
                Guid.NewGuid().ToString("N"),
                agentId,
                macro.Descriptor.Id,
                macro.Descriptor.Name,
                macro.Descriptor.Category,
                startTime,
                DateTimeOffset.UtcNow,
                executionException is OperationCanceledException
                    ? MacroExecutionStatus.Cancelled
                    : MacroExecutionStatus.Failed,
                executionException.Message,
                executionException.GetType().Name,
                []);
            GameSnapshot? afterSnapshot = null;
            Exception? snapshotException = null;
            try
            {
                afterSnapshot = await CaptureSnapshotAsync(agentId, CancellationToken.None);
            }
            catch (Exception exception)
            {
                snapshotException = exception;
                failureRecord = failureRecord with
                {
                    Notes = JoinNotes(
                        failureRecord.Notes,
                        $"Post-execution synchronized snapshot capture failed ({exception.GetType().Name}).")
                };
            }

            try
            {
                await telemetryStore.SaveAsync(failureRecord, CancellationToken.None);
                if (experienceRecorder is not null)
                {
                    await experienceRecorder.RecordMacroExecutionAsync(
                        failureRecord,
                        beforeSnapshot,
                        afterSnapshot,
                        CancellationToken.None);
                }
            }
            catch (Exception telemetryException)
            {
                if (snapshotException is not null)
                {
                    throw new AggregateException(
                        "Macro execution, post-state observation, and telemetry or experience persistence failed.",
                        executionException,
                        snapshotException,
                        telemetryException);
                }
                throw new AggregateException(
                    "Macro execution failed and its telemetry or agent experience could not be persisted.",
                    executionException,
                    telemetryException);
            }

            if (snapshotException is not null)
            {
                throw new AggregateException(
                    "Macro execution failed and its post-execution synchronized snapshot could not be captured.",
                    executionException,
                    snapshotException);
            }
            throw;
        }

        GameSnapshot? postExecutionSnapshot = null;
        Exception? postSnapshotException = null;
        try
        {
            postExecutionSnapshot = await CaptureSnapshotAsync(agentId, CancellationToken.None);
        }
        catch (Exception exception)
        {
            postSnapshotException = exception;
            record = record with
            {
                Notes = JoinNotes(
                    record.Notes,
                    $"Post-execution synchronized snapshot capture failed ({exception.GetType().Name}).")
            };
        }

        try
        {
            await telemetryStore.SaveAsync(record, cancellationToken);
        }
        catch (Exception persistenceException) when (postSnapshotException is not null)
        {
            throw new AggregateException(
                "Macro telemetry persistence and post-execution synchronized snapshot capture failed.",
                persistenceException,
                postSnapshotException);
        }
        if (experienceRecorder is not null)
        {
            try
            {
                await experienceRecorder.RecordMacroExecutionAsync(
                    record,
                    beforeSnapshot,
                    postExecutionSnapshot,
                    cancellationToken);
            }
            catch (Exception experienceException)
            {
                if (postSnapshotException is not null)
                {
                    throw new AggregateException(
                        "Macro telemetry was persisted, but agent experience recording and post-state observation failed.",
                        experienceException,
                        postSnapshotException);
                }
                throw new InvalidOperationException(
                    "Macro telemetry was persisted, but its agent experience could not be recorded.",
                    experienceException);
            }
        }
        if (postSnapshotException is not null)
        {
            var persistenceStatus = experienceRecorder is null
                ? "Macro telemetry was persisted"
                : "Macro telemetry and agent experience were persisted";
            throw new InvalidOperationException(
                $"{persistenceStatus}, but the post-execution synchronized snapshot could not be captured.",
                postSnapshotException);
        }
        return record;
    }

    private async Task<GameSnapshot?> CaptureSnapshotAsync(
        string agentId,
        CancellationToken cancellationToken)
    {
        if (snapshotProvider is null)
        {
            return null;
        }

        var snapshot = await snapshotProvider.CaptureSynchronizedSnapshotAsync(agentId, cancellationToken);
        if (snapshot is not null && !GameSnapshotValidation.IsValid(snapshot))
        {
            throw new InvalidDataException("The synchronized game-state provider returned an invalid snapshot.");
        }
        return snapshot;
    }

    private static string JoinNotes(params string[] notes) =>
        string.Join(
            Environment.NewLine,
            notes.Where(note => !string.IsNullOrWhiteSpace(note)));
}
