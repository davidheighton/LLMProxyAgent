namespace LLMSurvivingProxyAgent.Core;

/// <summary>
/// Local model servers a brain can use. <see cref="ModelConfiguration.Provider"/> names one of these.
/// </summary>
public static class LlmBackends
{
    /// <summary>Ollama's native <c>/api/chat</c> API.</summary>
    public const string Ollama = "Ollama";

    /// <summary>llama.cpp's <c>llama-server</c>, through its OpenAI-compatible <c>/v1/chat/completions</c> API.</summary>
    public const string LlamaCpp = "LlamaCpp";

    public static bool IsSupported(string? provider) => Normalize(provider) is not null;

    /// <summary>Returns the canonical backend name, or null for an unknown provider.</summary>
    public static string? Normalize(string? provider) =>
        provider?.Trim().ToLowerInvariant() switch
        {
            "ollama" => Ollama,
            "llamacpp" or "llama.cpp" or "llama-cpp" or "llama-server" or "llamaserver" => LlamaCpp,
            _ => null
        };

    public static bool IsLlamaCpp(ModelConfiguration configuration) =>
        Normalize(configuration?.Provider) == LlamaCpp;
}
