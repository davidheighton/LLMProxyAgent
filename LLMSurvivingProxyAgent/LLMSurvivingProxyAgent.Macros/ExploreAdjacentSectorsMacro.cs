using LLMSurvivingProxyAgent.Core;

namespace LLMSurvivingProxyAgent.Macros;

public sealed class ExploreAdjacentSectorsMacro : IMacro
{
    public MacroDescriptor Descriptor { get; } = new(
        "explore-adjacent-sectors",
        "Explore Adjacent Sectors",
        "List known outgoing warp destinations that have not been fully explored.",
        MacroCategory.Exploration,
        ["Current sector warp data is known"],
        "A deterministic list of adjacent sectors that remain unexplored.");

    public async Task<MacroCheck> CanExecuteAsync(
        MacroContext context,
        CancellationToken cancellationToken = default)
    {
        if (context.CurrentSector is not int currentSector)
        {
            return new MacroCheck(false, "A current sector is required.");
        }

        var sector = await context.WorldStateStore.GetSectorAsync(currentSector, cancellationToken);
        return sector is { WarpDataKnown: true }
            ? new MacroCheck(true, string.Empty)
            : new MacroCheck(false, $"Warp data for sector {currentSector} is not known.");
    }

    public async Task<string> EstimateOutcomeAsync(
        MacroContext context,
        CancellationToken cancellationToken = default)
    {
        var sectors = await context.NavigationService.FindUnexploredAdjacentSectorsAsync(
            context.CurrentSector!.Value,
            cancellationToken);
        return $"Estimate: {sectors.Count} adjacent sector(s) are not fully explored.";
    }

    public async Task<MacroResult> ExecuteAsync(
        MacroContext context,
        CancellationToken cancellationToken = default)
    {
        var sectors = await context.NavigationService.FindUnexploredAdjacentSectorsAsync(
            context.CurrentSector!.Value,
            cancellationToken);
        var ids = sectors.Select(sector => sector.Id).ToArray();
        return new MacroResult(
            ids.Length == 0 ? MacroExecutionStatus.Succeeded : MacroExecutionStatus.Planned,
            ids.Length == 0
                ? $"No unexplored outgoing neighbors from sector {context.CurrentSector}."
                : $"Exploration candidates from sector {context.CurrentSector}: {string.Join(", ", ids)}",
            "Candidate discovery only; no movement or scan commands were sent.",
            ids);
    }
}
