using LLMSurvivingProxyAgent.Core;
using LLMSurvivingProxyAgent.Runtime;

namespace LLMSurvivingProxyAgent.TradeWars.Tests;

public sealed class ExplorationCandidateTests
{
    [Fact]
    public void PrefersNeverVisitedUnexploredSectorsAndSkipsHazards()
    {
        IReadOnlyList<int> candidates = AutonomousTestWorker.SelectExplorationCandidates(
            [99, 818, 1430, 1727, 4618],
            [
                new AdjacentSectorKnowledge(818, ExplorationStatus.Explored, true, true),
                new AdjacentSectorKnowledge(1430, ExplorationStatus.Unknown, false, false)
            ],
            previousSector: null,
            visitedSectors: new HashSet<int> { 1727 });

        Assert.Equal([1430, 4618], candidates);
    }

    [Fact]
    public void AvoidsBacktrackingUnlessItIsTheOnlyExit()
    {
        Assert.Equal(
            [20],
            AutonomousTestWorker.SelectExplorationCandidates([10, 20], [], previousSector: 10, new HashSet<int>()));
        Assert.Equal(
            [10],
            AutonomousTestWorker.SelectExplorationCandidates([10], [], previousSector: 10, new HashSet<int> { 10 }));
    }

    [Fact]
    public void FallsBackToVisitedSectorsWhenEverythingNearbyIsKnown()
    {
        Assert.Equal(
            [5, 7],
            AutonomousTestWorker.SelectExplorationCandidates([7, 5], [], previousSector: null, new HashSet<int> { 5, 7 }));
        Assert.Empty(AutonomousTestWorker.SelectExplorationCandidates([99], [], null, new HashSet<int>()));
    }

    [Fact]
    public void DetectsThreeRoundTripsBetweenTwoSectorsOnly()
    {
        Assert.True(AutonomousTestWorker.IsOscillating([4180, 668, 4180, 668, 4180, 668]));
        Assert.True(AutonomousTestWorker.IsOscillating([12, 4180, 668, 4180, 668, 4180, 668]));
        Assert.False(AutonomousTestWorker.IsOscillating([668, 4180, 668, 4180, 668]));
        Assert.False(AutonomousTestWorker.IsOscillating([4180, 668, 4180, 12, 4180, 668]));
        Assert.False(AutonomousTestWorker.IsOscillating([1, 2, 3, 4, 5, 6]));
    }
}
