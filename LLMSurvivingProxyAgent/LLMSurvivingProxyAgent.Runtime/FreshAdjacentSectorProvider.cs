using LLMSurvivingProxyAgent.Core;

namespace LLMSurvivingProxyAgent.Runtime;

public sealed class FreshAdjacentSectorProvider(IWorldStateStore worldStateStore)
    : IAutonomousMoveTargetProvider
{
    private static readonly TimeSpan MaximumWarpDataAge = TimeSpan.FromMinutes(15);

    public async Task<IReadOnlyList<int>> GetFreshAdjacentSectorIdsAsync(
        int? currentSector,
        CancellationToken cancellationToken = default)
    {
        if (currentSector is not > 0)
        {
            return [];
        }

        var sector = await worldStateStore.GetSectorAsync(currentSector.Value, cancellationToken)
            .ConfigureAwait(false);
        if (sector is null ||
            !sector.WarpDataKnown ||
            sector.ObservedAt is not { } observedAt ||
            observedAt > DateTimeOffset.UtcNow ||
            DateTimeOffset.UtcNow - observedAt > MaximumWarpDataAge)
        {
            return [];
        }

        return sector.Warps
            .Where(destination => destination > 0 && destination != currentSector.Value)
            .Distinct()
            .Order()
            .ToArray();
    }

    public async Task<IReadOnlyList<AdjacentSectorKnowledge>> GetSectorKnowledgeAsync(
        IReadOnlyList<int> sectorIds,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(sectorIds);

        var knowledge = new List<AdjacentSectorKnowledge>(sectorIds.Count);
        foreach (var sectorId in sectorIds.Distinct())
        {
            cancellationToken.ThrowIfCancellationRequested();
            var sector = await worldStateStore.GetSectorAsync(sectorId, cancellationToken)
                .ConfigureAwait(false);
            knowledge.Add(sector is null
                ? new AdjacentSectorKnowledge(sectorId, ExplorationStatus.Unknown, false, false)
                : new AdjacentSectorKnowledge(
                    sector.Id,
                    sector.Exploration,
                    sector.WarpDataKnown,
                    sector.Port is not null));
        }

        return knowledge;
    }
}
