namespace LLMSurvivingProxyAgent.Runtime;

public sealed record ProxyGameEvent(
    DateTimeOffset Timestamp,
    string Kind,
    int? CurrentSector = null,
    string? PromptSurface = null,
    IReadOnlyDictionary<string, string>? Metadata = null);
