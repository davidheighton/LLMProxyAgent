namespace LLMSurvivingProxyAgent.Core;

public sealed record ModelConfiguration(
    string Provider,
    string Model,
    Uri Endpoint)
{
    /// <summary>Request model reasoning before answering. Null keeps each brain's default.</summary>
    public bool? Think { get; init; }

    /// <summary>Upper bound on generated tokens. Null keeps each brain's default.</summary>
    public int? MaxTokens { get; init; }
}

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
