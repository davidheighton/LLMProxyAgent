using System.Text.Json;
using System.Text.Json.Serialization;
using LLMSurvivingProxyAgent.CorpusBuilder;
using LLMSurvivingProxyAgent.Knowledge;
using LLMSurvivingProxyAgent.Packaging;

namespace LLMSurvivingProxyAgent.TradeWars.Tests;

public sealed class WebSourceKnowledgePackageTests
{
    [Fact]
    public async Task PackagesReviewedSourcesWithUrlProvenanceAndSkipsDiscoveryClaims()
    {
        var directory = Path.Combine(Path.GetTempPath(), $"twbot-web-seed-{Guid.NewGuid():N}");
        Directory.CreateDirectory(directory);
        try
        {
            var inputPath = Path.Combine(directory, "review.json");
            var packagePath = Path.Combine(directory, "web.seedpkg");
            var input = new WebSourceKnowledgeSet(
                2,
                1,
                1,
                0,
                0,
                [
                    new WebSourceKnowledgeEntry(
                        "https://docs.example.test/twx-proxy",
                        1,
                        200,
                        "TWX Proxy documentation",
                        "Software documentation",
                        "Example author",
                        null,
                        ["The source describes a local database and preset searches."],
                        ["Runtime behavior was not independently verified."],
                        "AuthoredClaim",
                        "KnowledgeEvidenceCandidate"),
                    new WebSourceKnowledgeEntry(
                        "https://forums.example.test/",
                        1,
                        200,
                        "Forum index",
                        "Forum index",
                        null,
                        null,
                        [],
                        ["Navigation page only."],
                        "DiscoverySignal",
                        "DiscoveryOnly")
                ],
                [
                    new WebSourceExclusion(
                        "https://docs.example.test/missing",
                        404,
                        "Empty source")
                ]);
            await File.WriteAllTextAsync(inputPath, JsonSerializer.Serialize(input, JsonOptions));

            var result = await new WebSourceKnowledgePackageService()
                .BuildAsync(inputPath, packagePath, "test-web-v1");

            Assert.Equal(packagePath, result.PackagePath);
            Assert.Equal(1, result.DocumentsExported);
            Assert.Equal(1, result.NodesExported);

            var seedStore = new SqliteKnowledgeStore(
                $"Data Source={Path.Combine(directory, "seed.db")};Pooling=False");
            var runtimeStore = new SqliteKnowledgeStore(
                $"Data Source={Path.Combine(directory, "runtime.db")};Pooling=False");
            await seedStore.InitializeAsync();
            await runtimeStore.InitializeAsync();
            await using var packageStream = File.OpenRead(packagePath);
            var importReport = await new SeedKnowledgePackageService(seedStore, runtimeStore)
                .ImportAsync(packageStream);
            var documents = await seedStore.GetAllDocumentsAsync();
            var node = Assert.Single(documents.SelectMany(document => document.Nodes));

            Assert.Equal(1, importReport.DocumentsImported);
            Assert.Equal(1, importReport.NodesImported);
            Assert.Single(documents);
            Assert.Equal(
                ExtractionBasis.ExternalSourceReview,
                node.Basis);
            Assert.Equal(0.5m, node.Confidence);
            Assert.Equal(
                "https://docs.example.test/twx-proxy",
                Assert.Single(node.Evidence).SourceUrl);
            Assert.Contains(
                "review observation 1",
                Assert.Single(node.Evidence).SourceLocator!);
            Assert.DoesNotContain(documents, document => document.Nodes.Count == 0);
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
