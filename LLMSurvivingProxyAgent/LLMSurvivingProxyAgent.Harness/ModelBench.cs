using System.Globalization;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using LLMSurvivingProxyAgent.Core;
using LLMSurvivingProxyAgent.Models;
using LLMSurvivingProxyAgent.Runtime;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Configuration;

namespace LLMSurvivingProxyAgent.Harness;

/// <summary>What a bench run replays and how answers are judged, per brain.</summary>
public sealed record BenchProfile(string Brain, string Role, string DecisionField, IReadOnlySet<string>? ValidDecisions, string Schema)
{
    public static BenchProfile Pilot { get; } = new(
        BrainConfiguration.Pilot,
        "Pilot",
        "action",
        Enum.GetNames<AutonomousTestAction>().Select(JsonNamingPolicy.CamelCase.ConvertName).ToHashSet(StringComparer.OrdinalIgnoreCase),
        """
        {"type":"object","properties":{"action":{"type":"string"},"targetSector":{"type":"integer"},
        "rationale":{"type":"string"},"citedKnowledgeNodeId":{"type":"string"}},
        "required":["action","targetSector","rationale","citedKnowledgeNodeId"]}
        """);

    public static BenchProfile Strategic { get; } = new(
        BrainConfiguration.Strategic,
        "Strategic Review",
        "objectiveDecision",
        new HashSet<string>(["continue", "selectProposal", "proposeAndSelect", "complete", "hold"], StringComparer.OrdinalIgnoreCase),
        """
        {"type":"object","properties":{"summary":{"type":"string"},"recommendation":{"type":"string"},
        "objectiveDecision":{"type":"string","enum":["continue","selectProposal","proposeAndSelect","complete","hold"]}},
        "required":["summary","recommendation","objectiveDecision"]}
        """);

    public static BenchProfile For(string brain) => brain.Trim().ToLowerInvariant() switch
    {
        "pilot" => Pilot,
        "strategic" or "strategy" => Strategic,
        _ => throw new ArgumentException("--brain must be pilot or strategic.")
    };
}

/// <summary>One recorded decision prompt and the decision that was made with it at the time.</summary>
public sealed record BenchCase(string InteractionId, string RecordedModel, IReadOnlyList<LlmChatMessage> Messages, string? ReferenceDecision)
{
    /// <summary>The exact schema used live, when it was recorded; otherwise the profile's generic schema is used.</summary>
    public string? RecordedSchema { get; init; }
}

public sealed record BenchModelSpec(string Label, ModelConfiguration Configuration)
{
    /// <summary>
    /// Parses <c>[backend:]model[@endpoint]</c>, e.g. <c>llamacpp:qwen3-0.6b@http://127.0.0.1:8081/</c> or
    /// <c>ollama:qwen3:1.7b</c>. Without a backend prefix the model is an Ollama tag. Endpoints default as in
    /// <see cref="BrainConfiguration"/>; <c>think</c> follows the brain's default unless <c>+think</c>/<c>-think</c>
    /// is appended to the model.
    /// </summary>
    public static BenchModelSpec Parse(string text, string brain)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(text);
        string spec = text.Trim();
        string? endpointText = null;
        int at = spec.LastIndexOf('@');
        if (at > 0)
        {
            endpointText = spec[(at + 1)..];
            spec = spec[..at];
        }

        string backend = LlmBackends.Ollama;
        int colon = spec.IndexOf(':');
        if (colon > 0 && LlmBackends.Normalize(spec[..colon]) is { } prefixed)
        {
            backend = prefixed;
            spec = spec[(colon + 1)..];
        }

        bool? think = null;
        if (spec.EndsWith("+think", StringComparison.OrdinalIgnoreCase))
        {
            think = true;
            spec = spec[..^"+think".Length];
        }
        else if (spec.EndsWith("-think", StringComparison.OrdinalIgnoreCase))
        {
            think = false;
            spec = spec[..^"-think".Length];
        }
        if (spec.Length == 0)
        {
            throw new ArgumentException($"Model spec '{text}' names no model.");
        }

        var settings = new Dictionary<string, string?>
        {
            [$"Brains:{brain}:Backend"] = backend,
            [$"Brains:{brain}:Model"] = spec,
            [$"Brains:{brain}:Endpoint"] = endpointText,
            [$"Brains:{brain}:Thinking"] = think?.ToString(),
            [$"Brains:{brain}:Temperature"] = null
        };
        ModelConfiguration configuration = BrainConfiguration.Resolve(
            new ConfigurationBuilder().AddInMemoryCollection(settings).Build(),
            brain,
            spec);
        if (!configuration.Endpoint.IsLoopback)
        {
            throw new ArgumentException($"Model spec '{text}' must use a loopback endpoint; recorded game prompts stay local.");
        }
        return new BenchModelSpec(text.Trim(), configuration);
    }
}

public sealed record BenchResult(
    string Label,
    int Cases,
    int Valid,
    int Comparable,
    int Agreed,
    double MedianMilliseconds,
    double P95Milliseconds,
    double? MeanCompletionTokens,
    double? TokensPerSecond,
    IReadOnlyList<string> Errors)
{
    public double ValidRate => Cases == 0 ? 0 : (double)Valid / Cases;
    public double? AgreementRate => Comparable == 0 ? null : (double)Agreed / Comparable;
}

/// <summary>
/// Replays recorded decision prompts against candidate models to compare speed and answer quality on real game
/// situations. Agreement with the recorded decision is a consistency signal, not ground truth: the recorded model
/// may itself have been wrong.
/// </summary>
public sealed class ModelBench(LlmChatClient client)
{
    private static readonly Regex RoleHeader = new(@"^\[(system|user|assistant)\]\r?$", RegexOptions.Multiline | RegexOptions.CultureInvariant);

    public static IReadOnlyList<BenchCase> LoadCases(string connectionString, BenchProfile profile, int limit, string? agentId = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(connectionString);
        ArgumentNullException.ThrowIfNull(profile);
        using var connection = new SqliteConnection(connectionString);
        connection.Open();
        using var command = connection.CreateCommand();
        command.CommandText = """
            SELECT interaction_id, model, prompt, response, request_schema FROM llm_interactions
            WHERE role = $role AND succeeded = 1 AND in_progress = 0 AND attempt = 1
              AND ($agentId IS NULL OR agent_id = $agentId)
            ORDER BY completed_at DESC
            LIMIT $limit;
            """;
        command.Parameters.AddWithValue("$role", profile.Role);
        command.Parameters.AddWithValue("$agentId", (object?)agentId ?? DBNull.Value);
        command.Parameters.AddWithValue("$limit", limit);
        var cases = new List<BenchCase>();
        try
        {
            using SqliteDataReader reader = command.ExecuteReader();
            while (reader.Read())
            {
                IReadOnlyList<LlmChatMessage> messages = ParsePrompt(reader.GetString(2));
                if (messages.Count == 0)
                {
                    continue;
                }
                cases.Add(new BenchCase(
                    reader.GetString(0),
                    reader.GetString(1),
                    messages,
                    ExtractDecision(ExtractContent(reader.GetString(3)), profile.DecisionField))
                {
                    RecordedSchema = reader.IsDBNull(4) ? null : reader.GetString(4)
                });
            }
        }
        catch (SqliteException exception) when (exception.SqliteErrorCode == 1)
        {
            throw new InvalidOperationException("No LLM interaction history was found; play some games first.", exception);
        }
        cases.Reverse();
        return cases;
    }

    /// <summary>Splits a recorded prompt (<c>[role]</c> header lines followed by content) back into messages.</summary>
    public static IReadOnlyList<LlmChatMessage> ParsePrompt(string prompt)
    {
        MatchCollection headers = RoleHeader.Matches(prompt ?? string.Empty);
        var messages = new List<LlmChatMessage>(headers.Count);
        for (int index = 0; index < headers.Count; index++)
        {
            Match header = headers[index];
            int start = header.Index + header.Length;
            int end = index + 1 < headers.Count ? headers[index + 1].Index : prompt!.Length;
            messages.Add(new LlmChatMessage(header.Groups[1].Value, prompt![start..end].Trim('\r', '\n')));
        }
        return messages;
    }

    /// <summary>Gets the model's answer text from a raw Ollama or OpenAI-style response (the last JSON document wins).</summary>
    public static string? ExtractContent(string rawResponse)
    {
        foreach (string line in (rawResponse ?? string.Empty)
                     .Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                     .Reverse()
                     .Prepend(rawResponse?.Trim() ?? string.Empty))
        {
            try
            {
                using JsonDocument document = JsonDocument.Parse(line);
                JsonElement root = document.RootElement;
                if (root.TryGetProperty("message", out JsonElement message) &&
                    message.TryGetProperty("content", out JsonElement content))
                {
                    return content.GetString();
                }
                if (root.TryGetProperty("choices", out JsonElement choices) &&
                    choices.ValueKind == JsonValueKind.Array &&
                    choices.GetArrayLength() > 0 &&
                    choices[0].TryGetProperty("message", out JsonElement choiceMessage) &&
                    choiceMessage.TryGetProperty("content", out JsonElement choiceContent))
                {
                    return choiceContent.GetString();
                }
            }
            catch (JsonException)
            {
            }
        }
        return null;
    }

    public static string? ExtractDecision(string? content, string field)
    {
        if (string.IsNullOrWhiteSpace(content))
        {
            return null;
        }
        try
        {
            using JsonDocument document = JsonDocument.Parse(content);
            return document.RootElement.ValueKind == JsonValueKind.Object &&
                   document.RootElement.TryGetProperty(field, out JsonElement value) &&
                   value.ValueKind == JsonValueKind.String
                ? value.GetString()
                : null;
        }
        catch (JsonException)
        {
            return null;
        }
    }

    public async Task<BenchResult> RunAsync(
        BenchModelSpec model,
        BenchProfile profile,
        IReadOnlyList<BenchCase> cases,
        Action<string>? progress = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(model);
        ArgumentNullException.ThrowIfNull(profile);
        ArgumentNullException.ThrowIfNull(cases);
        using JsonDocument genericSchema = JsonDocument.Parse(profile.Schema);
        var latencies = new List<double>();
        var tokens = new List<int>();
        var errors = new List<string>();
        double generationSeconds = 0;
        int valid = 0;
        int comparable = 0;
        int agreed = 0;

        // One untimed warm-up call so model loading does not count against the first case.
        if (cases.Count > 0)
        {
            try
            {
                await client.ChatAsync(model.Configuration, Request(cases[0], genericSchema.RootElement, profile, model), cancellationToken)
                    .ConfigureAwait(false);
            }
            catch (Exception exception) when (exception is HttpRequestException or InvalidDataException or TaskCanceledException)
            {
                errors.Add($"warm-up: {exception.Message}");
            }
        }

        for (int index = 0; index < cases.Count; index++)
        {
            BenchCase benchCase = cases[index];
            try
            {
                LlmChatResult result = await client
                    .ChatAsync(model.Configuration, Request(benchCase, genericSchema.RootElement, profile, model), cancellationToken)
                    .ConfigureAwait(false);
                latencies.Add(result.Elapsed.TotalMilliseconds);
                if (result.CompletionTokens is { } completion)
                {
                    tokens.Add(completion);
                    generationSeconds += result.Elapsed.TotalSeconds;
                }

                string? decision = ExtractDecision(result.Content, profile.DecisionField);
                if (decision is not null && (profile.ValidDecisions is null || profile.ValidDecisions.Contains(decision)))
                {
                    valid++;
                    if (benchCase.ReferenceDecision is { } reference)
                    {
                        comparable++;
                        agreed += string.Equals(reference, decision, StringComparison.OrdinalIgnoreCase) ? 1 : 0;
                    }
                }
                else if (errors.Count < 5)
                {
                    errors.Add($"case {index + 1}: unusable answer: {Truncate(result.Content, 160)}");
                }
            }
            catch (Exception exception) when (exception is HttpRequestException or InvalidDataException or TaskCanceledException)
            {
                if (errors.Count < 5)
                {
                    errors.Add($"case {index + 1}: {exception.Message}");
                }
            }
            progress?.Invoke($"{model.Label}: {index + 1}/{cases.Count}");
        }

        return new BenchResult(
            model.Label,
            cases.Count,
            valid,
            comparable,
            agreed,
            Percentile(latencies, 0.5),
            Percentile(latencies, 0.95),
            tokens.Count == 0 ? null : Math.Round(tokens.Average(), 1),
            generationSeconds > 0 ? Math.Round(tokens.Sum() / generationSeconds, 1) : null,
            errors);
    }

    public static string ToMarkdown(BenchProfile profile, IReadOnlyList<BenchCase> cases, IReadOnlyList<BenchResult> results)
    {
        var text = new StringBuilder();
        text.AppendLine(CultureInfo.InvariantCulture, $"# {profile.Brain} model bench");
        text.AppendLine();
        string recordedBy = string.Join(", ", cases.Select(item => item.RecordedModel).Distinct(StringComparer.OrdinalIgnoreCase));
        text.AppendLine(CultureInfo.InvariantCulture,
            $"{cases.Count} recorded {profile.Role} prompts (first attempts), replayed against each model. Agreement compares `{profile.DecisionField}` with the decision recorded at the time (by {recordedBy}); it measures consistency, not correctness.");
        int faithful = cases.Count(item => item.RecordedSchema is not null);
        text.AppendLine();
        text.AppendLine(CultureInfo.InvariantCulture,
            $"{faithful} of {cases.Count} prompts replay the exact schema used live; the rest use a generic schema, so their answers are less constrained than in play.");
        text.AppendLine();
        text.AppendLine("| Model | Valid answers | Agreement | Median latency | p95 latency | Mean output tokens | Tokens/s |");
        text.AppendLine("| --- | --- | --- | --- | --- | --- | --- |");
        foreach (BenchResult result in results.OrderBy(item => item.MedianMilliseconds))
        {
            string agreement = result.AgreementRate is { } rate
                ? string.Create(CultureInfo.InvariantCulture, $"{result.Agreed}/{result.Comparable} ({rate:P0})")
                : "—";
            string meanTokens = result.MeanCompletionTokens?.ToString("N1", CultureInfo.InvariantCulture) ?? "—";
            string tokensPerSecond = result.TokensPerSecond?.ToString("N1", CultureInfo.InvariantCulture) ?? "—";
            text.AppendLine(CultureInfo.InvariantCulture,
                $"| {result.Label} | {result.Valid}/{result.Cases} ({result.ValidRate:P0}) | {agreement} | {result.MedianMilliseconds:N0} ms | {result.P95Milliseconds:N0} ms | {meanTokens} | {tokensPerSecond} |");
        }
        foreach (BenchResult result in results.Where(item => item.Errors.Count > 0))
        {
            text.AppendLine();
            text.AppendLine(CultureInfo.InvariantCulture, $"**{result.Label}** problems:");
            foreach (string error in result.Errors)
            {
                text.AppendLine(CultureInfo.InvariantCulture, $"- {error}");
            }
        }
        return text.ToString();
    }

    private static LlmChatRequest Request(BenchCase benchCase, JsonElement genericSchema, BenchProfile profile, BenchModelSpec model) =>
        new(
            $"the {profile.Brain} bench",
            benchCase.Messages,
            benchCase.RecordedSchema is { } recorded ? JsonDocument.Parse(recorded).RootElement.Clone() : genericSchema)
        {
            Think = model.Configuration.Think,
            MaxTokens = model.Configuration.MaxTokens ?? (profile == BenchProfile.Pilot ? 2048 : null)
        };

    private static double Percentile(List<double> values, double percentile)
    {
        if (values.Count == 0)
        {
            return 0;
        }
        var sorted = values.Order().ToArray();
        int index = (int)Math.Ceiling(percentile * sorted.Length) - 1;
        return Math.Round(sorted[Math.Clamp(index, 0, sorted.Length - 1)], 1);
    }

    private static string Truncate(string? value, int length) =>
        string.IsNullOrEmpty(value) ? "(empty)" : value.Length <= length ? value : value[..length] + "...";
}
