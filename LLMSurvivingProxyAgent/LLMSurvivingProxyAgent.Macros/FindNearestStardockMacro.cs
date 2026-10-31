using LLMSurvivingProxyAgent.Core;

namespace LLMSurvivingProxyAgent.Macros;

public sealed class FindNearestStardockMacro : IMacro
{
    public MacroDescriptor Descriptor { get; } = new(
        "find-nearest-stardock",
        "Find Nearest StarDock",
        "Plan a shortest directed route from the current sector to the nearest known StarDock.",
        MacroCategory.Navigation,
        ["Current sector is known", "Universe warp data is available"],
        "A shortest-hop route to the nearest reachable StarDock.");

    public async Task<MacroCheck> CanExecuteAsync(
        MacroContext context,
        CancellationToken cancellationToken = default)
    {
        if (context.CurrentSector is not int currentSector)
        {
            return new MacroCheck(false, "A current sector is required to plan navigation.");
        }
        if (await context.WorldStateStore.GetUniverseAsync(cancellationToken) is null)
        {
            return new MacroCheck(false, "Universe metadata has not been loaded.");
        }
        return new MacroCheck(true, $"Planning from sector {currentSector}; no movement will be issued.");
    }

    public Task<string> EstimateOutcomeAsync(
        MacroContext context,
        CancellationToken cancellationToken = default) =>
        Task.FromResult("Estimate: shortest known directed route; this macro does not move the ship.");

    public async Task<MacroResult> ExecuteAsync(
        MacroContext context,
        CancellationToken cancellationToken = default)
    {
        var path = await context.NavigationService.FindNearestStarDockAsync(
            context.CurrentSector!.Value,
            cancellationToken: cancellationToken);
        return path is null
            ? new MacroResult(
                MacroExecutionStatus.Blocked,
                "No reachable StarDock is present in the known warp graph.",
                "No game commands were sent.",
                [])
            : new MacroResult(
                MacroExecutionStatus.Planned,
                $"Route found in {path.HopCount} hops: {string.Join(" -> ", path.Sectors)}",
                "Navigation plan only; no game commands were sent.",
                path.Sectors);
    }
}
