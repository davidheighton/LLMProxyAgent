using LLMSurvivingProxyAgent.Core;
using LLMSurvivingProxyAgent.TradeWars;

namespace LLMSurvivingProxyAgent.Macros;

public sealed record MacroContext(
    string AgentId,
    int? CurrentSector,
    IWorldStateStore WorldStateStore,
    NavigationService NavigationService);

public sealed record MacroCheck(bool CanExecute, string Notes);

public interface IMacro
{
    MacroDescriptor Descriptor { get; }

    Task<MacroCheck> CanExecuteAsync(
        MacroContext context,
        CancellationToken cancellationToken = default);

    Task<string> EstimateOutcomeAsync(
        MacroContext context,
        CancellationToken cancellationToken = default);

    Task<MacroResult> ExecuteAsync(
        MacroContext context,
        CancellationToken cancellationToken = default);
}
