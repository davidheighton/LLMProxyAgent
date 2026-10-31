using LLMSurvivingProxyAgent.Core;

namespace LLMSurvivingProxyAgent.Macros;

public sealed class RefreshCimMacro : IMacro
{
    public MacroDescriptor Descriptor { get; } = new(
        "refresh-cim",
        "Refresh CIM",
        "Request updated port and warp information from a connected game session.",
        MacroCategory.Exploration,
        ["An authorized live TradeWars session is connected"],
        "Updated port and warp observations imported into the world database.");

    public Task<MacroCheck> CanExecuteAsync(
        MacroContext context,
        CancellationToken cancellationToken = default) =>
        Task.FromResult(new MacroCheck(
            false,
            "Live CIM input is not configured; no connection or game command will be attempted."));

    public Task<string> EstimateOutcomeAsync(
        MacroContext context,
        CancellationToken cancellationToken = default) =>
        Task.FromResult("A live CIM batch could refresh port profiles and warp observations.");

    public Task<MacroResult> ExecuteAsync(
        MacroContext context,
        CancellationToken cancellationToken = default) =>
        Task.FromResult(new MacroResult(
            MacroExecutionStatus.Blocked,
            "CIM refresh requires a connected game session.",
            "Submit verified CIM lines through the offline CIM importer instead.",
            []));
}
