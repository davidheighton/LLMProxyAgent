using LLMSurvivingProxyAgent.Core;
using LLMSurvivingProxyAgent.Runtime;

namespace LLMSurvivingProxyAgent.TradeWars.Tests;

public sealed class AutonomousActionWatchdogTests
{
    [Fact]
    public void RepeatedCommandCyclesWithoutVerifiedProgressRequestStrategicRecovery()
    {
        var watchdog = new AutonomousActionWatchdog();
        DateTimeOffset now = DateTimeOffset.UtcNow;

        AutonomousActionWatchdogObservation initial = watchdog.Observe(
            "meridian",
            CreateSituation(now, credits: 25_000),
            previousAction: null,
            now);

        Assert.Equal(0, initial.UnchangedCycles);
        Assert.Null(initial.RecoveryReason);

        for (int unchangedCycle = 1;
             unchangedCycle < AutonomousActionWatchdog.RequiredUnchangedCycles;
             unchangedCycle++)
        {
            AutonomousActionWatchdogObservation observation = watchdog.Observe(
                "meridian",
                CreateSituation(now.AddSeconds(unchangedCycle), credits: 25_000),
                "None",
                now.AddSeconds(unchangedCycle));

            Assert.Equal(unchangedCycle, observation.UnchangedCycles);
            Assert.Null(observation.RecoveryReason);
        }

        DateTimeOffset recoveryAt = now.AddSeconds(
            AutonomousActionWatchdog.RequiredUnchangedCycles);
        AutonomousActionWatchdogObservation recovery = watchdog.Observe(
            "meridian",
            CreateSituation(recoveryAt, credits: 25_000),
            "None",
            recoveryAt);

        Assert.Equal(AutonomousActionWatchdog.RequiredUnchangedCycles, recovery.UnchangedCycles);
        Assert.Contains("identify the blocking precondition", recovery.RecoveryReason);
        Assert.Equal(
            recoveryAt + AutonomousActionWatchdog.ReviewRequestCooldown,
            recovery.NextReviewEligibleAt);

        AutonomousActionWatchdogObservation duplicatePrompt = watchdog.Observe(
            "meridian",
            CreateSituation(recoveryAt, credits: 25_000),
            "None",
            recoveryAt.AddSeconds(1));
        Assert.Null(duplicatePrompt.RecoveryReason);
        Assert.Equal(recovery.UnchangedCycles, duplicatePrompt.UnchangedCycles);
        Assert.Equal(recovery.NextReviewEligibleAt, duplicatePrompt.NextReviewEligibleAt);

        DateTimeOffset progressAt = recoveryAt.AddSeconds(2);
        AutonomousActionWatchdogObservation progress = watchdog.Observe(
            "meridian",
            CreateSituation(progressAt, credits: 25_001),
            "None",
            progressAt);
        Assert.Equal(0, progress.UnchangedCycles);
        Assert.Null(progress.RecoveryReason);
        Assert.Null(progress.NextReviewEligibleAt);
    }

    [Fact]
    public void WatchdogIgnoresUnhealthyOrNonCommandSituations()
    {
        var watchdog = new AutonomousActionWatchdog();
        DateTimeOffset now = DateTimeOffset.UtcNow;
        GameSituationReport situation = CreateSituation(now, credits: 25_000) with
        {
            Synchronization = GameSynchronizationState.Stale
        };

        AutonomousActionWatchdogObservation observation = watchdog.Observe(
            "meridian",
            situation,
            "None",
            now);

        Assert.Equal(0, observation.UnchangedCycles);
        Assert.Null(observation.RecoveryReason);
    }

    private static GameSituationReport CreateSituation(DateTimeOffset observedAt, long credits) =>
        new(
            observedAt,
            GamePresenceState.Connected,
            GameAuthenticationState.Authenticated,
            GameSynchronizationState.Healthy,
            "Command",
            observedAt,
            new GameSnapshot(551, credits, 100, null, observedAt),
            []);
}
