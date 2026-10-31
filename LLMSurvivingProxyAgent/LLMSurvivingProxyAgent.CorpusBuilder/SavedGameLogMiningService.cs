using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using LLMSurvivingProxyAgent.Knowledge;

namespace LLMSurvivingProxyAgent.CorpusBuilder;

public sealed class SavedGameLogMiningService
{
    public const long MaximumLogBytes = 1024L * 1024 * 1024;

    private const int MaximumLineLength = 4096;
    private const int MaximumMenuLineGap = 8;
    private const int MinimumMenuRows = 2;
    private const int MinimumOptions = 4;
    private const int MinimumDisplayObservations = 2;

    private static readonly Regex AnsiSequence = new(
        @"\x1B\[[0-?]*[ -/]*[@-~]",
        RegexOptions.Compiled);

    private static readonly Regex MenuOption = new(
        @"<(?<key>[^<>]{1,3})>\s*(?<label>[^<>º]+?)(?=\s*<|º|$)",
        RegexOptions.Compiled);

    private readonly IKnowledgeStore _knowledgeStore;

    public SavedGameLogMiningService(IKnowledgeStore knowledgeStore)
    {
        _knowledgeStore = knowledgeStore ?? throw new ArgumentNullException(nameof(knowledgeStore));
    }

    public async Task<KnowledgeMiningReport> MineRootAsync(
        CorpusRoot root,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(root);
        ArgumentException.ThrowIfNullOrWhiteSpace(root.Id);
        ArgumentException.ThrowIfNullOrWhiteSpace(root.Path);
        if (root.Kind != CorpusSourceKind.SavedGameLogs)
        {
            throw new ArgumentException("Saved-game log mining requires a SavedGameLogs root.", nameof(root));
        }

        var rootPath = Path.GetFullPath(root.Path);
        if (!Directory.Exists(rootPath))
        {
            throw new DirectoryNotFoundException($"Saved-game log root does not exist: {rootPath}");
        }

        var files = EnumerateLogFiles(rootPath).ToArray();
        var mined = 0;
        var tooLarge = 0;
        var nodesCreated = 0;
        var menuStructures = 0;
        var menuOptions = 0;
        var menuDisplays = 0;

        foreach (var filePath in files)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (new FileInfo(filePath).Length > MaximumLogBytes)
            {
                tooLarge++;
                continue;
            }

            var extraction = await ExtractMenusAsync(filePath, cancellationToken);
            if (extraction.Menus.Count == 0)
            {
                continue;
            }

            var relativePath = CreatePrivateRelativePath(rootPath, filePath);
            var document = CreateDocument(root.Id, relativePath, extraction);
            await _knowledgeStore.ReplaceDocumentAsync(document, cancellationToken);
            mined++;
            nodesCreated += document.Nodes.Count;
            menuStructures += document.Nodes.Count;
            menuOptions += document.Nodes.Sum(node =>
                node.Evidence.Count(evidence => evidence.Signal == "menu option"));
            menuDisplays += extraction.Menus.Sum(menu => menu.DisplayCount);
        }

        return new KnowledgeMiningReport(
            root.Id,
            files.Length,
            mined,
            0,
            tooLarge,
            nodesCreated)
        {
            MenuStructuresObserved = menuStructures,
            MenuOptionsObserved = menuOptions,
            MenuDisplayObservations = menuDisplays
        };
    }

    private static async Task<MenuExtraction> ExtractMenusAsync(
        string filePath,
        CancellationToken cancellationToken)
    {
        var observedMenus = new Dictionary<string, MenuAccumulator>(StringComparer.Ordinal);
        var currentRows = new List<MenuRow>();
        var lineNumber = 0;
        var lastMenuLine = 0;

        await using var stream = new FileStream(
            filePath,
            FileMode.Open,
            FileAccess.Read,
            FileShare.Read,
            bufferSize: 64 * 1024,
            useAsync: true);
        using var reader = new StreamReader(stream, Encoding.UTF8, detectEncodingFromByteOrderMarks: true);

        while (await reader.ReadLineAsync(cancellationToken) is { } rawLine)
        {
            cancellationToken.ThrowIfCancellationRequested();
            lineNumber++;

            if (lineNumber - lastMenuLine > MaximumMenuLineGap)
            {
                AddCurrentMenu(observedMenus, currentRows);
                currentRows.Clear();
            }

            var row = ParseMenuRow(rawLine, lineNumber);
            if (row is null)
            {
                continue;
            }

            currentRows.Add(row);
            lastMenuLine = lineNumber;
        }

        AddCurrentMenu(observedMenus, currentRows);
        var menus = observedMenus.Values
            .Where(menu => menu.DisplayCount >= MinimumDisplayObservations)
            .OrderBy(menu => menu.Signature, StringComparer.Ordinal)
            .Select(menu => menu.ToObservedMenu())
            .ToArray();
        return new MenuExtraction(menus);
    }

    private static MenuRow? ParseMenuRow(string rawLine, int lineNumber)
    {
        if (!rawLine.StartsWith("[RX-LINE ", StringComparison.Ordinal))
        {
            return null;
        }

        var payloadStart = rawLine.IndexOf("] ", StringComparison.Ordinal);
        if (payloadStart < 0)
        {
            return null;
        }

        var text = AnsiSequence.Replace(rawLine[(payloadStart + 2)..], string.Empty);
        if (text.Length > MaximumLineLength || !text.Contains('º'))
        {
            return null;
        }

        var options = MenuOption.Matches(text)
            .Select(match => new MenuOptionValue(
                match.Groups["key"].Value.Trim(),
                Regex.Replace(match.Groups["label"].Value.Trim(), @"\s+", " ")))
            .Where(option => option.Key.Length is > 0 and <= 3 && option.Label.Length is > 0 and <= 80)
            .DistinctBy(option => option.CanonicalValue, StringComparer.Ordinal)
            .ToArray();
        return options.Length >= 2
            ? new MenuRow(lineNumber, options)
            : null;
    }

    private static void AddCurrentMenu(
        IDictionary<string, MenuAccumulator> observedMenus,
        IReadOnlyList<MenuRow> rows)
    {
        if (rows.Count < MinimumMenuRows)
        {
            return;
        }

        var uniqueRows = rows
            .DistinctBy(row => row.CanonicalValue, StringComparer.Ordinal)
            .ToArray();
        var options = uniqueRows.SelectMany(row => row.Options).ToArray();
        if (options.Length < MinimumOptions)
        {
            return;
        }

        var signature = string.Join(
            "\n",
            uniqueRows.Select(row => row.CanonicalValue));
        if (observedMenus.TryGetValue(signature, out var menu))
        {
            menu.AddObservation(rows[0].LineNumber);
            return;
        }

        observedMenus.Add(signature, new MenuAccumulator(signature, uniqueRows, rows[0].LineNumber));
    }

    private static string CreatePrivateRelativePath(string rootPath, string filePath)
    {
        var relativePath = Path.GetRelativePath(rootPath, filePath).Replace('\\', '/');
        var pathHash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(relativePath)))[..16]
            .ToLowerInvariant();
        return $"captures/{pathHash}.log";
    }

    private static KnowledgeDocument CreateDocument(
        string rootId,
        string relativePath,
        MenuExtraction extraction)
    {
        var documentId = StableId($"saved-game-log\n{rootId}\n{relativePath}");
        var nodes = extraction.Menus.Select(menu =>
        {
            var title = menu.Options.Any(option =>
                    string.Equals(option.Label, "Main Menu Help", StringComparison.OrdinalIgnoreCase))
                ? "Menu: Main Command"
                : menu.Options.Any(option =>
                        string.Equals(option.Label, "Computer Help", StringComparison.OrdinalIgnoreCase)) &&
                    menu.Options.Any(option =>
                        string.Equals(option.Label, "Exit Computer", StringComparison.OrdinalIgnoreCase))
                    ? "Menu: Onboard Computer"
                    : "Menu: Observed Key Choices";
            var evidence = menu.Rows
                .SelectMany(row => row.Options.Select(option => new KnowledgeEvidence(
                    row.LineNumber,
                    "menu option",
                    $"<{option.Key}> {option.Label}")))
                .DistinctBy(item => item.Text, StringComparer.Ordinal)
                .ToList();
            foreach (var observationLine in menu.ObservationLines)
            {
                evidence.Add(new KnowledgeEvidence(
                    observationLine,
                    "repeated menu display",
                    "Captured menu layout; raw transcript text omitted."));
            }

            var summary =
                $"Observed {menu.DisplayCount} repeated incoming menu displays with {menu.Options.Count} distinct key/label options. Menu transitions and runtime availability are not inferred.";
            var nodeId = StableId($"{documentId}\n{menu.Signature}");
            return new KnowledgeNode(
                nodeId,
                documentId,
                KnowledgeCategory.MenuStructure,
                title,
                summary,
                0.9m,
                ExtractionBasis.StaticStructure,
                evidence);
        }).ToArray();

        var canonical = string.Join(
            "\n",
            extraction.Menus.Select(menu => $"{menu.Signature}\n{menu.DisplayCount}"));
        var contentHash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(canonical)));
        return new KnowledgeDocument(
            documentId,
            rootId,
            relativePath,
            "saved-game-log.log",
            contentHash,
            "saved-game-log",
            DateTimeOffset.UtcNow,
            nodes,
            []);
    }

    private static string StableId(string value) =>
        Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(value))).ToLowerInvariant();

    private static IEnumerable<string> EnumerateLogFiles(string rootPath)
    {
        var directories = new Stack<string>();
        directories.Push(rootPath);
        while (directories.Count > 0)
        {
            var current = directories.Pop();
            foreach (var childDirectory in Directory.EnumerateDirectories(current)
                .OrderByDescending(path => path, StringComparer.OrdinalIgnoreCase))
            {
                var info = new DirectoryInfo(childDirectory);
                if ((info.Attributes & FileAttributes.ReparsePoint) == 0)
                {
                    directories.Push(childDirectory);
                }
            }

            foreach (var filePath in Directory.EnumerateFiles(current, "*.log")
                .OrderBy(path => path, StringComparer.OrdinalIgnoreCase))
            {
                if ((File.GetAttributes(filePath) & FileAttributes.ReparsePoint) == 0)
                {
                    yield return filePath;
                }
            }
        }
    }

    private sealed record MenuExtraction(IReadOnlyList<ObservedMenu> Menus);

    private sealed record MenuOptionValue(string Key, string Label)
    {
        public string CanonicalValue => $"{Key}\u001F{Label}";
    }

    private sealed record MenuRow(int LineNumber, IReadOnlyList<MenuOptionValue> Options)
    {
        public string CanonicalValue => string.Join(
            "\u001E",
            Options.Select(option => option.CanonicalValue));
    }

    private sealed record ObservedMenu(
        string Signature,
        IReadOnlyList<MenuRow> Rows,
        IReadOnlyList<MenuOptionValue> Options,
        int DisplayCount,
        IReadOnlyList<int> ObservationLines);

    private sealed class MenuAccumulator(string signature, IReadOnlyList<MenuRow> rows, int firstLine)
    {
        private readonly List<int> _observationLines = [firstLine];

        public string Signature { get; } = signature;
        public IReadOnlyList<MenuRow> Rows { get; } = rows;
        public int DisplayCount { get; private set; } = 1;

        public void AddObservation(int lineNumber)
        {
            DisplayCount++;
            _observationLines.Add(lineNumber);
        }

        public ObservedMenu ToObservedMenu()
        {
            var options = Rows.SelectMany(row => row.Options)
                .DistinctBy(option => option.CanonicalValue, StringComparer.Ordinal)
                .ToArray();
            return new ObservedMenu(Signature, Rows, options, DisplayCount, _observationLines);
        }
    }
}
