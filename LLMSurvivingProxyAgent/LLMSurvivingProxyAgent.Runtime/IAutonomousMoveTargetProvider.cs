using LLMSurvivingProxyAgent.Core;

namespace LLMSurvivingProxyAgent.Runtime;

public interface IAutonomousMoveTargetProvider
{
    bool RequiresCurrentSectorDisplayBeforeMovement => false;

    Task<IReadOnlyList<int>> GetFreshAdjacentSectorIdsAsync(
        int? currentSector,
        CancellationToken cancellationToken = default);

    Task<IReadOnlyList<AdjacentSectorKnowledge>> GetSectorKnowledgeAsync(
        IReadOnlyList<int> sectorIds,
        CancellationToken cancellationToken = default);
}
