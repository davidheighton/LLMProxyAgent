using System.Text.Json;
using System.Text.Json.Serialization;
using LLMSurvivingProxyAgent.CorpusBuilder;

namespace LLMSurvivingProxyAgent.TradeWars.Tests;

public sealed class WebSourceInventoryTests
{
    [Fact]
    public async Task WritesClassifiedUrlProvenanceWithoutFetchingPages()
    {
        var directory = Path.Combine(Path.GetTempPath(), $"twbot-web-inventory-{Guid.NewGuid():N}");
        Directory.CreateDirectory(directory);
        try
        {
            var inputPath = Path.Combine(directory, "urls.json");
            var outputPath = Path.Combine(directory, "inventory.json");
            var retrievedAt = new DateTimeOffset(2026, 10, 22, 12, 0, 0, TimeSpan.Zero);
            var input = new WebSourceInventoryInput(
            [
                new WebSourceSeed(
                    "https://forums.example.test/threads/sst/",
                    "SST discussion",
                    "TradeWars player",
                    PriorityTier: 1),
                new WebSourceSeed("https://manual.example.test/wiki/trade-wars-bible"),
                new WebSourceSeed(
                    "https://example.test/tactics/sst-guide",
                    Role: WebSourceRole.TacticalGuide),
                new WebSourceSeed(
                    "https://example.test/archive/changelog/v1",
                    RetrievedAt: retrievedAt),
                new WebSourceSeed(
                    "https://github.example.test/project/source",
                    Role: WebSourceRole.DeveloperNotes,
                    EvidenceClassCandidate: WebSourceEvidenceClassCandidate.MixedCandidate)
            ]);
            await File.WriteAllTextAsync(inputPath, JsonSerializer.Serialize(input, JsonOptions));

            var inventory = await new WebSourceInventoryService()
                .WriteInventoryAsync(inputPath, outputPath);
            var written = JsonSerializer.Deserialize<WebSourceInventory>(
                await File.ReadAllTextAsync(outputPath),
                JsonOptions);

            Assert.NotNull(written);
            Assert.Equal("1.0", inventory.SchemaVersion);
            Assert.Equal(5, inventory.Sources.Count);
            Assert.Equal(inventory.SchemaVersion, written.SchemaVersion);
            Assert.Equal(inventory.Sources, written.Sources);
            Assert.Equal("forums.example.test", inventory.Sources[0].Domain);
            Assert.Equal(WebSourceRole.ForumDiscussion, inventory.Sources[0].Role);
            Assert.Equal("SST discussion", inventory.Sources[0].Title);
            Assert.Equal("TradeWars player", inventory.Sources[0].Author);
            Assert.Equal("UserSupplied", inventory.Sources[0].MetadataBasis);
            Assert.Equal(1, inventory.Sources[0].PriorityTier);
            Assert.Null(inventory.Sources[0].RetrievedAt);
            Assert.Equal("HostAndPathHeuristic", inventory.Sources[0].RoleClassificationBasis);
            Assert.Equal(WebSourceRole.ReferenceManual, inventory.Sources[1].Role);
            Assert.Equal(WebSourceRole.TacticalGuide, inventory.Sources[2].Role);
            Assert.Equal("UserSupplied", inventory.Sources[2].RoleClassificationBasis);
            Assert.Equal(WebSourceRole.VersionHistory, inventory.Sources[3].Role);
            Assert.Equal(retrievedAt, inventory.Sources[3].RetrievedAt);
            Assert.Equal(
                WebSourceEvidenceClassCandidate.MixedCandidate,
                inventory.Sources[4].ExpectedEvidenceClass);
            Assert.All(inventory.Sources, source =>
            {
                Assert.Equal(WebSourceRetrievalStatus.NotFetched, source.RetrievalStatus);
            });
            Assert.All(inventory.Sources.Take(4), source =>
                Assert.Equal(
                    WebSourceEvidenceClassCandidate.AuthoredClaimCandidate,
                    source.ExpectedEvidenceClass));
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    [Theory]
    [InlineData("ftp://example.test/strategy", "HTTP(S)")]
    [InlineData("https://user:password@example.test/guide", "credentials")]
    public void RejectsUnsupportedOrCredentialBearingUrls(string url, string expectedMessage)
    {
        var exception = Assert.Throws<InvalidDataException>(() =>
            WebSourceInventoryService.CreateInventory([new WebSourceSeed(url)]));

        Assert.Contains(expectedMessage, exception.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void RejectsDuplicateNormalizedUrls()
    {
        var exception = Assert.Throws<InvalidDataException>(() =>
            WebSourceInventoryService.CreateInventory(
            [
                new WebSourceSeed("https://EXAMPLE.test/guides/sst"),
                new WebSourceSeed("https://example.test/guides/sst")
            ]));

        Assert.Contains("Duplicate", exception.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(6)]
    public void RejectsPriorityTiersOutsideSupportedRange(int priorityTier)
    {
        var exception = Assert.Throws<InvalidDataException>(() =>
            WebSourceInventoryService.CreateInventory(
                [new WebSourceSeed("https://example.test/guide", PriorityTier: priorityTier)]));

        Assert.Contains("between 1 and 5", exception.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Theory]
    [InlineData("{\"sources\":null}", "non-null")]
    [InlineData("{\"sources\":[{\"url\":\"https://example.test/guide\",\"role\":\"unknown\"}]}", "invalid JSON")]
    [InlineData("{\"sources\":[{\"url\":\"https://example.test/guide\",\"evidenceClassCandidate\":\"unknown\"}]}", "invalid JSON")]
    [InlineData("{\"sources\":[null]}", "null source entries")]
    public async Task ReportsMalformedSourceInputExplicitly(string json, string expectedMessage)
    {
        var directory = Path.Combine(Path.GetTempPath(), $"twbot-web-inventory-{Guid.NewGuid():N}");
        Directory.CreateDirectory(directory);
        try
        {
            var inputPath = Path.Combine(directory, "urls.json");
            var outputPath = Path.Combine(directory, "inventory.json");
            await File.WriteAllTextAsync(inputPath, json);

            var exception = await Assert.ThrowsAsync<InvalidDataException>(() =>
                new WebSourceInventoryService().WriteInventoryAsync(inputPath, outputPath));

            Assert.Contains(expectedMessage, exception.Message, StringComparison.OrdinalIgnoreCase);
            Assert.False(File.Exists(outputPath));
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        Converters = { new JsonStringEnumConverter(JsonNamingPolicy.CamelCase) }
    };
}
