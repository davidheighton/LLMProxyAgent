using System.Collections.Concurrent;
using LLMSurvivingProxyAgent.Core;
using LLMSurvivingProxyAgent.Goals;
using Microsoft.Extensions.Logging;

namespace LLMSurvivingProxyAgent.Runtime;

public sealed class StrategicReviewAutonomousActionObjectiveProvider(
    IGoalProposalStore goalStore,
    GoalProposalService proposalService,
    StrategicReviewService strategicReviewService,
    ModelConfiguration modelConfiguration,
    ILogger<StrategicReviewAutonomousActionObjectiveProvider>? logger = null,
    TimeProvider? timeProvider = null) : IAutonomousActionObjectiveProvider
{
    private static readonly TimeSpan ReviewInterval = TimeSpan.FromMinutes(20);
    private static readonly TimeSpan MinimumReviewInterval = TimeSpan.FromMinutes(15);
    private readonly TimeProvider _timeProvider = timeProvider ?? TimeProvider.System;
    private static readonly LearningObjectiveSeed[] AlphaLearningObjectives =
    [
        new(
            "Establish identity",
            "Learn the authenticated agent identity and verified ship identity from synchronized observations."),
        new(
            "Establish synchronization",
            "Learn how connection, authentication, synchronization, and fresh ship status are verified."),
        new(
            "Establish location",
            "Learn the current sector and how location observations are confirmed."),
        new(
            "Learn trading",
            "Learn port trading from verified port inventory, ship cargo, and credit changes."),
        new(
            "Learn movement",
            "Learn safe one-sector navigation using fresh, verified adjacent-sector data."),
        new(
            "Learn ports",
            "Learn how port classes, inventories, and available services are observed and verified."),
        new(
            "Learn turns",
            "Learn how turns are observed and how turn-consuming actions affect the available balance."),
        new(
            "Learn prompts",
            "Learn how terminal prompts identify the safe next interaction and how prompt changes are verified.")
    ];

    private readonly ConcurrentDictionary<string, ReviewState> _reviewStates =
        new(StringComparer.OrdinalIgnoreCase);

    public Task<AutonomousActionObjective?> GetOrCreateActiveObjectiveAsync(
        string agentId,
        GameSnapshot snapshot,
        CancellationToken cancellationToken = default) =>
        GetOrCreateActiveObjectiveAsync(agentId, snapshot, integratedGameClient: null, cancellationToken);

    public Task<AutonomousActionObjective?> GetOrCreateActiveObjectiveAsync(
        string agentId,
        GameSituationReport situation,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(situation);
        if (situation.Snapshot is not { } snapshot)
        {
            throw new ArgumentException(
                "An active objective requires a synchronized game snapshot.",
                nameof(situation));
        }

        return GetOrCreateActiveObjectiveAsync(
            agentId,
            snapshot,
            situation.IntegratedGameClient,
            cancellationToken);
    }

    private async Task<AutonomousActionObjective?> GetOrCreateActiveObjectiveAsync(
        string agentId,
        GameSnapshot snapshot,
        IntegratedGameClientSnapshot? integratedGameClient,
        CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(agentId);
        ArgumentNullException.ThrowIfNull(snapshot);
        if (!GameSnapshotValidation.IsValid(snapshot))
        {
            throw new ArgumentException("An active objective requires a valid synchronized snapshot.", nameof(snapshot));
        }

        if (integratedGameClient?.CurrentSector is { } integratedSector &&
            snapshot.CurrentSector is { } synchronizedSector &&
            integratedSector != synchronizedSector)
        {
            logger?.LogWarning(
                "MTC reported sector {MtcSector} for agent {AgentId}, but the synchronized ship snapshot reports sector {ShipSector}; discarding MTC sector details for this review.",
                integratedSector,
                agentId,
                synchronizedSector);
        }
        integratedGameClient = IntegratedGameClientLocationPolicy.UseSynchronizedShipLocation(
            snapshot,
            integratedGameClient);

        ReviewState state = _reviewStates.GetOrAdd(agentId, static _ => new ReviewState());
        await state.Gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            await EnsureAlphaLearningProposalsAsync(agentId, snapshot, cancellationToken)
                .ConfigureAwait(false);
            var objectives = await GetActiveObjectivesAsync(agentId, cancellationToken).ConfigureAwait(false);
            bool significantChange = state.LastReviewedSnapshot is { } previousSnapshot &&
                HasSignificantChange(previousSnapshot, snapshot);
            string? requestedReason = state.GetRequestedReason();
            DateTimeOffset reviewNow = _timeProvider.GetUtcNow();
            TimeSpan sinceLastReview = state.LastReviewedAt is { } lastReviewedAt
                ? reviewNow - lastReviewedAt
                : TimeSpan.MaxValue;
            bool reviewDue =
                state.LastReviewedAt is null ||
                sinceLastReview >= ReviewInterval ||
                (sinceLastReview >= MinimumReviewInterval &&
                 (objectives.Count == 0 || requestedReason is not null || significantChange));

            if (reviewDue)
            {
                string reviewReason = requestedReason ??
                    (objectives.Count == 0
                        ? "No active objective exists; select a learning objective for this session."
                        : significantChange
                            ? "A significant synchronized game-state change requires strategic review."
                            : "The scheduled strategic review interval has elapsed.");
                StrategicReviewRecord? review = null;
                try
                {
                    review = await strategicReviewService.ReviewWithModelAsync(
                        agentId,
                        snapshot,
                        modelConfiguration,
                        reviewReason,
                        integratedGameClient,
                        cancellationToken).ConfigureAwait(false);
                }
                catch (StrategicAssessmentValidationException exception)
                {
                    logger?.LogWarning(
                        exception,
                        "Strategic review returned an invalid assessment for agent {AgentId}; keeping the active objective or selecting from the pending learning curriculum.",
                        agentId);
                }
                catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
                {
                    throw;
                }
                catch (Exception exception)
                {
                    logger?.LogError(
                        exception,
                        "Strategic review failed for agent {AgentId}; delaying the next review attempt.",
                        agentId);
                    throw;
                }
                finally
                {
                    if (!cancellationToken.IsCancellationRequested)
                    {
                        state.ClearRequestedReason();
                        state.RecordReview(snapshot, _timeProvider.GetUtcNow());
                    }
                }

                if (review is not null)
                {
                    state.SetPlannedScripts(review.Assessment.PlannedScripts);
                    objectives = await GetActiveObjectivesAsync(agentId, cancellationToken).ConfigureAwait(false);

                    if (objectives.Count == 0 &&
                        review.Assessment.ObjectiveDecision == ObjectiveDecision.Complete)
                    {
                        StrategicReviewRecord transitionReview = await strategicReviewService.ReviewWithModelAsync(
                            agentId,
                            snapshot,
                            modelConfiguration,
                            "The previous learning objective is complete; select or create the next learning objective.",
                            integratedGameClient,
                            cancellationToken).ConfigureAwait(false);
                        state.SetPlannedScripts(transitionReview.Assessment.PlannedScripts);
                        state.RecordReview(snapshot, _timeProvider.GetUtcNow());
                        objectives = await GetActiveObjectivesAsync(agentId, cancellationToken).ConfigureAwait(false);
                        if (objectives.Count == 0 &&
                            transitionReview.Assessment.ObjectiveDecision == ObjectiveDecision.Complete)
                        {
                            throw new InvalidDataException(
                                "Strategic review completed an objective without selecting or creating its successor.");
                        }
                    }
                }
            }

            if (objectives.Count == 0)
            {
                objectives = await SelectOrCreateLearningObjectiveAsync(
                    agentId,
                    snapshot,
                    cancellationToken).ConfigureAwait(false);
            }

            AgentObjective activeObjective = objectives.Single();
            return new AutonomousActionObjective(activeObjective.Goal, activeObjective.Rationale)
            {
                PlannedScripts = state.GetPlannedScripts()
            };
        }
        finally
        {
            state.Gate.Release();
        }
    }

    public void RequestStrategicReview(string agentId, string reason)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(agentId);
        ArgumentException.ThrowIfNullOrWhiteSpace(reason);
        if (reason.Length > 500)
        {
            reason = reason[..500];
        }

        _reviewStates.GetOrAdd(agentId, static _ => new ReviewState())
            .RequestReview(reason);
    }

    private async Task EnsureAlphaLearningProposalsAsync(
        string agentId,
        GameSnapshot snapshot,
        CancellationToken cancellationToken)
    {
        var proposals = await goalStore.GetProposalsAsync(
            agentId,
            limit: 500,
            cancellationToken: cancellationToken).ConfigureAwait(false);
        var existingGoals = proposals.Select(proposal => proposal.Goal)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        foreach (LearningObjectiveSeed seed in AlphaLearningObjectives)
        {
            if (existingGoals.Contains(seed.Goal))
            {
                continue;
            }

            await proposalService.ProposeAsync(
                new GoalProposalDraft(
                    agentId,
                    seed.Goal,
                    seed.Rationale,
                    1m,
                    [],
                    snapshot,
                    []),
                cancellationToken).ConfigureAwait(false);
        }
    }

    private async Task<IReadOnlyList<AgentObjective>> SelectOrCreateLearningObjectiveAsync(
        string agentId,
        GameSnapshot snapshot,
        CancellationToken cancellationToken)
    {
        var pendingProposals = await goalStore.GetProposalsAsync(
            agentId,
            GoalProposalStatus.PendingSelection,
            limit: 500,
            cancellationToken: cancellationToken).ConfigureAwait(false);
        if (pendingProposals.Count == 0)
        {
            int objectiveNumber = (await goalStore.GetProposalsAsync(
                agentId,
                limit: 500,
                cancellationToken: cancellationToken).ConfigureAwait(false)).Count + 1;
            await proposalService.ProposeAsync(
                new GoalProposalDraft(
                    agentId,
                    $"Learn a new TradeWars system behavior (objective {objectiveNumber}).",
                    "All supplied Alpha-1 learning objectives are complete. Select an observable, unresolved system behavior and learn it using verified evidence.",
                    1m,
                    [],
                    snapshot,
                    []),
                cancellationToken).ConfigureAwait(false);
            pendingProposals = await goalStore.GetProposalsAsync(
                agentId,
                GoalProposalStatus.PendingSelection,
                limit: 500,
                cancellationToken: cancellationToken).ConfigureAwait(false);
        }

        if (pendingProposals.Count == 0)
        {
            throw new InvalidOperationException(
                $"Agent '{agentId}' has no pending proposal from which to establish an active learning objective.");
        }

        GoalProposal selected = pendingProposals
            .OrderBy(proposal => CurriculumOrder(proposal.Goal))
            .ThenBy(proposal => proposal.ProposedAt)
            .First();
        await proposalService.SelectProposalAsync(
            agentId,
            selected.ProposalId,
            "Strategic Brain fallback: keep exactly one learning objective active when review does not select one.",
            cancellationToken).ConfigureAwait(false);
        return await GetActiveObjectivesAsync(agentId, cancellationToken).ConfigureAwait(false);
    }

    private async Task<IReadOnlyList<AgentObjective>> GetActiveObjectivesAsync(
        string agentId,
        CancellationToken cancellationToken)
    {
        var objectives = await goalStore.GetObjectivesAsync(
            agentId,
            ObjectiveStatus.Active,
            limit: 2,
            cancellationToken).ConfigureAwait(false);
        if (objectives.Count > 1)
        {
            throw new InvalidDataException($"Agent '{agentId}' has more than one active objective.");
        }

        return objectives;
    }

    private static int CurriculumOrder(string goal)
    {
        int index = Array.FindIndex(
            AlphaLearningObjectives,
            seed => string.Equals(seed.Goal, goal, StringComparison.OrdinalIgnoreCase));
        return index < 0 ? AlphaLearningObjectives.Length : index;
    }

    private static bool HasSignificantChange(GameSnapshot previous, GameSnapshot current)
    {
        if (previous.CurrentSector != current.CurrentSector ||
            previous.Turns != current.Turns ||
            previous.HoloScannerInstalled != current.HoloScannerInstalled ||
            previous.HoldsTotal != current.HoldsTotal ||
            previous.HoldsEmpty != current.HoldsEmpty ||
            previous.FuelOre != current.FuelOre ||
            previous.Organics != current.Organics ||
            previous.Equipment != current.Equipment ||
            previous.Shields != current.Shields)
        {
            return true;
        }

        long? priorCredits = previous.SynchronizedCredits;
        long? currentCredits = current.SynchronizedCredits;
        if (priorCredits != currentCredits && priorCredits is { } prior && currentCredits is { } latest)
        {
            long threshold = Math.Max(5_000, prior / 10);
            if (Math.Abs(latest - prior) >= threshold)
            {
                return true;
            }
        }

        if (previous.Ship is not { } priorShip || current.Ship is not { } currentShip)
        {
            return previous.Ship is not null || current.Ship is not null;
        }

        return !string.Equals(priorShip.ShipType, currentShip.ShipType, StringComparison.Ordinal) ||
            priorShip.SectorId != currentShip.SectorId ||
            priorShip.Fighters != currentShip.Fighters;
    }

    private sealed record LearningObjectiveSeed(string Goal, string Rationale);

    private sealed class ReviewState
    {
        private readonly object _sync = new();
        private string? _requestedReason;
        private IReadOnlyList<AutonomousScriptPlan> _plannedScripts = [];

        public SemaphoreSlim Gate { get; } = new(1, 1);
        public DateTimeOffset? LastReviewedAt { get; private set; }
        public GameSnapshot? LastReviewedSnapshot { get; private set; }

        public void RequestReview(string reason)
        {
            lock (_sync)
            {
                _requestedReason = reason;
            }
        }

        public string? GetRequestedReason()
        {
            lock (_sync)
            {
                return _requestedReason;
            }
        }

        public void SetPlannedScripts(IReadOnlyList<AutonomousScriptPlan> plannedScripts)
        {
            lock (_sync)
                _plannedScripts = plannedScripts.ToArray();
        }

        public IReadOnlyList<AutonomousScriptPlan> GetPlannedScripts()
        {
            lock (_sync)
                return _plannedScripts.ToArray();
        }

        public void ClearRequestedReason()
        {
            lock (_sync)
            {
                _requestedReason = null;
            }
        }

        public void RecordReview(GameSnapshot snapshot, DateTimeOffset reviewedAt)
        {
            LastReviewedAt = reviewedAt;
            LastReviewedSnapshot = snapshot;
        }
    }
}
