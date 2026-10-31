using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;

namespace LLMSurvivingProxyAgent.CorpusBuilder;

public enum WebSourceRole
{
    TacticalGuide,
    ReferenceManual,
    ForumDiscussion,
    VersionHistory,
    DeveloperNotes,
    PlayerJournal,
    AllianceNotes,
    GeneralWebDocumentation
}

public enum WebSourceRetrievalStatus
{
    NotFetched
}

public enum WebSourceEvidenceClassCandidate
{
    AuthoredClaimCandidate,
    ImplementedBehaviorCandidate,
    ObservedOutcomeCandidate,
    MixedCandidate
}

public sealed record WebSourceSeed(
    string Url,
    string? Title = null,
    string? Author = null,
    WebSourceRole? Role = null,
    DateTimeOffset? RetrievedAt = null,
    int? PriorityTier = null,
    WebSourceEvidenceClassCandidate? EvidenceClassCandidate = null);

public sealed record WebSourceInventoryInput(IReadOnlyList<WebSourceSeed> Sources);

public sealed record WebSourceInventory(
    string SchemaVersion,
    DateTimeOffset InventoriedAt,
    IReadOnlyList<WebSourceInventoryEntry> Sources);

public sealed record WebSourceInventoryEntry(
    string Url,
    string Domain,
    string? Title,
    string? Author,
    DateTimeOffset? RetrievedAt,
    string MetadataBasis,
    WebSourceRole Role,
    string RoleClassificationBasis,
    WebSourceEvidenceClassCandidate ExpectedEvidenceClass,
    WebSourceRetrievalStatus RetrievalStatus,
    int? PriorityTier);

public sealed class WebSourceInventoryService
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        WriteIndented = true,
        Converters = { new JsonStringEnumConverter(JsonNamingPolicy.CamelCase) }
    };

    private static readonly Regex UrlTokens = new(
        @"[a-z0-9]+",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant | RegexOptions.Compiled);

    private static readonly HashSet<string> ForumMarkers = new(StringComparer.OrdinalIgnoreCase)
    {
        "bbs", "board", "bulletin", "forum", "forums", "thread", "threads"
    };

    private static readonly HashSet<string> VersionHistoryMarkers = new(StringComparer.OrdinalIgnoreCase)
    {
        "change", "changes", "changelog", "history", "patch", "release", "version"
    };

    private static readonly HashSet<string> PlayerJournalMarkers = new(StringComparer.OrdinalIgnoreCase)
    {
        "blog", "diary", "journal", "personal", "playerlog"
    };

    private static readonly HashSet<string> AllianceMarkers = new(StringComparer.OrdinalIgnoreCase)
    {
        "alliance", "clan", "corp", "corporation", "guild"
    };

    private static readonly HashSet<string> DeveloperMarkers = new(StringComparer.OrdinalIgnoreCase)
    {
        "architecture", "developer", "development", "devnotes", "design", "implementation"
    };

    private static readonly HashSet<string> TacticalGuideMarkers = new(StringComparer.OrdinalIgnoreCase)
    {
        "guide", "guides", "playbook", "strategy", "strategies", "tactic", "tactical",
        "tactics", "walkthrough"
    };

    private static readonly HashSet<string> ReferenceMarkers = new(StringComparer.OrdinalIgnoreCase)
    {
        "bible", "docs", "documentation", "faq", "help", "manual", "reference", "rules", "wiki"
    };

    public async Task<WebSourceInventory> WriteInventoryAsync(
        string inputPath,
        string outputPath,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(inputPath);
        ArgumentException.ThrowIfNullOrWhiteSpace(outputPath);
        var sourcePath = Path.GetFullPath(inputPath);
        var destination = Path.GetFullPath(outputPath);
        if (string.Equals(sourcePath, destination, StringComparison.OrdinalIgnoreCase))
        {
            throw new ArgumentException("Input and output must use different paths.", nameof(outputPath));
        }
        if (!File.Exists(sourcePath))
        {
            throw new FileNotFoundException("Web source URL input file was not found.", sourcePath);
        }
        if (File.Exists(destination) || Directory.Exists(destination))
        {
            throw new IOException("Web source inventory output already exists; choose a new path.");
        }

        var inputJson = await File.ReadAllTextAsync(sourcePath, cancellationToken);
        WebSourceInventoryInput input;
        try
        {
            input = JsonSerializer.Deserialize<WebSourceInventoryInput>(inputJson, JsonOptions)
                ?? throw new InvalidDataException("Web source input is empty or invalid.");
        }
        catch (JsonException exception)
        {
            throw new InvalidDataException(
                "Web source input is invalid JSON or contains an unsupported role value.",
                exception);
        }
        if (input.Sources is null)
        {
            throw new InvalidDataException("Web source input must contain a non-null 'sources' array.");
        }

        var inventory = CreateInventory(input.Sources);
        var bytes = JsonSerializer.SerializeToUtf8Bytes(inventory, JsonOptions);
        Directory.CreateDirectory(Path.GetDirectoryName(destination)!);
        await using var output = new FileStream(
            destination,
            FileMode.CreateNew,
            FileAccess.Write,
            FileShare.None,
            4096,
            FileOptions.Asynchronous);
        await output.WriteAsync(bytes, cancellationToken);
        return inventory;
    }

    public static WebSourceInventory CreateInventory(
        IReadOnlyList<WebSourceSeed> sources,
        DateTimeOffset? inventoriedAt = null)
    {
        ArgumentNullException.ThrowIfNull(sources);
        if (sources.Count == 0)
        {
            throw new InvalidDataException("Web source inventory requires at least one URL.");
        }

        var entries = new List<WebSourceInventoryEntry>(sources.Count);
        var seenUrls = new HashSet<string>(StringComparer.Ordinal);
        foreach (var source in sources)
        {
            if (source is null)
            {
                throw new InvalidDataException("Web source input cannot contain null source entries.");
            }

            if (!Uri.TryCreate(source.Url?.Trim(), UriKind.Absolute, out var uri) ||
                uri.Scheme is not ("http" or "https") ||
                string.IsNullOrWhiteSpace(uri.Host) ||
                !string.IsNullOrEmpty(uri.UserInfo))
            {
                throw new InvalidDataException(
                    $"Web source URL '{source.Url}' must be an absolute HTTP(S) URL without embedded credentials.");
            }

            var normalizedUrl = uri.AbsoluteUri;
            if (!seenUrls.Add(normalizedUrl))
            {
                throw new InvalidDataException($"Duplicate web source URL: {normalizedUrl}");
            }
            if (source.Role is { } suppliedRole && !Enum.IsDefined(suppliedRole))
            {
                throw new InvalidDataException($"Unknown web source role: {suppliedRole}");
            }
            if (source.PriorityTier is { } priorityTier && priorityTier is < 1 or > 5)
            {
                throw new InvalidDataException(
                    $"Web source priority tier must be between 1 and 5: {priorityTier}");
            }
            if (source.EvidenceClassCandidate is { } evidenceClassCandidate &&
                !Enum.IsDefined(evidenceClassCandidate))
            {
                throw new InvalidDataException(
                    $"Unknown web source evidence class candidate: {evidenceClassCandidate}");
            }

            var role = source.Role ?? Classify(uri);
            var metadataProvided = source.Title is not null ||
                source.Author is not null ||
                source.RetrievedAt.HasValue;
            entries.Add(new WebSourceInventoryEntry(
                normalizedUrl,
                uri.IdnHost.ToLowerInvariant(),
                NormalizeOptional(source.Title),
                NormalizeOptional(source.Author),
                source.RetrievedAt,
                metadataProvided ? "UserSupplied" : "Unavailable",
                role,
                source.Role.HasValue ? "UserSupplied" : "HostAndPathHeuristic",
                source.EvidenceClassCandidate ?? WebSourceEvidenceClassCandidate.AuthoredClaimCandidate,
                WebSourceRetrievalStatus.NotFetched,
                source.PriorityTier));
        }

        return new WebSourceInventory(
            "1.0",
            inventoriedAt ?? DateTimeOffset.UtcNow,
            entries);
    }

    private static WebSourceRole Classify(Uri uri)
    {
        var path = Uri.UnescapeDataString(uri.AbsolutePath);
        var tokens = UrlTokens.Matches($"{uri.IdnHost}/{path}")
            .Select(match => match.Value)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        if (tokens.Overlaps(ForumMarkers))
        {
            return WebSourceRole.ForumDiscussion;
        }
        if (tokens.Overlaps(VersionHistoryMarkers))
        {
            return WebSourceRole.VersionHistory;
        }
        if (tokens.Overlaps(PlayerJournalMarkers))
        {
            return WebSourceRole.PlayerJournal;
        }
        if (tokens.Overlaps(AllianceMarkers))
        {
            return WebSourceRole.AllianceNotes;
        }
        if (tokens.Overlaps(DeveloperMarkers))
        {
            return WebSourceRole.DeveloperNotes;
        }
        if (tokens.Overlaps(TacticalGuideMarkers))
        {
            return WebSourceRole.TacticalGuide;
        }
        if (tokens.Overlaps(ReferenceMarkers))
        {
            return WebSourceRole.ReferenceManual;
        }
        return WebSourceRole.GeneralWebDocumentation;
    }

    private static string? NormalizeOptional(string? value) =>
        string.IsNullOrWhiteSpace(value) ? null : value.Trim();
}
