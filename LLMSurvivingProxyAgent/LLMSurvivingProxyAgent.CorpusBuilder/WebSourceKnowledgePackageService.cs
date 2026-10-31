using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using LLMSurvivingProxyAgent.Knowledge;
using LLMSurvivingProxyAgent.Packaging;

namespace LLMSurvivingProxyAgent.CorpusBuilder;

public sealed record WebSourceKnowledgePackageResult(
    string PackagePath,
    int DocumentsExported,
    int NodesExported);

public sealed class WebSourceKnowledgePackageService
{
    private const string RootId = "external-web-review";
    private const long MaximumInputBytes = 5 * 1024 * 1024;

    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        Converters = { new JsonStringEnumConverter(JsonNamingPolicy.CamelCase) }
    };

    public async Task<WebSourceKnowledgePackageResult> BuildAsync(
        string inputPath,
        string outputPath,
        string version = "1.0.0",
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(inputPath);
        ArgumentException.ThrowIfNullOrWhiteSpace(outputPath);
        ArgumentException.ThrowIfNullOrWhiteSpace(version);
        if (version.Length > 64 ||
            !version.All(character => char.IsAsciiLetterOrDigit(character) || character is '.' or '-' or '_'))
        {
            throw new ArgumentException("Version must use at most 64 ASCII letters, digits, dots, dashes, or underscores.", nameof(version));
        }

        var sourcePath = Path.GetFullPath(inputPath);
        var destination = Path.GetFullPath(outputPath);
        if (string.Equals(sourcePath, destination, StringComparison.OrdinalIgnoreCase))
        {
            throw new ArgumentException("Input and output must use different paths.", nameof(outputPath));
        }
        if (!File.Exists(sourcePath))
        {
            throw new FileNotFoundException("Reviewed web-source input was not found.", sourcePath);
        }
        if (File.Exists(destination) || Directory.Exists(destination))
        {
            throw new IOException("Web-source seed package output already exists; choose a new path.");
        }
        if (new FileInfo(sourcePath).Length > MaximumInputBytes)
        {
            throw new InvalidDataException("Reviewed web-source input exceeds the 5 MB limit.");
        }

        var inputJson = await File.ReadAllTextAsync(sourcePath, cancellationToken);
        WebSourceKnowledgeSet input;
        try
        {
            input = JsonSerializer.Deserialize<WebSourceKnowledgeSet>(inputJson, JsonOptions)
                ?? throw new InvalidDataException("Reviewed web-source input is empty or invalid.");
        }
        catch (JsonException exception)
        {
            throw new InvalidDataException("Reviewed web-source input is malformed JSON.", exception);
        }

        ValidateInput(input);
        var documents = CreateDocuments(input.Sources, DateTimeOffset.UtcNow);
        var nodeCount = documents.Sum(document => document.Nodes.Count);
        var miningCandidates = input.KnowledgeEvidenceCandidates + input.MetadataOnlySources;
        var inventorySource = new PackageSourceSummary(
            RootId,
            "ExternalWebReview",
            input.Sources.Count,
            miningCandidates,
            0,
            input.DiscoveryOnlySources,
            0,
            0,
            documents.Count,
            nodeCount,
            new FileInfo(sourcePath).Length,
            new Dictionary<string, int>(StringComparer.Ordinal) { [".urlreview"] = input.Sources.Count },
            new Dictionary<string, int>(StringComparer.Ordinal)
            {
                [KnowledgeCategory.WorldKnowledge.ToString()] = nodeCount
            },
            false);

        var stagingDirectory = Path.Combine(Path.GetTempPath(), $"twbot-web-seed-{Guid.NewGuid():N}");
        Directory.CreateDirectory(stagingDirectory);
        try
        {
            var store = new SqliteKnowledgeStore($"Data Source={Path.Combine(stagingDirectory, "staging.db")};Pooling=False");
            await store.InitializeAsync(cancellationToken);
            await store.ImportDocumentsAsync(documents, cancellationToken);
            var package = await new SeedKnowledgePackageService(store, store)
                .ExportVersionedAsync(
                    "external-web-source-review",
                    version,
                    [inventorySource],
                    cancellationToken);

            Directory.CreateDirectory(Path.GetDirectoryName(destination)!);
            var temporaryOutput = destination + $".{Guid.NewGuid():N}.tmp";
            try
            {
                await File.WriteAllBytesAsync(temporaryOutput, package, cancellationToken);
                File.Move(temporaryOutput, destination);
            }
            finally
            {
                if (File.Exists(temporaryOutput))
                {
                    File.Delete(temporaryOutput);
                }
            }

            return new WebSourceKnowledgePackageResult(destination, documents.Count, nodeCount);
        }
        finally
        {
            Directory.Delete(stagingDirectory, recursive: true);
        }
    }

    public static IReadOnlyList<KnowledgeDocument> CreateDocuments(
        IReadOnlyList<WebSourceKnowledgeEntry> sources,
        DateTimeOffset minedAt)
    {
        ArgumentNullException.ThrowIfNull(sources);
        return sources
            .Where(source => source.CorpusUse != "DiscoveryOnly")
            .Select(source =>
        {
            var sourceHash = Hash(source.Url);
            var documentId = $"web-{sourceHash[..24]}";
            var nodes = source.Observations.Select((observation, index) =>
                {
                    var locator = $"review observation {index + 1}; no page line locator available";
                    var evidenceText = BuildEvidenceText(source, observation);
                    return new KnowledgeNode(
                        $"web-node-{Hash($"{source.Url}\n{index + 1}")[..24]}",
                        documentId,
                        KnowledgeCategory.WorldKnowledge,
                        $"{source.VisibleTitle} - observation {index + 1}",
                        $"Paraphrased source observation; not independently validated as game doctrine: {observation}",
                        0.5m,
                        ExtractionBasis.ExternalSourceReview,
                        [
                            new KnowledgeEvidence(1, "external-web-source-review", evidenceText)
                            {
                                SourceUrl = source.Url,
                                SourceLocator = locator
                            }
                        ]);
                }).ToArray();

            var serializedSource = JsonSerializer.Serialize(source, JsonOptions);
            var relativePath = $"web/{new Uri(source.Url).IdnHost.ToLowerInvariant()}/{sourceHash[..24]}.urlreview";
            return new KnowledgeDocument(
                documentId,
                RootId,
                relativePath,
                $"{sourceHash[..24]}.urlreview",
                Hash(serializedSource),
                "en",
                minedAt,
                nodes,
                []);
            })
            .ToArray();
    }

    private static void ValidateInput(WebSourceKnowledgeSet input)
    {
        if (input.SourceCount is < 1 or > 1000 ||
            input.Sources is null ||
            input.Sources.Count != input.SourceCount ||
            input.ExcludedSources is null ||
            input.Sources.Any(source => source is null))
        {
            throw new InvalidDataException("Reviewed web-source input has invalid source counts or entries.");
        }

        var seenUrls = new HashSet<string>(StringComparer.Ordinal);
        foreach (var source in input.Sources)
        {
            if (!Uri.TryCreate(source.Url, UriKind.Absolute, out var uri) ||
                uri.Scheme is not ("http" or "https") ||
                string.IsNullOrWhiteSpace(uri.Host) ||
                !string.IsNullOrEmpty(uri.UserInfo) ||
                source.HttpStatus != 200 ||
                !seenUrls.Add(uri.AbsoluteUri) ||
                source.PriorityTier is < 1 or > 5 ||
                string.IsNullOrWhiteSpace(source.VisibleTitle) ||
                string.IsNullOrWhiteSpace(source.ContentType) ||
                source.Observations is null ||
                source.Limitations is null ||
                source.Observations.Count > 100 ||
                source.Limitations.Count > 100 ||
                source.Observations.Any(note =>
                    string.IsNullOrWhiteSpace(note) || note.Length > 5000) ||
                source.Limitations.Any(note =>
                    string.IsNullOrWhiteSpace(note) || note.Length > 2000) ||
                source.CorpusUse is not ("KnowledgeEvidenceCandidate" or "DiscoveryOnly" or "MetadataOnly"))
            {
                throw new InvalidDataException("Reviewed web-source entry is invalid or is not an accessible HTTP(S) source.");
            }

            if (source.CorpusUse == "KnowledgeEvidenceCandidate" && source.Observations.Count == 0)
            {
                throw new InvalidDataException("Knowledge-evidence sources must contain at least one reviewed observation.");
            }
            if (source.CorpusUse == "MetadataOnly" && source.Observations.Count == 0)
            {
                throw new InvalidDataException("Metadata-only sources must contain at least one metadata observation.");
            }
        }

        var expectedEvidenceCount = input.Sources.Count(source =>
            source.CorpusUse == "KnowledgeEvidenceCandidate");
        var expectedDiscoveryCount = input.Sources.Count(source => source.CorpusUse == "DiscoveryOnly");
        var expectedMetadataCount = input.Sources.Count(source => source.CorpusUse == "MetadataOnly");
        if (input.KnowledgeEvidenceCandidates != expectedEvidenceCount ||
            input.DiscoveryOnlySources != expectedDiscoveryCount ||
            input.MetadataOnlySources != expectedMetadataCount ||
            input.OutcomeEvidenceCount != 0)
        {
            throw new InvalidDataException("Reviewed web-source summary counts do not match source records.");
        }
    }

    private static string BuildEvidenceText(WebSourceKnowledgeEntry source, string observation)
    {
        var metadata = new List<string> { $"Source URL: {source.Url}", $"Page title: {source.VisibleTitle}" };
        if (!string.IsNullOrWhiteSpace(source.AuthorOrPublisher))
        {
            metadata.Add($"Visible author/publisher: {source.AuthorOrPublisher}");
        }
        if (!string.IsNullOrWhiteSpace(source.DisplayedDate))
        {
            metadata.Add($"Displayed source date: {source.DisplayedDate}");
        }

        metadata.Add($"Review note: {observation}");
        return string.Join("; ", metadata);
    }

    private static string Hash(string value) =>
        Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(value))).ToLowerInvariant();
}

public sealed record WebSourceKnowledgeSet(
    int SourceCount,
    int KnowledgeEvidenceCandidates,
    int DiscoveryOnlySources,
    int MetadataOnlySources,
    int OutcomeEvidenceCount,
    IReadOnlyList<WebSourceKnowledgeEntry> Sources,
    IReadOnlyList<WebSourceExclusion> ExcludedSources);

public sealed record WebSourceKnowledgeEntry(
    string Url,
    int PriorityTier,
    int HttpStatus,
    string VisibleTitle,
    string ContentType,
    string? AuthorOrPublisher,
    string? DisplayedDate,
    IReadOnlyList<string> Observations,
    IReadOnlyList<string> Limitations,
    string EvidenceClass,
    string CorpusUse);

public sealed record WebSourceExclusion(
    string Url,
    int HttpStatus,
    string Reason);
