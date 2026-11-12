using System.Net;
using System.Text;
using System.Text.Json;
using LLMSurvivingProxyAgent.Core;
using LLMSurvivingProxyAgent.Harness;
using LLMSurvivingProxyAgent.Models;
using LLMSurvivingProxyAgent.Runtime;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Configuration;

namespace LLMSurvivingProxyAgent.TradeWars.Tests;

public sealed class LlmBackendTests
{
    private static readonly JsonElement Schema = JsonDocument.Parse(
        """{"type":"object","properties":{"action":{"type":"string"}},"required":["action"]}""").RootElement.Clone();

    private static ModelConfiguration LlamaCpp(string model = "pilot-small", bool? think = null) =>
        new(LlmBackends.LlamaCpp, model, new Uri("http://127.0.0.1:8081/")) { Think = think };

    [Fact]
    public async Task LlamaCppRequestUsesOpenAiSchemaFormatAndDisablesThinking()
    {
        var handler = new QueueHandler(OpenAiResponse("""{"action":"none"}""", reasoning: null, promptTokens: 120, completionTokens: 7));
        var client = new LlmChatClient(new HttpClient(handler));

        LlmChatResult result = await client.ChatAsync(
            LlamaCpp(),
            new LlmChatRequest("a test", [new("system", "s"), new("user", "u")], Schema) { MaxTokens = 256, Seed = 7 });

        Assert.Equal("http://127.0.0.1:8081/v1/chat/completions", handler.Requests[0].Uri);
        using JsonDocument request = JsonDocument.Parse(handler.Requests[0].Body);
        JsonElement root = request.RootElement;
        Assert.Equal("pilot-small", root.GetProperty("model").GetString());
        Assert.False(root.GetProperty("stream").GetBoolean());
        Assert.Equal(256, root.GetProperty("max_tokens").GetInt32());
        Assert.Equal(7, root.GetProperty("seed").GetInt32());
        Assert.Equal("json_schema", root.GetProperty("response_format").GetProperty("type").GetString());
        Assert.Equal(
            "object",
            root.GetProperty("response_format").GetProperty("json_schema").GetProperty("schema").GetProperty("type").GetString());
        Assert.False(root.GetProperty("chat_template_kwargs").GetProperty("enable_thinking").GetBoolean());
        Assert.Equal("system", root.GetProperty("messages")[0].GetProperty("role").GetString());
        Assert.Equal("""{"action":"none"}""", result.Content);
        Assert.Equal(120, result.PromptTokens);
        Assert.Equal(7, result.CompletionTokens);
        Assert.Equal("stop", result.FinishReason);
    }

    [Fact]
    public async Task LlamaCppStripsInlineThinkBlock()
    {
        var handler = new QueueHandler(OpenAiResponse("<think>\nweighing options\n</think>\n{\"action\":\"none\"}", null));
        LlmChatResult result = await new LlmChatClient(new HttpClient(handler))
            .ChatAsync(LlamaCpp(), new LlmChatRequest("a test", [new("user", "u")], Schema));

        Assert.Equal("""{"action":"none"}""", result.Content);
        Assert.Contains("weighing options", result.Thinking);
    }

    [Fact]
    public async Task LlamaCppDeliberatesThenAnswersUnderTheSchema()
    {
        var handler = new QueueHandler(
            OpenAiResponse("Plan: buy holds first, then trade.", reasoning: "Long-horizon reasoning.", completionTokens: 300),
            OpenAiResponse("""{"action":"continue"}""", null, completionTokens: 10));
        LlmChatResult result = await new LlmChatClient(new HttpClient(handler)).ChatAsync(
            LlamaCpp("strategist", think: true),
            new LlmChatRequest("strategy", [new("system", "s"), new("user", "u")], Schema) { MaxTokens = 4096 });

        Assert.Equal(2, handler.Requests.Count);
        using JsonDocument first = JsonDocument.Parse(handler.Requests[0].Body);
        Assert.False(first.RootElement.TryGetProperty("response_format", out _));
        Assert.True(first.RootElement.GetProperty("chat_template_kwargs").GetProperty("enable_thinking").GetBoolean());
        using JsonDocument second = JsonDocument.Parse(handler.Requests[1].Body);
        Assert.True(second.RootElement.TryGetProperty("response_format", out _));
        Assert.False(second.RootElement.GetProperty("chat_template_kwargs").GetProperty("enable_thinking").GetBoolean());
        JsonElement[] messages = second.RootElement.GetProperty("messages").EnumerateArray().ToArray();
        Assert.Equal("assistant", messages[^2].GetProperty("role").GetString());
        Assert.Contains("Long-horizon reasoning.", messages[^2].GetProperty("content").GetString());
        Assert.Contains("buy holds first", messages[^2].GetProperty("content").GetString());

        Assert.Equal("""{"action":"continue"}""", result.Content);
        Assert.Equal(310, result.CompletionTokens);
        Assert.Contains("Long-horizon reasoning.", result.Thinking);
    }

    [Fact]
    public async Task OllamaRequestKeepsNativeShape()
    {
        var handler = new QueueHandler(
            """{"message":{"role":"assistant","content":"{\"action\":\"none\"}","thinking":"t"},"done_reason":"stop","eval_count":9,"prompt_eval_count":50}""");
        LlmChatResult result = await new LlmChatClient(new HttpClient(handler)).ChatAsync(
            new ModelConfiguration(LlmBackends.Ollama, "qwen3:1.7b", new Uri("http://localhost:11434/")),
            new LlmChatRequest("a test", [new("user", "u")], Schema) { Think = false, MaxTokens = 2048 });

        Assert.Equal("http://localhost:11434/api/chat", handler.Requests[0].Uri);
        using JsonDocument request = JsonDocument.Parse(handler.Requests[0].Body);
        Assert.False(request.RootElement.GetProperty("think").GetBoolean());
        Assert.Equal(2048, request.RootElement.GetProperty("options").GetProperty("num_predict").GetInt32());
        Assert.Equal("object", request.RootElement.GetProperty("format").GetProperty("type").GetString());
        Assert.False(request.RootElement.GetProperty("options").TryGetProperty("temperature", out _));
        Assert.Equal(9, result.CompletionTokens);
        Assert.Equal("t", result.Thinking);
    }

    [Fact]
    public async Task HttpErrorsNameTheBackendAndPurpose()
    {
        var handler = new QueueHandler("model not loaded") { Status = HttpStatusCode.ServiceUnavailable };
        var exception = await Assert.ThrowsAsync<HttpRequestException>(() =>
            new LlmChatClient(new HttpClient(handler)).ChatAsync(LlamaCpp(), new LlmChatRequest("the bench", [new("user", "u")], Schema)));

        Assert.Contains("llama.cpp returned HTTP 503", exception.Message);
        Assert.Contains("the bench", exception.Message);
    }

    [Fact]
    public async Task PilotProviderRunsOnLlamaCpp()
    {
        var handler = new QueueHandler(OpenAiResponse(
            """{"action":"none","targetSector":0,"rationale":"Nothing to do.","citedKnowledgeNodeId":""}""", null));
        var provider = new OllamaAutonomousTestActionProvider(new HttpClient(handler));
        DateTimeOffset now = DateTimeOffset.UtcNow;
        var snapshot = new GameSnapshot(5, 1_000, 100, new Ship("S", "Me", "Merchant Cruiser", 5, 0, now), now);
        var situation = new GameSituationReport(
            now, GamePresenceState.Connected, GameAuthenticationState.Authenticated, GameSynchronizationState.Healthy,
            "Command", now, snapshot, []);

        AutonomousTestActionDecision decision = await provider.DecideAsync(
            LlamaCpp("qwen3-0.6b"),
            new AutonomousTestActionContext(situation, []));

        Assert.Equal(AutonomousTestAction.None, decision.Action);
        Assert.EndsWith("/v1/chat/completions", handler.Requests[0].Uri);
        using JsonDocument request = JsonDocument.Parse(handler.Requests[0].Body);
        // A qwen3 Pilot on llama.cpp answers directly unless thinking is configured.
        Assert.False(request.RootElement.GetProperty("chat_template_kwargs").GetProperty("enable_thinking").GetBoolean());
        Assert.Equal(2048, request.RootElement.GetProperty("max_tokens").GetInt32());
    }

    [Fact]
    public void BrainConfigurationDefaultsPerBackend()
    {
        IConfiguration ollama = Configuration(new() { ["Brains:Pilot:Model"] = "qwen3:1.7b" });
        ModelConfiguration ollamaPilot = BrainConfiguration.Resolve(ollama, BrainConfiguration.Pilot, "fallback");
        Assert.Equal(LlmBackends.Ollama, ollamaPilot.Provider);
        Assert.Equal("qwen3:1.7b", ollamaPilot.Model);
        Assert.Equal(new Uri("http://localhost:11434/"), ollamaPilot.Endpoint);
        Assert.Null(ollamaPilot.Think);

        IConfiguration llama = Configuration(new()
        {
            ["Reasoning:Backend"] = "llama.cpp",
            ["Brains:Strategic:Model"] = "big-thinker",
            ["Brains:Pilot:Thinking"] = "false"
        });
        ModelConfiguration pilot = BrainConfiguration.Resolve(llama, BrainConfiguration.Pilot, "tiny");
        ModelConfiguration strategic = BrainConfiguration.Resolve(llama, BrainConfiguration.Strategic, "default");
        ModelConfiguration reactive = BrainConfiguration.Resolve(llama, BrainConfiguration.Reactive, "tiny");
        Assert.Equal(LlmBackends.LlamaCpp, pilot.Provider);
        Assert.Equal(new Uri("http://127.0.0.1:8081/"), pilot.Endpoint);
        Assert.False(pilot.Think);
        Assert.Equal(new Uri("http://127.0.0.1:8082/"), strategic.Endpoint);
        Assert.Equal("big-thinker", strategic.Model);
        Assert.True(strategic.Think);
        Assert.Equal(BrainConfiguration.DefaultStrategicDeliberationTokens, strategic.MaxTokens);
        Assert.Equal(pilot.Endpoint, reactive.Endpoint);

        Assert.Throws<InvalidOperationException>(() => BrainConfiguration.Resolve(
            Configuration(new() { ["Brains:Pilot:Backend"] = "vllm" }), BrainConfiguration.Pilot, "x"));
    }

    [Fact]
    public void LlamaServerOptionsBuildSharedServerCommandLine()
    {
        ModelConfiguration strategic = LlamaCpp("strategist") with { Endpoint = new Uri("http://127.0.0.1:8082/") };
        Assert.Null(LlamaCppServerOptions.Read(Configuration(new()), BrainConfiguration.Strategic, strategic));

        LlamaCppServerOptions options = LlamaCppServerOptions.Read(
            Configuration(new()
            {
                ["Brains:Strategic:LlamaCpp:ModelPath"] = "models/strategist.gguf",
                ["Brains:Strategic:LlamaCpp:Parallel"] = "2",
                ["Brains:Strategic:LlamaCpp:ExtraArguments"] = "-fa on --cache-type-k q8_0 --chat-template-file \"C:\\My Templates\\t.jinja\""
            }),
            BrainConfiguration.Strategic,
            strategic)!;
        IReadOnlyList<string> arguments = options.BuildArguments();

        Assert.Equal("strategist", Value(arguments, "--alias"));
        Assert.Equal("8082", Value(arguments, "--port"));
        Assert.Equal("32768", Value(arguments, "--ctx-size"));
        Assert.Equal("2", Value(arguments, "--parallel"));
        Assert.Equal("999", Value(arguments, "--n-gpu-layers"));
        Assert.Contains("--jinja", arguments);
        Assert.Equal("C:\\My Templates\\t.jinja", Value(arguments, "--chat-template-file"));
        Assert.Equal("on", Value(arguments, "-fa"));
        Assert.Throws<InvalidOperationException>(() => LlamaCppServerOptions.Read(
            Configuration(new() { ["Brains:Pilot:LlamaCpp:ModelPath"] = "m.gguf" }),
            BrainConfiguration.Pilot,
            LlamaCpp() with { Endpoint = new Uri("http://10.0.0.5:8081/") }));
    }

    [Theory]
    [InlineData("ollama:qwen3:1.7b", LlmBackends.Ollama, "qwen3:1.7b", "http://localhost:11434/", null)]
    [InlineData("qwen3:1.7b", LlmBackends.Ollama, "qwen3:1.7b", "http://localhost:11434/", null)]
    [InlineData("llamacpp:qwen3-0.6b", LlmBackends.LlamaCpp, "qwen3-0.6b", "http://127.0.0.1:8081/", null)]
    [InlineData("llama.cpp:gemma-3-1b-think@http://127.0.0.1:9100/", LlmBackends.LlamaCpp, "gemma-3-1b", "http://127.0.0.1:9100/", false)]
    [InlineData("llamacpp:smol+think", LlmBackends.LlamaCpp, "smol", "http://127.0.0.1:8081/", true)]
    public void BenchModelSpecsParse(string text, string backend, string model, string endpoint, bool? think)
    {
        ModelConfiguration configuration = BenchModelSpec.Parse(text, BrainConfiguration.Pilot).Configuration;

        Assert.Equal(backend, configuration.Provider);
        Assert.Equal(model, configuration.Model);
        Assert.Equal(new Uri(endpoint), configuration.Endpoint);
        Assert.Equal(think, configuration.Think);
    }

    [Fact]
    public void BenchRejectsRemoteEndpoints()
    {
        Assert.Throws<ArgumentException>(() => BenchModelSpec.Parse("llamacpp:x@http://example.com:8081/", "Pilot"));
    }

    [Fact]
    public void BenchParsesRecordedPromptsAndResponses()
    {
        IReadOnlyList<LlmChatMessage> messages = ModelBench.ParsePrompt(
            $"[system]{Environment.NewLine}You pilot.{Environment.NewLine}{Environment.NewLine}[user]{Environment.NewLine}{{\"a\":1}}");
        Assert.Equal(["system", "user"], messages.Select(message => message.Role));
        Assert.Equal("You pilot.", messages[0].Content);
        Assert.Equal("{\"a\":1}", messages[1].Content);

        Assert.Equal("""{"action":"none"}""", ModelBench.ExtractContent("""{"message":{"content":"{\"action\":\"none\"}"}}"""));
        string deliberated = OpenAiResponse("thinking prose", null) + "\n" + OpenAiResponse("""{"objectiveDecision":"hold"}""", null);
        Assert.Equal("hold", ModelBench.ExtractDecision(ModelBench.ExtractContent(deliberated), "objectiveDecision"));
        Assert.Null(ModelBench.ExtractDecision("not json", "action"));
    }

    [Fact]
    public async Task BenchReplaysRecordedPromptsAndScoresModels()
    {
        string path = Path.Combine(Path.GetTempPath(), $"twbot-bench-{Guid.NewGuid():N}.db");
        string connectionString = new SqliteConnectionStringBuilder { DataSource = path, Pooling = false }.ToString();
        try
        {
            var history = new SqliteLlmInteractionHistoryStore(connectionString);
            DateTimeOffset now = DateTimeOffset.UtcNow;
            string[] recorded = ["moveToAdjacentSector", "none", "tradeAtCurrentPort"];
            for (int index = 0; index < recorded.Length; index++)
            {
                history.Record(new LlmInteractionRecord(
                    $"i{index}", "meridian", "Pilot", "Ollama", "qwen3:8b", 1, now.AddSeconds(index), now.AddSeconds(index),
                    $"[system]\nYou pilot.\n\n[user]\n{{\"case\":{index}}}",
                    JsonSerializer.Serialize(new { message = new { content = $"{{\"action\":\"{recorded[index]}\"}}" } }),
                    true, null));
            }
            history.Record(new LlmInteractionRecord(
                "retry", "meridian", "Pilot", "Ollama", "qwen3:8b", 2, now, now, "[user]\nretry", "{}", true, null));
            history.Record(new LlmInteractionRecord(
                "strategy", "meridian", "Strategic Review", "Ollama", "qwen3:8b", 1, now, now, "[user]\ns", "{}", true, null));

            IReadOnlyList<BenchCase> cases = ModelBench.LoadCases(connectionString, BenchProfile.Pilot, 10);
            Assert.Equal(["i0", "i1", "i2"], cases.Select(item => item.InteractionId));
            Assert.Equal("moveToAdjacentSector", cases[0].ReferenceDecision);

            // Warm-up, then: agrees, disagrees, invalid action.
            var handler = new QueueHandler(
                OpenAiResponse("""{"action":"none"}""", null, completionTokens: 5),
                OpenAiResponse("""{"action":"moveToAdjacentSector"}""", null, completionTokens: 5),
                OpenAiResponse("""{"action":"buyFighters"}""", null, completionTokens: 5),
                OpenAiResponse("""{"action":"selfDestruct"}""", null, completionTokens: 5));
            BenchResult result = await new ModelBench(new LlmChatClient(new HttpClient(handler))).RunAsync(
                BenchModelSpec.Parse("llamacpp:tiny", BrainConfiguration.Pilot),
                BenchProfile.Pilot,
                cases);

            Assert.Equal(4, handler.Requests.Count);
            Assert.Equal(3, result.Cases);
            Assert.Equal(2, result.Valid);
            Assert.Equal(2, result.Comparable);
            Assert.Equal(1, result.Agreed);
            Assert.Equal(5, result.MeanCompletionTokens);
            Assert.Contains(result.Errors, error => error.Contains("selfDestruct", StringComparison.Ordinal));
            string markdown = ModelBench.ToMarkdown(BenchProfile.Pilot, cases, [result]);
            Assert.Contains("| llamacpp:tiny | 2/3", markdown);
        }
        finally
        {
            File.Delete(path);
        }
    }

    private static string Value(IReadOnlyList<string> arguments, string name) =>
        arguments[arguments.ToList().IndexOf(name) + 1];

    private static IConfiguration Configuration(Dictionary<string, string?> values) =>
        new ConfigurationBuilder().AddInMemoryCollection(values).Build();

    private static string OpenAiResponse(string content, string? reasoning, int promptTokens = 10, int completionTokens = 3) =>
        JsonSerializer.Serialize(new
        {
            choices = new[]
            {
                new
                {
                    finish_reason = "stop",
                    message = new { role = "assistant", content, reasoning_content = reasoning }
                }
            },
            usage = new { prompt_tokens = promptTokens, completion_tokens = completionTokens }
        });

    private sealed class QueueHandler(params string[] responses) : HttpMessageHandler
    {
        private readonly Queue<string> _responses = new(responses);

        public HttpStatusCode Status { get; init; } = HttpStatusCode.OK;
        public List<(string Uri, string Body)> Requests { get; } = [];

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            string body = request.Content is null ? string.Empty : await request.Content.ReadAsStringAsync(cancellationToken);
            Requests.Add((request.RequestUri!.ToString(), body));
            string response = _responses.Count > 1 ? _responses.Dequeue() : _responses.Peek();
            return new HttpResponseMessage(Status) { Content = new StringContent(response, Encoding.UTF8, "application/json") };
        }
    }
}
