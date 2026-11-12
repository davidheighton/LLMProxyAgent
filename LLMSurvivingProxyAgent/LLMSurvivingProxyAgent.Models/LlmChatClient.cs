using System.Diagnostics;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;
using LLMSurvivingProxyAgent.Core;

namespace LLMSurvivingProxyAgent.Models;

public sealed record LlmChatMessage(string Role, string Content);

/// <summary>One structured-output chat request, independent of the model server.</summary>
public sealed record LlmChatRequest(
    string Purpose,
    IReadOnlyList<LlmChatMessage> Messages,
    JsonElement Schema)
{
    /// <summary>Request model reasoning. Null leaves the server default.</summary>
    public bool? Think { get; init; }

    public int? MaxTokens { get; init; }
    public double? Temperature { get; init; }
    public int? Seed { get; init; }
}

public sealed record LlmChatResult(
    string Content,
    string? Thinking,
    string? FinishReason,
    int? PromptTokens,
    int? CompletionTokens,
    TimeSpan Elapsed,
    string RawResponse)
{
    public double? TokensPerSecond =>
        CompletionTokens is > 0 and var tokens && Elapsed.TotalSeconds > 0
            ? Math.Round(tokens / Elapsed.TotalSeconds, 1)
            : null;
}

/// <summary>
/// Sends structured-output chat requests to Ollama (<c>/api/chat</c>) or llama.cpp's <c>llama-server</c>
/// (<c>/v1/chat/completions</c>), chosen by <see cref="ModelConfiguration.Provider"/>.
/// <para>
/// llama.cpp constrains the whole generation with the JSON-schema grammar, so a model cannot think and then answer
/// in one call. When reasoning is requested there, the client deliberates first: one unconstrained call with
/// thinking on, then a schema-constrained call (thinking off) that sees that reasoning. Ollama handles thinking
/// and the schema together in one call.
/// </para>
/// </summary>
public sealed class LlmChatClient(HttpClient httpClient)
{
    private const int MaximumDeliberationCharacters = 12_000;

    private static readonly JsonSerializerOptions RequestJsonOptions = new(JsonSerializerDefaults.Web)
    {
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
    };

    private static readonly Regex ThinkBlock = new(
        @"^\s*<think>.*?</think>\s*",
        RegexOptions.Singleline | RegexOptions.CultureInvariant);

    public async Task<LlmChatResult> ChatAsync(
        ModelConfiguration configuration,
        LlmChatRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(configuration);
        ArgumentNullException.ThrowIfNull(request);
        ArgumentException.ThrowIfNullOrWhiteSpace(configuration.Model);
        // The request decides first; otherwise the brain's configured thinking/token settings apply.
        request = request with
        {
            Think = request.Think ?? configuration.Think,
            MaxTokens = request.MaxTokens ?? configuration.MaxTokens
        };
        return LlmBackends.Normalize(configuration.Provider) switch
        {
            LlmBackends.Ollama => await SendOllamaAsync(configuration, request, cancellationToken).ConfigureAwait(false),
            LlmBackends.LlamaCpp => request.Think == true
                ? await DeliberateLlamaCppAsync(configuration, request, cancellationToken).ConfigureAwait(false)
                // Under a JSON-schema grammar the model cannot emit a reasoning block anyway; say so explicitly so
                // reasoning templates (e.g. Qwen3) do not open one.
                : await SendLlamaCppAsync(configuration, request, constrain: true, think: false, cancellationToken)
                    .ConfigureAwait(false),
            _ => throw new ArgumentException($"Provider '{configuration.Provider}' is not supported.", nameof(configuration))
        };
    }

    private async Task<LlmChatResult> SendOllamaAsync(
        ModelConfiguration configuration,
        LlmChatRequest request,
        CancellationToken cancellationToken)
    {
        var endpoint = new Uri(configuration.Endpoint.ToString().TrimEnd('/') + "/api/chat");
        var options = request.MaxTokens is null && request.Temperature is null && request.Seed is null
            ? null
            : new OllamaOptions(request.MaxTokens, request.Temperature, request.Seed);
        var body = new OllamaChatRequest(
            configuration.Model,
            false,
            request.Schema,
            request.Messages,
            request.Think,
            options);
        var stopwatch = Stopwatch.StartNew();
        string responseBody = await PostAsync(endpoint, body, LlmBackends.Ollama, request.Purpose, cancellationToken)
            .ConfigureAwait(false);
        stopwatch.Stop();

        OllamaChatResponse? payload;
        try
        {
            payload = JsonSerializer.Deserialize<OllamaChatResponse>(responseBody, RequestJsonOptions);
        }
        catch (JsonException exception)
        {
            throw new InvalidDataException($"Ollama returned a malformed response for {request.Purpose}.", exception);
        }

        return new LlmChatResult(
            payload?.Message?.Content ?? string.Empty,
            payload?.Message?.Thinking,
            payload?.DoneReason,
            payload?.PromptEvalCount,
            payload?.EvalCount,
            stopwatch.Elapsed,
            responseBody);
    }

    private async Task<LlmChatResult> DeliberateLlamaCppAsync(
        ModelConfiguration configuration,
        LlmChatRequest request,
        CancellationToken cancellationToken)
    {
        LlmChatRequest thinking = request with
        {
            Messages =
            [
                .. request.Messages,
                new LlmChatMessage(
                    "user",
                    "Before answering, reason step by step about this decision in plain prose: weigh the evidence, " +
                    "the alternatives, and what happens over the next several moves. Do not write the JSON answer yet.")
            ]
        };
        LlmChatResult deliberation = await SendLlamaCppAsync(
                configuration,
                thinking,
                constrain: false,
                think: true,
                cancellationToken)
            .ConfigureAwait(false);

        string reasoning = string.Join(
            Environment.NewLine,
            new[] { deliberation.Thinking, deliberation.Content }.Where(text => !string.IsNullOrWhiteSpace(text)));
        if (reasoning.Length > MaximumDeliberationCharacters)
        {
            reasoning = reasoning[^MaximumDeliberationCharacters..];
        }

        LlmChatRequest answer = request with
        {
            Messages =
            [
                .. thinking.Messages,
                new LlmChatMessage("assistant", reasoning),
                new LlmChatMessage("user", "Now return only the JSON object that matches the required schema.")
            ]
        };
        LlmChatResult final = await SendLlamaCppAsync(configuration, answer, constrain: true, think: false, cancellationToken)
            .ConfigureAwait(false);
        return final with
        {
            Thinking = reasoning,
            PromptTokens = Sum(deliberation.PromptTokens, final.PromptTokens),
            CompletionTokens = Sum(deliberation.CompletionTokens, final.CompletionTokens),
            Elapsed = deliberation.Elapsed + final.Elapsed,
            RawResponse = deliberation.RawResponse + Environment.NewLine + final.RawResponse
        };
    }

    private async Task<LlmChatResult> SendLlamaCppAsync(
        ModelConfiguration configuration,
        LlmChatRequest request,
        bool constrain,
        bool? think,
        CancellationToken cancellationToken)
    {
        var endpoint = new Uri(configuration.Endpoint.ToString().TrimEnd('/') + "/v1/chat/completions");
        var body = new JsonObject
        {
            ["model"] = configuration.Model,
            ["stream"] = false,
            ["messages"] = JsonSerializer.SerializeToNode(request.Messages, RequestJsonOptions)
        };
        if (request.MaxTokens is { } maxTokens)
        {
            body["max_tokens"] = maxTokens;
        }
        if (request.Temperature is { } temperature)
        {
            body["temperature"] = temperature;
        }
        if (request.Seed is { } seed)
        {
            body["seed"] = seed;
        }
        if (constrain)
        {
            body["response_format"] = new JsonObject
            {
                ["type"] = "json_schema",
                ["json_schema"] = new JsonObject
                {
                    ["name"] = "response",
                    ["strict"] = true,
                    ["schema"] = JsonNode.Parse(request.Schema.GetRawText())
                }
            };
        }
        if (think is { } enableThinking)
        {
            // Honoured by templates that support it (Qwen3 and similar) when llama-server runs with --jinja.
            body["chat_template_kwargs"] = new JsonObject { ["enable_thinking"] = enableThinking };
        }

        var stopwatch = Stopwatch.StartNew();
        string responseBody = await PostAsync(endpoint, body, "llama.cpp", request.Purpose, cancellationToken)
            .ConfigureAwait(false);
        stopwatch.Stop();

        OpenAiChatResponse? payload;
        try
        {
            payload = JsonSerializer.Deserialize<OpenAiChatResponse>(responseBody, RequestJsonOptions);
        }
        catch (JsonException exception)
        {
            throw new InvalidDataException($"llama.cpp returned a malformed response for {request.Purpose}.", exception);
        }

        OpenAiChoice? choice = payload?.Choices?.FirstOrDefault();
        string content = choice?.Message?.Content ?? string.Empty;
        string? thinking = choice?.Message?.ReasoningContent;
        Match inlineThinking = ThinkBlock.Match(content);
        if (inlineThinking.Success)
        {
            // Without --reasoning-format the reasoning stays inline in the content.
            thinking ??= inlineThinking.Value.Trim();
            content = content[inlineThinking.Length..];
        }

        return new LlmChatResult(
            content.Trim(),
            thinking,
            choice?.FinishReason,
            payload?.Usage?.PromptTokens,
            payload?.Usage?.CompletionTokens,
            stopwatch.Elapsed,
            responseBody);
    }

    private async Task<string> PostAsync(
        Uri endpoint,
        object body,
        string backendName,
        string purpose,
        CancellationToken cancellationToken)
    {
        using HttpResponseMessage response = await httpClient
            .PostAsJsonAsync(endpoint, body, RequestJsonOptions, cancellationToken)
            .ConfigureAwait(false);
        string responseBody = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
        if (!response.IsSuccessStatusCode)
        {
            string errorBody = responseBody.Length <= 1000 ? responseBody : responseBody[..1000];
            throw new HttpRequestException(
                $"{backendName} returned HTTP {(int)response.StatusCode} ({response.StatusCode}) for {purpose}: {errorBody}",
                null,
                response.StatusCode);
        }
        return responseBody;
    }

    private static int? Sum(int? first, int? second) =>
        first is null && second is null ? null : (first ?? 0) + (second ?? 0);

    private sealed record OllamaChatRequest(
        string Model,
        bool Stream,
        JsonElement Format,
        IReadOnlyList<LlmChatMessage> Messages,
        bool? Think,
        OllamaOptions? Options);

    private sealed record OllamaOptions(
        [property: JsonPropertyName("num_predict")] int? NumPredict,
        double? Temperature,
        int? Seed);

    private sealed record OllamaMessage(string? Role, string? Content, string? Thinking);

    private sealed record OllamaChatResponse(
        OllamaMessage? Message,
        [property: JsonPropertyName("done_reason")] string? DoneReason,
        [property: JsonPropertyName("eval_count")] int? EvalCount,
        [property: JsonPropertyName("prompt_eval_count")] int? PromptEvalCount);

    private sealed record OpenAiChatResponse(
        IReadOnlyList<OpenAiChoice>? Choices,
        OpenAiUsage? Usage);

    private sealed record OpenAiChoice(
        OpenAiMessage? Message,
        [property: JsonPropertyName("finish_reason")] string? FinishReason);

    private sealed record OpenAiMessage(
        string? Role,
        string? Content,
        [property: JsonPropertyName("reasoning_content")] string? ReasoningContent);

    private sealed record OpenAiUsage(
        [property: JsonPropertyName("prompt_tokens")] int? PromptTokens,
        [property: JsonPropertyName("completion_tokens")] int? CompletionTokens);
}
