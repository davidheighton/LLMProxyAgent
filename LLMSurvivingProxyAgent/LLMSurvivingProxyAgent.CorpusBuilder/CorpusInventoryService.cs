using LLMSurvivingProxyAgent.Knowledge;
using LLMSurvivingProxyAgent.Packaging;

namespace LLMSurvivingProxyAgent.CorpusBuilder;

public sealed class CorpusInventoryService
{
    private static readonly HashSet<string> DocumentationExtensions = new(StringComparer.OrdinalIgnoreCase)
    {
        ".doc", ".docx", ".html", ".htm", ".md", ".pdf", ".rtf", ".tid", ".txt"
    };

    private static readonly HashSet<string> LogExtensions = new(StringComparer.OrdinalIgnoreCase)
    {
        ".jsonl", ".log"
    };

    private static readonly HashSet<string> ExcludedInventoryDirectories = new(StringComparer.OrdinalIgnoreCase)
    {
        ".git", ".vs", "bin", "obj", "node_modules", "packages", "vendor",
        "dist", "build", "out", "coverage", ".venv", "__pycache__", ".cache"
    };

    public PackageSourceSummary Inspect(
        CorpusRoot root,
        bool inventoryOnly = false,
        IReadOnlyList<string>? nestedRootsToExclude = null)
    {
        ArgumentNullException.ThrowIfNull(root);
        ArgumentException.ThrowIfNullOrWhiteSpace(root.Id);
        ArgumentException.ThrowIfNullOrWhiteSpace(root.Path);
        nestedRootsToExclude ??= [];
        var rootPath = Path.GetFullPath(root.Path);
        if (!Directory.Exists(rootPath))
        {
            throw new DirectoryNotFoundException($"Corpus source root does not exist: {rootPath}");
        }

        var filesByExtension = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        var documentationFilesByRole = new Dictionary<string, int>(StringComparer.Ordinal);
        var documentationSamplesByRole = new Dictionary<string, List<string>>(StringComparer.Ordinal);
        var directories = new Stack<string>();
        directories.Push(rootPath);
        var totalFiles = 0;
        var miningCandidates = 0;
        var tooLargeFiles = 0;
        var excludedDirectories = 0;
        var skippedReparsePoints = 0;
        long totalBytes = 0;

        while (directories.Count > 0)
        {
            var currentDirectory = directories.Pop();
            foreach (var childDirectory in Directory.EnumerateDirectories(currentDirectory)
                .OrderByDescending(path => path, StringComparer.OrdinalIgnoreCase))
            {
                var info = new DirectoryInfo(childDirectory);
                if ((info.Attributes & FileAttributes.ReparsePoint) != 0)
                {
                    skippedReparsePoints++;
                }
                else if (nestedRootsToExclude.Any(path =>
                    string.Equals(Path.GetFullPath(childDirectory), path, StringComparison.OrdinalIgnoreCase)))
                {
                    excludedDirectories++;
                }
                else if (ExcludedInventoryDirectories.Contains(info.Name))
                {
                    excludedDirectories++;
                }
                else
                {
                    directories.Push(childDirectory);
                }
            }

            foreach (var filePath in Directory.EnumerateFiles(currentDirectory)
                .OrderBy(path => path, StringComparer.OrdinalIgnoreCase))
            {
                if ((File.GetAttributes(filePath) & FileAttributes.ReparsePoint) != 0)
                {
                    skippedReparsePoints++;
                    continue;
                }

                var fileInfo = new FileInfo(filePath);
                var extension = Path.GetExtension(filePath);
                if (string.IsNullOrWhiteSpace(extension))
                {
                    extension = "(no extension)";
                }
                extension = extension.ToLowerInvariant();
                totalFiles++;
                totalBytes = checked(totalBytes + fileInfo.Length);
                filesByExtension[extension] = filesByExtension.GetValueOrDefault(extension) + 1;
                if (DocumentationExtensions.Contains(extension))
                {
                    var relativePath = Path.GetRelativePath(rootPath, filePath).Replace('\\', '/');
                    var role = DocumentationSourceClassifier.ClassifyPath(relativePath).ToString();
                    documentationFilesByRole[role] = documentationFilesByRole.GetValueOrDefault(role) + 1;
                    if (!documentationSamplesByRole.TryGetValue(role, out var samples))
                    {
                        samples = [];
                        documentationSamplesByRole.Add(role, samples);
                    }
                    if (samples.Count < 3)
                    {
                        samples.Add(relativePath);
                    }
                }

                var isSavedGameLog = root.Kind == CorpusSourceKind.SavedGameLogs &&
                    string.Equals(extension, ".log", StringComparison.OrdinalIgnoreCase);
                var isArchive = KnowledgeSourcePolicy.IsSupportedArchiveFile(filePath);
                var isCandidate = !inventoryOnly &&
                    root.Kind != CorpusSourceKind.RuntimeArtifacts &&
                    (isSavedGameLog ||
                     root.Kind != CorpusSourceKind.SavedGameLogs &&
                     (KnowledgeSourcePolicy.IsSupportedSourceFile(filePath, root.Id) || isArchive));
                if (isCandidate)
                {
                    miningCandidates++;
                    var maximumBytes = isSavedGameLog
                        ? SavedGameLogMiningService.MaximumLogBytes
                        : isArchive
                            ? KnowledgeSourcePolicy.MaximumArchiveBytes
                            : KnowledgeSourcePolicy.MaximumSourceBytes;
                    if (fileInfo.Length > maximumBytes)
                    {
                        tooLargeFiles++;
                    }
                }
            }
        }

        var documentationFiles = filesByExtension
            .Where(pair => DocumentationExtensions.Contains(pair.Key))
            .Sum(pair => pair.Value);
        var logFiles = filesByExtension
            .Where(pair => LogExtensions.Contains(pair.Key))
            .Sum(pair => pair.Value);
        return new PackageSourceSummary(
            root.Id,
            root.Kind.ToString(),
            totalFiles,
            miningCandidates,
            tooLargeFiles,
            totalFiles - miningCandidates + tooLargeFiles,
            excludedDirectories,
            skippedReparsePoints,
            0,
            0,
            totalBytes,
            filesByExtension.OrderBy(pair => pair.Key, StringComparer.Ordinal)
                .ToDictionary(pair => pair.Key, pair => pair.Value, StringComparer.Ordinal),
            new Dictionary<string, int>(StringComparer.Ordinal),
            inventoryOnly || root.Kind == CorpusSourceKind.RuntimeArtifacts)
        {
            Coverage = new CorpusCoverageMetrics
            {
                DocumentationFilesInventoried = documentationFiles,
                DocumentationFilesInventoriedByRole = documentationFilesByRole,
                DocumentationSamplesByRole = documentationSamplesByRole.ToDictionary(
                    pair => pair.Key,
                    pair => (IReadOnlyList<string>)pair.Value
                        .OrderBy(path => path, StringComparer.Ordinal)
                        .ToArray(),
                    StringComparer.Ordinal),
                LogFilesInventoried = logFiles,
                LogContentCoverage = root.Kind is CorpusSourceKind.RuntimeArtifacts or CorpusSourceKind.SavedGameLogs || logFiles > 0
                    ? CorpusCoverageStatus.InventoryOnly
                    : CorpusCoverageStatus.NotMeasured
            }
        };
    }
}
