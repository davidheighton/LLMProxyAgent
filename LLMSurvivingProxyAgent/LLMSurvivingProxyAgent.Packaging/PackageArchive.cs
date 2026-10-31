using System.IO.Compression;
using System.Security.Cryptography;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace LLMSurvivingProxyAgent.Packaging;

internal static class PackageArchive
{
    private const string ManifestFileName = "manifest.json";
    private const string PayloadFileName = "payload.json";
    private const int CurrentSchemaVersion = 3;
    private const int MaximumArchiveBytes = 128 * 1024 * 1024;
    private const int MaximumPayloadBytes = 120 * 1024 * 1024;

    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        Converters = { new JsonStringEnumConverter(JsonNamingPolicy.CamelCase) }
    };

    public static byte[] Create<TPayload>(
        string packageType,
        string source,
        TPayload payload,
        bool historyMayBeTruncated,
        IReadOnlyList<string> notIncluded,
        string? packageRevision = null,
        IReadOnlyList<PackageSourceSummary>? sources = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(packageType);
        ArgumentException.ThrowIfNullOrWhiteSpace(source);
        ArgumentNullException.ThrowIfNull(payload);
        ArgumentNullException.ThrowIfNull(notIncluded);
        sources ??= [];
        if (packageType.Length > 80 ||
            source.Length > 500 ||
            packageRevision?.Length > 100 ||
            notIncluded.Count > 50 ||
            notIncluded.Any(item => string.IsNullOrWhiteSpace(item) || item.Length > 500) ||
            sources.Count > 1000)
        {
            throw new ArgumentException("Package manifest metadata exceeds supported limits.");
        }

        var payloadBytes = SerializePayload(payload);
        if (payloadBytes.Length > MaximumPayloadBytes)
        {
            throw new InvalidDataException("The package payload exceeds the 120 MB limit.");
        }
        var manifest = new PackageManifest(
            "twbot-package",
            CurrentSchemaVersion,
            packageType,
            Guid.NewGuid().ToString("N"),
            DateTimeOffset.UtcNow,
            source,
            Convert.ToHexString(SHA256.HashData(payloadBytes)),
            historyMayBeTruncated,
            notIncluded)
        {
            PackageRevision = packageRevision,
            Sources = sources
        };

        using var output = new MemoryStream();
        using (var archive = new ZipArchive(output, ZipArchiveMode.Create, leaveOpen: true))
        {
            WriteEntry(archive, ManifestFileName, JsonSerializer.SerializeToUtf8Bytes(manifest, JsonOptions));
            WriteEntry(archive, PayloadFileName, payloadBytes);
        }
        if (output.Length > MaximumArchiveBytes)
        {
            throw new InvalidDataException("The compressed package exceeds the 128 MB limit.");
        }
        return output.ToArray();
    }

    public static string ComputePayloadSha256<TPayload>(TPayload payload) =>
        Convert.ToHexString(SHA256.HashData(SerializePayload(payload)));

    public static async Task<(PackageManifest Manifest, TPayload Payload)> ReadAsync<TPayload>(
        Stream input,
        string expectedPackageType,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(input);
        ArgumentException.ThrowIfNullOrWhiteSpace(expectedPackageType);
        var archiveBytes = await ReadBoundedAsync(input, MaximumArchiveBytes, cancellationToken);
        using var archiveStream = new MemoryStream(archiveBytes, writable: false);
        using var archive = new ZipArchive(archiveStream, ZipArchiveMode.Read);
        if (archive.Entries.Count != 2 ||
            archive.GetEntry(ManifestFileName) is not { } manifestEntry ||
            archive.GetEntry(PayloadFileName) is not { } payloadEntry ||
            manifestEntry.Length > 1024 * 1024 ||
            payloadEntry.Length > MaximumPayloadBytes)
        {
            throw new InvalidDataException("The package must contain exactly one manifest and one bounded payload.");
        }

        var manifestBytes = await ReadEntryAsync(manifestEntry, 1024 * 1024, cancellationToken);
        var payloadBytes = await ReadEntryAsync(payloadEntry, MaximumPayloadBytes, cancellationToken);
        var manifest = JsonSerializer.Deserialize<PackageManifest>(manifestBytes, JsonOptions)
            ?? throw new InvalidDataException("The package manifest is empty or invalid.");
        if (manifest.Format != "twbot-package" ||
            manifest.SchemaVersion is not (1 or 2 or CurrentSchemaVersion) ||
            manifest.PackageType != expectedPackageType ||
            string.IsNullOrWhiteSpace(manifest.PackageId) ||
            manifest.PackageId.Length > 100 ||
            string.IsNullOrWhiteSpace(manifest.Source) ||
            manifest.Source.Length > 500 ||
            manifest.NotIncluded is null ||
            manifest.NotIncluded.Count > 50 ||
            manifest.NotIncluded.Any(item => string.IsNullOrWhiteSpace(item) || item.Length > 500) ||
            manifest.PackageRevision?.Length > 100 ||
            manifest.Sources is null ||
            manifest.Sources.Count > 1000 ||
            manifest.Sources.Any(source =>
                source is null ||
                string.IsNullOrWhiteSpace(source.RootId) ||
                source.RootId.Length > 200 ||
                string.IsNullOrWhiteSpace(source.Kind) ||
                source.Kind.Length > 100 ||
                source.TotalFiles < 0 ||
                source.MiningCandidates < 0 ||
                source.TooLargeFiles < 0 ||
                source.DocumentsMined < 0 ||
                source.NodesExtracted < 0 ||
                source.TotalBytes < 0 ||
                source.Coverage is null ||
                source.Coverage.DecisionContextDocuments < 0 ||
                source.Coverage.DecisionContextEvidenceBySignal is null ||
                source.Coverage.DecisionContextEvidenceBySignal.Count > 100 ||
                source.Coverage.DecisionContextEvidenceBySignal.Any(pair =>
                    string.IsNullOrWhiteSpace(pair.Key) || pair.Key.Length > 100 || pair.Value < 0) ||
                source.Coverage.TriggerRegistrations < 0 ||
                source.Coverage.UniqueTriggerKinds < 0 ||
                source.Coverage.DocumentsWithTriggers < 0 ||
                source.Coverage.HandlerResponseCandidates < 0 ||
                source.Coverage.PossibleRecoverySignals < 0 ||
                source.Coverage.ScriptArtifacts < 0 ||
                source.Coverage.SubroutineDefinitions < 0 ||
                source.Coverage.DocumentsWithPurposeContext < 0 ||
                source.Coverage.DocumentsWithGoalContext < 0 ||
                source.Coverage.DocumentsWithTypicalUsageContext < 0 ||
                source.Coverage.DocumentsWithTriggerContext < 0 ||
                source.Coverage.DocumentsWithStoppingConditionContext < 0 ||
                source.Coverage.StoppingConditionEvidence < 0 ||
                source.Coverage.DocumentsWithFailureConditionContext < 0 ||
                source.Coverage.FailureConditionEvidence < 0 ||
                source.Coverage.DocumentsWithPreconditionContext < 0 ||
                source.Coverage.DocumentsWithValidationContext < 0 ||
                source.Coverage.DocumentsWithRetryContext < 0 ||
                source.Coverage.DocumentsWithResumeContext < 0 ||
                source.Coverage.DocumentsWithAbortContext < 0 ||
                source.Coverage.DocumentsWithEscalationContext < 0 ||
                source.Coverage.DocumentsWithCheckpointContext < 0 ||
                source.Coverage.DocumentsWithRecoveryContext < 0 ||
                source.Coverage.DocumentsWithVerificationLoopContext < 0 ||
                source.Coverage.ExplicitUrgencyAnnotations < 0 ||
                source.Coverage.PreconditionCheckDocuments < 0 ||
                source.Coverage.ResultVerificationDocuments < 0 ||
                source.Coverage.DocumentationFilesInventoried < 0 ||
                source.Coverage.DocumentationDocumentsMined < 0 ||
                source.Coverage.LogFilesInventoried < 0 ||
                source.Coverage.MenuStructuresObserved < 0 ||
                source.Coverage.MenuOptionsObserved < 0 ||
                source.Coverage.MenuDisplayObservations < 0 ||
                !Enum.IsDefined(source.Coverage.MenuCoverage) ||
                !Enum.IsDefined(source.Coverage.LogContentCoverage)))
        {
            throw new InvalidDataException("The package manifest is unsupported or malformed.");
        }

        byte[] expectedHash;
        try
        {
            expectedHash = Convert.FromHexString(manifest.PayloadSha256);
        }
        catch (FormatException exception)
        {
            throw new InvalidDataException("The package payload hash is malformed.", exception);
        }
        var actualHash = SHA256.HashData(payloadBytes);
        if (expectedHash.Length != actualHash.Length ||
            !CryptographicOperations.FixedTimeEquals(expectedHash, actualHash))
        {
            throw new InvalidDataException("The package payload hash does not match its manifest.");
        }

        var payload = JsonSerializer.Deserialize<TPayload>(payloadBytes, JsonOptions)
            ?? throw new InvalidDataException("The package payload is empty or invalid.");
        return (manifest, payload);
    }

    private static void WriteEntry(ZipArchive archive, string name, byte[] bytes)
    {
        var entry = archive.CreateEntry(name, CompressionLevel.Optimal);
        using var stream = entry.Open();
        stream.Write(bytes);
    }

    private static byte[] SerializePayload<TPayload>(TPayload payload) =>
        JsonSerializer.SerializeToUtf8Bytes(payload, JsonOptions);

    private static async Task<byte[]> ReadBoundedAsync(
        Stream input,
        int maximumBytes,
        CancellationToken cancellationToken)
    {
        using var output = new MemoryStream();
        var buffer = new byte[16 * 1024];
        while (true)
        {
            var read = await input.ReadAsync(buffer, cancellationToken);
            if (read == 0)
            {
                break;
            }
            if (output.Length + read > maximumBytes)
            {
                throw new InvalidDataException("The package exceeds the maximum allowed size.");
            }
            await output.WriteAsync(buffer.AsMemory(0, read), cancellationToken);
        }
        return output.ToArray();
    }

    private static async Task<byte[]> ReadEntryAsync(
        ZipArchiveEntry entry,
        int maximumBytes,
        CancellationToken cancellationToken)
    {
        await using var input = entry.Open();
        return await ReadBoundedAsync(input, maximumBytes, cancellationToken);
    }
}
