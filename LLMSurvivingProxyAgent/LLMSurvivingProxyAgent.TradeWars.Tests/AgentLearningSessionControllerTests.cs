using LLMSurvivingProxyAgent.Core;
using LLMSurvivingProxyAgent.Runtime;
using Microsoft.Extensions.Logging.Abstractions;

namespace LLMSurvivingProxyAgent.TradeWars.Tests;

public sealed class AgentLearningSessionControllerTests
{
    [Fact]
    public async Task LearnModeRecordsRepeatedTrustedObservationsUntilStopped()
    {
        var recorder = new CountingExperienceRecorder();
        var service = new GameSituationObservationService(
            new TrustedSituationProvider(),
            recorder);
        await using var controller = new AgentLearningSessionController(
            service,
            NullLogger<AgentLearningSessionController>.Instance,
            TimeSpan.FromMilliseconds(20));

        await controller.StartAsync("meridian");
        await recorder.TwoObservations.WaitAsync(TimeSpan.FromSeconds(5));

        Assert.True(controller.IsRunning);
        await controller.StopAsync();
        Assert.False(controller.IsRunning);
        int recordedAtStop = recorder.Count;
        await Task.Delay(60);
        Assert.Equal(recordedAtStop, recorder.Count);
        Assert.All(recorder.SessionIds, sessionId => Assert.StartsWith("learn-", sessionId));
    }

    [Fact]
    public async Task ChangedObservationServiceDoesNotRecordAnUnchangedSnapshotTwice()
    {
        var recorder = new CountingExperienceRecorder();
        var service = new GameSituationObservationService(
            new TrustedSituationProvider(changingState: false),
            recorder);

        GameSituationObservationResult first =
            await service.CaptureAndRecordChangedAsync("meridian", "learn-test");
        GameSituationObservationResult repeated =
            await service.CaptureAndRecordChangedAsync("meridian", "learn-test");

        Assert.True(first.Recorded);
        Assert.False(repeated.Recorded);
        Assert.Contains("has not changed", repeated.RejectionReason, StringComparison.Ordinal);
        Assert.Equal(1, recorder.Count);
    }

    private sealed class TrustedSituationProvider(bool changingState = true) : IGameSituationProvider
    {
        private int _sample;

        public Task<GameSituationReport> CaptureSituationAsync(
            string agentId,
            CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            DateTimeOffset now = DateTimeOffset.UtcNow;
            int sample = changingState ? Interlocked.Increment(ref _sample) : 0;
            var snapshot = new GameSnapshot(12, 100_000 + sample, 500 + sample, null, now)
            {
                Authenticated = true,
                CurrentPrompt = "Command",
                CurrentPromptObservedAt = now
            };
            return Task.FromResult(new GameSituationReport(
                now,
                GamePresenceState.Connected,
                GameAuthenticationState.Authenticated,
                GameSynchronizationState.Healthy,
                "Command",
                now,
                snapshot,
                []));
        }
    }

    private sealed class CountingExperienceRecorder : IAgentExperienceRecorder
    {
        private readonly TaskCompletionSource _twoObservations =
            new(TaskCreationOptions.RunContinuationsAsynchronously);
        private readonly List<string> _sessionIds = [];
        private int _count;

        public int Count => Volatile.Read(ref _count);
        public Task TwoObservations => _twoObservations.Task;
        public IReadOnlyList<string> SessionIds
        {
            get
            {
                lock (_sessionIds)
                {
                    return _sessionIds.ToArray();
                }
            }
        }

        public Task RecordMacroExecutionAsync(
            MacroExecutionRecord record,
            GameSnapshot? beforeSnapshot = null,
            GameSnapshot? afterSnapshot = null,
            CancellationToken cancellationToken = default) =>
            Task.CompletedTask;

        public Task RecordTurnObservationAsync(
            string agentId,
            string sessionId,
            int turnNumber,
            GameSnapshot beforeSnapshot,
            GameSnapshot afterSnapshot,
            CancellationToken cancellationToken = default) =>
            Task.CompletedTask;

        public Task RecordSituationObservationAsync(
            string agentId,
            string sessionId,
            GameSituationReport report,
            CancellationToken cancellationToken = default)
        {
            lock (_sessionIds)
            {
                _sessionIds.Add(sessionId);
            }

            if (Interlocked.Increment(ref _count) >= 2)
            {
                _twoObservations.TrySetResult();
            }
            return Task.CompletedTask;
        }
    }
}
