using System.Globalization;
using LLMSurvivingProxyAgent.Core;
using Microsoft.Extensions.Configuration;

namespace LLMSurvivingProxyAgent.Runtime;

/// <summary>
/// Resolves each brain's model server from configuration, so the Pilot can run a very small, fast model while the
/// Strategic brain runs a larger one that deliberates.
/// <code>
/// Brains:{Pilot|Strategic|Reactive}:Backend    Ollama (default) | LlamaCpp
/// Brains:{brain}:Endpoint                      defaults: Ollama Reasoning:OllamaEndpoint (http://localhost:11434/);
///                                              LlamaCpp Reasoning:LlamaCpp:{brain}Endpoint, else
///                                              Pilot/Reactive http://127.0.0.1:8081/, Strategic http://127.0.0.1:8082/
/// Brains:{brain}:Model                         model name (an Ollama tag, or the llama-server --alias)
/// Brains:{brain}:Thinking                      true/false; default: Strategic on llama.cpp deliberates, others unset
/// Brains:{brain}:MaxTokens                     generation cap; default: Strategic on llama.cpp 4096, others unset
/// </code>
/// <c>Reasoning:Backend</c> sets the default backend for every brain.
/// </summary>
/// <summary>The resolved model server for each brain in this host.</summary>
public sealed record BrainModels(ModelConfiguration Pilot, ModelConfiguration Strategic, ModelConfiguration Reactive);

public static class BrainConfiguration
{
    public const string Pilot = "Pilot";
    public const string Strategic = "Strategic";
    public const string Reactive = "Reactive";

    public const string DefaultOllamaEndpoint = "http://localhost:11434/";
    public const string DefaultLlamaCppPilotEndpoint = "http://127.0.0.1:8081/";
    public const string DefaultLlamaCppStrategicEndpoint = "http://127.0.0.1:8082/";
    public const int DefaultStrategicDeliberationTokens = 4096;

    public static ModelConfiguration Resolve(IConfiguration configuration, string brain, string defaultModel)
    {
        ArgumentNullException.ThrowIfNull(configuration);
        ArgumentException.ThrowIfNullOrWhiteSpace(brain);
        string section = $"Brains:{brain}";

        string backendText = configuration[$"{section}:Backend"] ?? configuration["Reasoning:Backend"] ?? LlmBackends.Ollama;
        string backend = LlmBackends.Normalize(backendText)
            ?? throw new InvalidOperationException(
                $"{section}:Backend '{backendText}' is not supported; use {LlmBackends.Ollama} or {LlmBackends.LlamaCpp}.");

        string endpointText = configuration[$"{section}:Endpoint"] ?? (backend == LlmBackends.Ollama
            ? configuration["Reasoning:OllamaEndpoint"] ?? DefaultOllamaEndpoint
            : configuration[$"Reasoning:LlamaCpp:{brain}Endpoint"] ??
              (brain == Strategic ? DefaultLlamaCppStrategicEndpoint : DefaultLlamaCppPilotEndpoint));
        if (!Uri.TryCreate(endpointText, UriKind.Absolute, out Uri? endpoint) ||
            (endpoint.Scheme != Uri.UriSchemeHttp && endpoint.Scheme != Uri.UriSchemeHttps))
        {
            throw new InvalidOperationException($"The {brain} model endpoint must be an absolute HTTP or HTTPS URL.");
        }

        string model = configuration[$"{section}:Model"] is { Length: > 0 } configuredModel
            ? configuredModel.Trim()
            : defaultModel;

        bool? think = configuration[$"{section}:Thinking"] is { Length: > 0 } thinkingText
            ? bool.TryParse(thinkingText, out bool parsed)
                ? parsed
                : throw new InvalidOperationException($"{section}:Thinking must be true or false.")
            : brain == Strategic && backend == LlmBackends.LlamaCpp ? true : null;

        int? maxTokens = configuration[$"{section}:MaxTokens"] is { Length: > 0 } maxTokensText
            ? int.TryParse(maxTokensText, NumberStyles.Integer, CultureInfo.InvariantCulture, out int tokens) && tokens > 0
                ? tokens
                : throw new InvalidOperationException($"{section}:MaxTokens must be a positive integer.")
            : brain == Strategic && backend == LlmBackends.LlamaCpp ? DefaultStrategicDeliberationTokens : null;

        return new ModelConfiguration(backend, model, endpoint)
        {
            Think = think,
            MaxTokens = maxTokens
        };
    }

    public static string Describe(ModelConfiguration configuration) =>
        $"{configuration.Provider} {configuration.Model} @ {configuration.Endpoint}" +
        (configuration.Think is { } think ? $" (thinking {(think ? "on" : "off")})" : string.Empty);
}
