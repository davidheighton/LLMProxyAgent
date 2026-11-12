using System.Net;
using System.Text;
using System.Text.Json;
using LLMSurvivingProxyAgent.Core;
using LLMSurvivingProxyAgent.Models;

namespace LLMSurvivingProxyAgent.TradeWars.Tests;

public sealed class AutonomousLoginIdentityProviderTests
{
    [Fact]
    public async Task OllamaChoosesNamesAndPasswordWithStructuredLocalRequests()
    {
        var handler = new RecordingHttpHandler(
        [
            """{"message":{"content":"{\"value\":\"Meridian\"}"}}""",
            """{"message":{"content":"{\"value\":\"StarMap\"}"}}""",
            """{"message":{"content":"{\"value\":\"Starward Crown\"}"}}""",
            """{"message":{"content":"{\"value\":\"New Terra\"}"}}""",
            """{"message":{"content":"{\"value\":\"NovaPass42\"}"}}"""
        ]);
        using var client = new HttpClient(handler);
        var provider = new OllamaAutonomousLoginIdentityProvider(client);
        var configuration = new ModelConfiguration(
            "Ollama",
            "qwen3:8b",
            new Uri("http://127.0.0.1:11434/"));
        var context = new AutonomousLoginIdentityContext(
            "192.168.4.75",
            2002,
            "default",
            "E",
            "Meridian8",
            "StarMap42");

        Assert.Equal(
            "Meridian",
            await provider.ChooseServerLoginNameAsync(configuration, context));
        Assert.Equal(
            "StarMap",
            await provider.ChooseTraderAliasAsync(configuration, context));
        Assert.Equal(
            "Starward Crown",
            await provider.ChooseShipNameAsync(configuration, context));
        Assert.Equal(
            "New Terra",
            await provider.ChooseHomePlanetNameAsync(configuration, context));
        Assert.Equal(
            "NovaPass42",
            await provider.GenerateGamePasswordAsync(configuration, context));

        Assert.Equal(5, handler.RequestBodies.Count);
        foreach (var requestBody in handler.RequestBodies)
        {
            using var request = JsonDocument.Parse(requestBody);
            Assert.Equal("qwen3:8b", request.RootElement.GetProperty("model").GetString());
            Assert.False(request.RootElement.GetProperty("think").GetBoolean());
            Assert.Equal(1.0, request.RootElement.GetProperty("options").GetProperty("temperature").GetDouble());
            Assert.True(request.RootElement.GetProperty("options").TryGetProperty("seed", out _));
            Assert.Equal("object", request.RootElement.GetProperty("format").GetProperty("type").GetString());
            Assert.False(request.RootElement.GetProperty("format").GetProperty("additionalProperties").GetBoolean());
            Assert.Equal(
                ["value"],
                request.RootElement.GetProperty("format")
                    .GetProperty("required")
                    .EnumerateArray()
                    .Select(value => value.GetString()));
            Assert.Contains("TradeWars 2002", requestBody, StringComparison.Ordinal);
            Assert.Contains("192.168.4.75", requestBody, StringComparison.Ordinal);
        }
        Assert.Contains("memorable", handler.RequestBodies[2], StringComparison.OrdinalIgnoreCase);
        Assert.Contains("not a random string", handler.RequestBodies[3], StringComparison.OrdinalIgnoreCase);
        Assert.Contains("fresh password", handler.RequestBodies[4], StringComparison.OrdinalIgnoreCase);
        Assert.Contains("Meridian8", handler.RequestBodies[4], StringComparison.Ordinal);
        Assert.Contains("StarMap42", handler.RequestBodies[4], StringComparison.Ordinal);
        Assert.All(handler.RequestUris, uri =>
            Assert.Equal("http://127.0.0.1:11434/api/chat", uri!.ToString()));
    }

    [Fact]
    public async Task OllamaIdentityProviderRejectsRemoteEndpointsAndMalformedValues()
    {
        using var client = new HttpClient(new RecordingHttpHandler(["""{"message":{"content":"{}"}}"""]));
        var provider = new OllamaAutonomousLoginIdentityProvider(client);
        var context = new AutonomousLoginIdentityContext("localhost", 2002, "default", "E");

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            provider.ChooseServerLoginNameAsync(
                new ModelConfiguration("Ollama", "qwen3:8b", new Uri("https://example.com/")),
                context));

        await Assert.ThrowsAsync<InvalidDataException>(() =>
            provider.ChooseServerLoginNameAsync(
                new ModelConfiguration("Ollama", "qwen3:8b", new Uri("http://localhost:11434/")),
                context));
    }

    private sealed class RecordingHttpHandler(IReadOnlyList<string> responses) : HttpMessageHandler
    {
        private readonly Queue<string> _responses = new(responses);

        public List<Uri?> RequestUris { get; } = [];
        public List<string> RequestBodies { get; } = [];

        protected override async Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            RequestUris.Add(request.RequestUri);
            RequestBodies.Add(request.Content is null
                ? string.Empty
                : await request.Content.ReadAsStringAsync(cancellationToken));
            if (!_responses.TryDequeue(out var responseBody))
            {
                throw new InvalidOperationException("No fake Ollama response remains.");
            }

            return new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(responseBody, Encoding.UTF8, "application/json")
            };
        }
    }
}
