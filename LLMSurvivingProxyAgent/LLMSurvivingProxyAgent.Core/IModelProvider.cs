namespace LLMSurvivingProxyAgent.Core;

public sealed record ModelConfiguration(
    string Provider,
    string Model,
    Uri Endpoint);

public interface IModelProvider
{
    string Id { get; }
    Task<IReadOnlyList<string>> DiscoverModelsAsync(
        ModelConfiguration configuration,
        CancellationToken cancellationToken = default);
    Task<MacroSelection> SelectMacroAsync(
        ModelConfiguration configuration,
        GameSnapshot snapshot,
        IReadOnlyList<MacroDescriptor> allowedMacros,
        CancellationToken cancellationToken = default);
}
