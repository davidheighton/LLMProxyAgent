using System.IO.Compression;
using System.Security.Cryptography;
using System.Net;
using System.Text;
using System.Text.RegularExpressions;

namespace LLMSurvivingProxyAgent.Knowledge;

public sealed class KnowledgeMiningService
{
    private const long MaximumSourceBytes = KnowledgeSourcePolicy.MaximumSourceBytes;
    private const string ExtractionVersion = "decision-context-v5";
    private static readonly Regex TiddlerMetadataLine = new(
        @"^\s*(?<name>title|tags|type)\s*:\s*(?<value>.*)$",
        RegexOptions.IgnoreCase | RegexOptions.Compiled | RegexOptions.CultureInvariant);
    private static readonly Regex TelixKeyBinding = new(
        """^\s*(?<key>\d+)\s*=\s*["']?(?<script>@[A-Za-z0-9_.-]+)["']?\s*$""",
        RegexOptions.Compiled | RegexOptions.CultureInvariant);

    private readonly IKnowledgeStore _knowledgeStore;
    private readonly IReadOnlyList<KnowledgeSourceRoot> _configuredRoots;

    private static readonly (string Name, string Pattern)[] Mechanics =
    [
        ("Port trading", @"\b(port|ports|trade|trading|haggle|buy|sell)\b"),
        ("Sector navigation", @"\b(sector|sectors|warp|warps|route|pathfinding|course)\b"),
        ("StarDock operations", @"\b(stardock|star dock|dock menu)\b"),
        ("CIM intelligence", @"\b(cim|computer information menu|port cim|warp cim)\b"),
        ("Colonization", @"\b(coloni[sz]|genesis torpedo|planet building)\w*\b"),
        ("Fighter grids", @"\b(fighter grid|fighters|defensive grid|grid work)\b"),
        ("Planet operations", @"\b(planet|citadel|colonist|terra)\w*\b"),
        ("Combat", @"\b(combat|attack|photon|torpedo|pdrop|surround)\b"),
        ("Ship refitting", @"\b(refurb|upgrade|holds|shields|scanner|restock)\w*\b"),
        ("Bust avoidance", @"\b(bust|busted|steal factor|robbery|avoid bust)\b"),
        ("Turns and credits", @"\b(turns?|credits?|reserve|bankroll)\b")
    ];

    private static readonly Regex TacticalLanguage = new(
        @"\b(avoid|before|after|if|when|unless|only|never|must|should|prefer|risk|safe|verify|check|require|return|recover|retry|reserve)\b",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);

    private static readonly Regex StrategicLanguage = new(
        @"\b(goal|objective|strategy|strategic|profit|income|bootstrap|long[- ]term|priorit[yi]ze|expand|maximize|minimize|reserve)\b",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);

    private static readonly Regex GameVocabulary = new(
        @"\b(port|trade|sector|warp|route|stardock|credits?|turns?|fighter|ship|planet|colonist|cim|ore|combat|photon|bust|shield|scanner)\b",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);

    private static readonly Regex Label = new(
        @"^\s*:([A-Za-z0-9_~.-]+)\s*$",
        RegexOptions.Compiled);

    private static readonly Regex GoSub = new(
        @"\bgosub\s+:?([A-Za-z0-9_~.-]+)",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);

    private static readonly Regex TriggerRegistrationPattern = new(
        @"^\s*set(?<kind>[A-Za-z]+)trigger\s+(?<name>\S+)\s+:?(?<handler>[A-Za-z0-9_~.-]+)(?<arguments>.*)$",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);

    private static readonly Regex UrgencyAnnotation = new(
        @"\b(emergency|urgent|immediate|immediately|critical|reactive)\b",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);

    private static readonly Regex FailureHandlingCandidate = new(
        @"\b(recover\w*|retry\w*|restore\w*|disconnect\w*|fail(?:ure|ed)?|error\w*|cleanup\w*|bust\w*|fallback\w*|abort\w*|cancel\w*|timeout\w*)\b",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);

    private static readonly Regex WorldFact = new(
        @"\bsector\s+(?<sector>\d+)\s+(?:warps?\s+to|connects?\s+to)\s+(?<warps>\d+(?:\s*[, ]\s*\d+)*)",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);

    private static readonly Regex StarDockFact = new(
        @"\bstardock\b.{0,32}?\bsector\s+(?<sector>\d+)|\bsector\s+(?<sector2>\d+).{0,32}?\bstardock\b",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);

    private static readonly Regex SecretLikeValue = new(
        @"(?i)\b(password|passwd|secret|token|api[_ -]?key|corp[_ -]?pw|sschannel)\b(\s*[:=]\s*|\s+)([""']?)[^\s""'`,;]+",
        RegexOptions.Compiled);

    private static readonly Regex HtmlNonText = new(
        @"<!--.*?-->|<(script|style)\b[^>]*>.*?</\1\s*>",
        RegexOptions.IgnoreCase | RegexOptions.Singleline | RegexOptions.Compiled | RegexOptions.CultureInvariant);

    private static readonly Regex HtmlTag = new(
        @"<[^>]*>",
        RegexOptions.Singleline | RegexOptions.Compiled | RegexOptions.CultureInvariant);

    private static readonly (string Name, string Pattern, string Summary)[] ConstructionPatterns =
    [
        (
            "Precondition checks",
            @"\b(if|ifnot|unless|require|check location|precondition)\b",
            "Conditional checks gate later behavior; the exact safety meaning needs review."),
        (
            "Persistent configuration",
            @"\b(loadvar|savevar|loadstate|savestate|persist)\b",
            "The routine reads or writes saved configuration/state."),
        (
            "Route validation",
            @"\b(route|path|warp|course|sector)\b.*\b(if|unless|avoid|valid|known|check|verify)\b|\b(if|unless)\b.*\b(route|path|warp|course|sector)\b",
            "Navigation data is coupled to a validation or guard condition."),
        (
            "Failure recovery",
            @"\b(recover|retry|restore|disconnect|disconnected|failure|failed|error|cleanup|bust|fallback)\b",
            "Failure, recovery, or fallback handling is present."),
        (
            "Prompt and event handling",
            @"\b(set\w*trigger|waitfor|waitforregeX|waitfortext|ontext|prompt|seteventtrigger)\b",
            "The routine coordinates with prompts, triggers, or asynchronous events."),
        (
            "Result verification",
            @"\b(verify|confirmed|confirmation|success|successful|complete|received|result)\b",
            "The routine contains an explicit result or completion check.")
    ];

    private static readonly (string Signal, Regex Pattern)[] DocumentationDecisionPatterns =
    [
        ("intent", DocumentationPattern(@"\b(because|reason(?:ing)?|why|purpose|intended to|in order to)\b")),
        ("objective", DocumentationPattern(@"\b(goal|objective|aim)\b|\btarget (?:goal|objective)\b")),
        ("usage conditions", DocumentationPattern(@"\b(when to use|useful when|best used when|most effective when)\b|\b(use|consider)\b.{0,80}\b(when|if|provided|assuming)\b")),
        ("avoidance conditions", DocumentationPattern(@"(?<!an )\bavoid\b.{0,80}\b(when|if)\b|\b(do not use|don't use|never use|not recommended|not suitable)\b")),
        ("assumptions", DocumentationPattern(@"\b(assum(?:e|ing|ption)|assumption)\b")),
        ("prerequisites", DocumentationPattern(@"\b(precondition|prerequisite|required|requires|must have|needs?)\b")),
        ("risks", DocumentationPattern(@"\b(risk|danger|drawback|penalty|downside|threat)\b|\b(may cause|can cause|can lead to|exposes? .{0,40} to)\b")),
        ("benefits", DocumentationPattern(@"\b(benefit|advantage|beneficial|efficient|high[- ]credit|high[- ]profit|profit potential|earning potential)\b")),
        ("expected outcomes", DocumentationPattern(@"\b(expected outcome|expected result|outcome|results? in|produces?|generates?|yields?|increases?|improves?)\b")),
        ("alternatives", DocumentationPattern(@"\b(alternative|instead of|rather than|another approach|another way)\b")),
        ("counterexamples", DocumentationPattern(@"\b(counterexample|exception to|exception when|unless|not when|does not apply|doesn't apply|fails when)\b")),
        ("stopping conditions", DocumentationPattern(@"\b(stop when|stop if|halt when|stopping condition)\b")),
        ("escalation conditions", DocumentationPattern(@"\b(escalat\w*|notify|alert|emergency|urgent)\b")),
        ("validation checks", DocumentationPattern(@"\b(validate|verify|confirm|ensure|check whether|check that)\b")),
        ("retry patterns", DocumentationPattern(@"\b(retry|try again|repeat (?:the|this|a) )\b")),
        ("resume patterns", DocumentationPattern(@"\b(resume (?:from|after|when)|recover (?:from|after|when)|restore (?:from|after)|checkpoint)\b")),
        ("abort conditions", DocumentationPattern(@"\b(abort|cancel|terminate)\b.{0,60}\b(if|when)\b|\b(abort|cancel|termination) condition\b")),
        ("recovery checkpoints", DocumentationPattern(@"\b(recovery checkpoint|checkpoint|saved state|save point)\b")),
        ("verification loops", DocumentationPattern(@"\b(recheck|re-check|verify again|repeat(?:ed)? verification|verification loop)\b"))
    ];

    public KnowledgeMiningService(
        IKnowledgeStore knowledgeStore,
        IEnumerable<KnowledgeSourceRoot> configuredRoots)
    {
        _knowledgeStore = knowledgeStore ?? throw new ArgumentNullException(nameof(knowledgeStore));
        ArgumentNullException.ThrowIfNull(configuredRoots);
        _configuredRoots = configuredRoots
            .Select(root =>
            {
                ArgumentException.ThrowIfNullOrWhiteSpace(root.Id);
                ArgumentException.ThrowIfNullOrWhiteSpace(root.Path);
                return new KnowledgeSourceRoot(root.Id, Path.GetFullPath(root.Path));
            })
            .ToArray();
        if (_configuredRoots.Select(root => root.Id).Distinct(StringComparer.Ordinal).Count() != _configuredRoots.Count)
        {
            throw new ArgumentException("Knowledge source root identifiers must be unique.", nameof(configuredRoots));
        }
    }

    public IReadOnlyList<KnowledgeSourceRoot> ConfiguredRoots => _configuredRoots;

    public async Task<IReadOnlyList<KnowledgeMiningReport>> MineConfiguredRootsAsync(
        CancellationToken cancellationToken = default)
    {
        var reports = new List<KnowledgeMiningReport>(_configuredRoots.Count);
        foreach (var root in _configuredRoots)
        {
            reports.Add(await MineDirectoryAsync(root, cancellationToken));
        }
        return reports;
    }

    private async Task<KnowledgeMiningReport> MineDirectoryAsync(
        KnowledgeSourceRoot root,
        CancellationToken cancellationToken)
    {
        var rootId = root.Id;
        var resolvedRoot = root.Path;
        if (!Directory.Exists(resolvedRoot))
        {
            throw new DirectoryNotFoundException($"Knowledge source root does not exist: {resolvedRoot}");
        }

        var files = EnumerateSourceFiles(resolvedRoot, rootId).ToArray();
        var archives = EnumerateArchiveFiles(resolvedRoot).ToArray();
        var observedContentHashes = new HashSet<string>(StringComparer.Ordinal);
        var mined = 0;
        var unchanged = 0;
        var tooLarge = 0;
        var nodesCreated = 0;
        var archiveFilesTooLarge = 0;
        var archiveEntriesFound = 0;
        var archiveEntriesMined = 0;
        var archiveEntriesDeduplicated = 0;
        var archiveEntriesTooLarge = 0;

        foreach (var filePath in files)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var fileInfo = new FileInfo(filePath);
            if (fileInfo.Length > MaximumSourceBytes)
            {
                tooLarge++;
                continue;
            }

            var bytes = await File.ReadAllBytesAsync(filePath, cancellationToken);
            if (bytes.Length > MaximumSourceBytes)
            {
                tooLarge++;
                continue;
            }
            var sourceHash = Convert.ToHexString(SHA256.HashData(bytes));
            observedContentHashes.Add(sourceHash);
            var contentHash = StableId($"{ExtractionVersion}\n{sourceHash}");
            var relativePath = Path.GetRelativePath(resolvedRoot, filePath).Replace('\\', '/');
            var documentId = StableId($"{rootId}\n{relativePath}");
            if (await _knowledgeStore.HasUnchangedDocumentAsync(documentId, contentHash, cancellationToken))
            {
                unchanged++;
                continue;
            }

            var extension = EffectiveSourceExtension(fileInfo.Name);
            var fileName = DisplaySourceFileName(fileInfo.Name);
            var content = PrepareSourceContent(extension, bytes);
            var (preparedContent, tiddlerLocator) = PrepareTiddlerContent(extension, content);
            var extractDecisionContext =
                DocumentationSourceClassifier.ClassifyPath(relativePath) != DocumentationSourceRole.HelpReference;
            var nodes = extension.Equals(".key", StringComparison.OrdinalIgnoreCase)
                ? ExtractTelixKeyBindings(documentId, preparedContent)
                : ExtractNodes(documentId, fileName, extension, preparedContent, extractDecisionContext);
            if (tiddlerLocator is not null)
            {
                nodes = AddSourceLocator(nodes, tiddlerLocator);
            }
            var relations = BuildRelations(nodes);
            var document = new KnowledgeDocument(
                documentId,
                rootId,
                relativePath,
                fileName,
                contentHash,
                LanguageFor(extension),
                DateTimeOffset.UtcNow,
                nodes,
                relations);
            await _knowledgeStore.ReplaceDocumentAsync(document, cancellationToken);
            mined++;
            nodesCreated += nodes.Count;
        }

        foreach (var archivePath in archives)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var archiveInfo = new FileInfo(archivePath);
            if (archiveInfo.Length > KnowledgeSourcePolicy.MaximumArchiveBytes)
            {
                tooLarge++;
                archiveFilesTooLarge++;
                continue;
            }

            await using var archiveStream = new FileStream(
                archivePath,
                FileMode.Open,
                FileAccess.Read,
                FileShare.Read,
                bufferSize: 81920,
                useAsync: true);
            using var archive = new ZipArchive(archiveStream, ZipArchiveMode.Read);
            if (archive.Entries.Count > KnowledgeSourcePolicy.MaximumArchiveEntries)
            {
                tooLarge++;
                archiveFilesTooLarge++;
                continue;
            }

            var entries = archive.Entries
                .Where(entry => KnowledgeSourcePolicy.IsSupportedArchiveEntry(entry.FullName, rootId))
                .OrderBy(entry => entry.FullName, StringComparer.OrdinalIgnoreCase)
                .ToArray();
            archiveEntriesFound += entries.Length;
            if (entries.Sum(entry => entry.Length) > KnowledgeSourcePolicy.MaximumArchiveExpandedBytes)
            {
                tooLarge++;
                archiveFilesTooLarge++;
                continue;
            }

            foreach (var entry in entries)
            {
                cancellationToken.ThrowIfCancellationRequested();
                if (entry.Length > MaximumSourceBytes)
                {
                    archiveEntriesTooLarge++;
                    continue;
                }

                var bytes = await ReadArchiveEntryAsync(entry, cancellationToken);
                var sourceHash = Convert.ToHexString(SHA256.HashData(bytes));
                if (!observedContentHashes.Add(sourceHash))
                {
                    archiveEntriesDeduplicated++;
                    continue;
                }

                var entryPath = entry.FullName.Replace('\\', '/');
                var archiveRelativePath = Path.GetRelativePath(resolvedRoot, archivePath).Replace('\\', '/');
                var relativePath = $"{archiveRelativePath}!/{entryPath}";
                var documentId = StableId($"{rootId}\n{relativePath}");
                var contentHash = StableId($"{ExtractionVersion}\n{sourceHash}");
                if (await _knowledgeStore.HasUnchangedDocumentAsync(documentId, contentHash, cancellationToken))
                {
                    unchanged++;
                    continue;
                }

                var fileName = Path.GetFileName(entryPath);
                var extension = EffectiveSourceExtension(fileName);
                var content = PrepareSourceContent(extension, bytes);
                var (preparedContent, tiddlerLocator) = PrepareTiddlerContent(extension, content);
                var extractDecisionContext =
                    DocumentationSourceClassifier.ClassifyPath(relativePath) != DocumentationSourceRole.HelpReference;
                var nodes = extension.Equals(".key", StringComparison.OrdinalIgnoreCase)
                    ? ExtractTelixKeyBindings(documentId, preparedContent)
                    : ExtractNodes(documentId, fileName, extension, preparedContent, extractDecisionContext);
                if (tiddlerLocator is not null)
                {
                    nodes = AddSourceLocator(nodes, tiddlerLocator);
                }

                var document = new KnowledgeDocument(
                    documentId,
                    rootId,
                    relativePath,
                    fileName,
                    contentHash,
                    LanguageFor(extension),
                    DateTimeOffset.UtcNow,
                    nodes,
                    BuildRelations(nodes));
                await _knowledgeStore.ReplaceDocumentAsync(document, cancellationToken);
                mined++;
                archiveEntriesMined++;
                nodesCreated += nodes.Count;
            }
        }

        return new KnowledgeMiningReport(
            rootId,
            files.Length + archives.Length,
            mined,
            unchanged,
            tooLarge,
            nodesCreated)
        {
            ArchiveFilesScanned = archives.Length,
            ArchiveFilesTooLarge = archiveFilesTooLarge,
            ArchiveEntriesFound = archiveEntriesFound,
            ArchiveEntriesMined = archiveEntriesMined,
            ArchiveEntriesDeduplicated = archiveEntriesDeduplicated,
            ArchiveEntriesTooLarge = archiveEntriesTooLarge
        };
    }

    private static IEnumerable<string> EnumerateSourceFiles(string rootPath, string rootId) =>
        EnumerateFiles(rootPath).Where(path => KnowledgeSourcePolicy.IsSupportedSourceFile(path, rootId));

    private static IEnumerable<string> EnumerateArchiveFiles(string rootPath) =>
        EnumerateFiles(rootPath).Where(KnowledgeSourcePolicy.IsSupportedArchiveFile);

    private static IEnumerable<string> EnumerateFiles(string rootPath)
    {
        var pending = new Stack<string>();
        pending.Push(rootPath);
        while (pending.Count > 0)
        {
            var directory = pending.Pop();
            foreach (var childDirectory in Directory.EnumerateDirectories(directory).OrderByDescending(path => path, StringComparer.OrdinalIgnoreCase))
            {
                var childInfo = new DirectoryInfo(childDirectory);
                if (!KnowledgeSourcePolicy.IsExcludedMiningDirectory(childInfo.Name) &&
                    (childInfo.Attributes & FileAttributes.ReparsePoint) == 0)
                {
                    pending.Push(childDirectory);
                }
            }

            foreach (var file in Directory.EnumerateFiles(directory)
                         .OrderBy(path => path, StringComparer.OrdinalIgnoreCase))
            {
                if ((File.GetAttributes(file) & FileAttributes.ReparsePoint) == 0)
                {
                    yield return file;
                }
            }
        }
    }

    private static async Task<byte[]> ReadArchiveEntryAsync(
        ZipArchiveEntry entry,
        CancellationToken cancellationToken)
    {
        await using var entryStream = entry.Open();
        using var content = new MemoryStream((int)entry.Length);
        var buffer = new byte[81920];
        while (true)
        {
            var bytesRead = await entryStream.ReadAsync(buffer.AsMemory(), cancellationToken);
            if (bytesRead == 0)
            {
                break;
            }

            if (content.Length + bytesRead > MaximumSourceBytes)
            {
                throw new InvalidDataException(
                    $"Archive entry '{entry.FullName}' expands beyond the {MaximumSourceBytes}-byte source limit.");
            }
            await content.WriteAsync(buffer.AsMemory(0, bytesRead), cancellationToken);
        }

        return content.ToArray();
    }

    private static string EffectiveSourceExtension(string fileName)
    {
        if (fileName.EndsWith(".ts.backup", StringComparison.OrdinalIgnoreCase) ||
            fileName.EndsWith(".ts.msfucked", StringComparison.OrdinalIgnoreCase))
        {
            return ".ts";
        }
        return Path.GetExtension(fileName).ToLowerInvariant();
    }

    private static string DisplaySourceFileName(string fileName) =>
        fileName.EndsWith(".ts.backup", StringComparison.OrdinalIgnoreCase) ||
        fileName.EndsWith(".ts.msfucked", StringComparison.OrdinalIgnoreCase)
            ? fileName[..fileName.LastIndexOf('.')]
            : fileName;

    private static (string Content, string? Locator) PrepareTiddlerContent(string extension, string content)
    {
        if (!extension.Equals(".tid", StringComparison.OrdinalIgnoreCase))
        {
            return (content, null);
        }

        var lines = content.Split(["\r\n", "\n", "\r"], StringSplitOptions.None);
        var separatorLine = Array.FindIndex(lines, string.IsNullOrWhiteSpace);
        if (separatorLine < 0)
        {
            return (content, null);
        }

        string? title = null;
        string? tags = null;
        string? type = null;
        foreach (var line in lines.Take(separatorLine))
        {
            var match = TiddlerMetadataLine.Match(line);
            if (!match.Success)
            {
                continue;
            }

            var value = match.Groups["value"].Value.Trim();
            switch (match.Groups["name"].Value.ToLowerInvariant())
            {
                case "title":
                    title = value;
                    break;
                case "tags":
                    tags = value;
                    break;
                case "type":
                    type = value;
                    break;
            }
        }

        var locator = string.Join(
            "; ",
            new[] { ("title", title), ("tags", tags), ("type", type) }
                .Where(item => !string.IsNullOrWhiteSpace(item.Item2))
                .Select(item => $"{item.Item1}={item.Item2}"));
        return (
            string.Concat(Enumerable.Repeat('\n', separatorLine + 1)) +
            string.Join('\n', lines.Skip(separatorLine + 1)),
            locator.Length == 0 ? null : RedactAndLimit(locator));
    }

    private static IReadOnlyList<KnowledgeNode> AddSourceLocator(
        IReadOnlyList<KnowledgeNode> nodes,
        string sourceLocator) =>
        nodes.Select(node => node with
        {
            Evidence = node.Evidence
                .Select(evidence => evidence with { SourceLocator = sourceLocator })
                .ToArray()
        }).ToArray();

    private static IReadOnlyList<KnowledgeNode> ExtractTelixKeyBindings(string documentId, string content)
    {
        var bindings = content.Split(["\r\n", "\n"], StringSplitOptions.None)
            .Select((line, index) => (Line: line, LineNumber: index + 1, Match: TelixKeyBinding.Match(line)))
            .Where(item => item.Match.Success)
            .GroupBy(item => item.Match.Groups["key"].Value, StringComparer.OrdinalIgnoreCase);

        return bindings.Select(group =>
        {
            var scripts = group
                .Select(item => item.Match.Groups["script"].Value)
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .OrderBy(script => script, StringComparer.OrdinalIgnoreCase)
                .ToArray();
            var title = $"Telix key binding: {group.Key}";
            var evidence = group.Select(item => new KnowledgeEvidence(
                item.LineNumber,
                "Telix key-to-script mapping",
                RedactAndLimit(item.Line))).ToArray();
            return new KnowledgeNode(
                StableId($"{documentId}\n{KnowledgeCategory.MacroDefinition}\n{title}"),
                documentId,
                KnowledgeCategory.MacroDefinition,
                title,
                $"Key code {group.Key} maps to {string.Join(", ", scripts)} in the source key map; runtime behavior is not validated.",
                0.85m,
                ExtractionBasis.ExplicitText,
                evidence);
        }).ToArray();
    }

    private static IReadOnlyList<KnowledgeNode> ExtractNodes(
        string documentId,
        string fileName,
        string extension,
        string content,
        bool extractDecisionContext)
    {
        var lines = content.Split(["\r\n", "\n"], StringSplitOptions.None);
        var candidates = new Dictionary<(KnowledgeCategory Category, string Title), Candidate>();
        var codeFile = IsCodeFile(extension);
        var macroTitle = Path.GetFileNameWithoutExtension(fileName);

        var labels = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        var calls = new List<(string Name, int Line, string Text)>();
        var triggerRegistrations = new List<TriggerRegistration>();
        var contextualEvidence = new Dictionary<string, KnowledgeEvidence>(StringComparer.Ordinal);
        var documentationDecisionEvidence = new Dictionary<string, List<(int Line, string Signal, string Text)>>(
            StringComparer.Ordinal);
        var guidanceLines = new List<(int Line, string Text)>();
        var strategicLines = new List<(int Line, string Text)>();
        var mechanicEvidence = new Dictionary<string, List<(int Line, string Text)>>(StringComparer.Ordinal);
        var worldFactLines = new List<(int Line, string Text)>();
        var patternEvidence = ConstructionPatterns.ToDictionary(
            pattern => pattern.Name,
            _ => new List<(int Line, string Text)>(),
            StringComparer.Ordinal);

        for (var index = 0; index < lines.Length; index++)
        {
            var line = lines[index];
            var lineNumber = index + 1;
            var trimmed = line.Trim();
            if (trimmed.Length == 0)
            {
                continue;
            }

            var labelMatch = Label.Match(line);
            if (labelMatch.Success)
            {
                labels[labelMatch.Groups[1].Value] = lineNumber;
            }
            foreach (Match call in GoSub.Matches(line))
            {
                calls.Add((call.Groups[1].Value, lineNumber, line));
            }

            var triggerMatch = TriggerRegistrationPattern.Match(line);
            if (codeFile && triggerMatch.Success)
            {
                triggerRegistrations.Add(new TriggerRegistration(
                    triggerMatch.Groups["kind"].Value,
                    triggerMatch.Groups["name"].Value,
                    triggerMatch.Groups["handler"].Value,
                    triggerMatch.Groups["arguments"].Value.Trim(),
                    lineNumber,
                    line));
            }

            if (codeFile)
            {
                foreach (var pattern in ConstructionPatterns)
                {
                    if (Regex.IsMatch(line, pattern.Pattern, RegexOptions.IgnoreCase | RegexOptions.CultureInvariant) &&
                        patternEvidence[pattern.Name].Count < 6)
                    {
                        patternEvidence[pattern.Name].Add((lineNumber, line));
                    }
                }
            }

            if (!TryGetCommentText(line, extension, out var commentText))
            {
                continue;
            }

            if (TryGetContextSignal(commentText, out var contextSignal))
            {
                contextualEvidence.TryAdd(
                    contextSignal,
                    new KnowledgeEvidence(lineNumber, contextSignal, RedactAndLimit(commentText)));
                if (!codeFile && extractDecisionContext)
                {
                    AddDocumentationDecisionEvidence(
                        documentationDecisionEvidence,
                        DocumentationSignalFor(contextSignal),
                        lineNumber,
                        commentText);
                }
            }
            if (!codeFile && extractDecisionContext && IsDocumentationFile(extension))
            {
                foreach (var (signal, pattern) in DocumentationDecisionPatterns)
                {
                    if (pattern.IsMatch(commentText))
                    {
                        AddDocumentationDecisionEvidence(
                            documentationDecisionEvidence,
                            signal,
                            lineNumber,
                            commentText);
                    }
                }
            }
            if (TacticalLanguage.IsMatch(commentText) && GameVocabulary.IsMatch(commentText))
            {
                guidanceLines.Add((lineNumber, commentText));
            }
            if (StrategicLanguage.IsMatch(commentText) && GameVocabulary.IsMatch(commentText))
            {
                strategicLines.Add((lineNumber, commentText));
            }

            foreach (var mechanic in Mechanics)
            {
                if (Regex.IsMatch(commentText, mechanic.Pattern, RegexOptions.IgnoreCase | RegexOptions.CultureInvariant))
                {
                    if (!mechanicEvidence.TryGetValue(mechanic.Name, out var evidence))
                    {
                        evidence = [];
                        mechanicEvidence.Add(mechanic.Name, evidence);
                    }
                    if (evidence.Count < 6)
                    {
                        evidence.Add((lineNumber, commentText));
                    }
                }
            }

            if (WorldFact.IsMatch(commentText) || StarDockFact.IsMatch(commentText))
            {
                worldFactLines.Add((lineNumber, commentText));
            }
        }

        if (codeFile)
        {
            var sourceEvidence = FirstNonEmptyLine(lines);
            var scriptEvidence = contextualEvidence.Values
                .OrderBy(item => item.LineNumber)
                .Select(item => (item.LineNumber, item.Signal, item.Text))
                .Append((sourceEvidence.LineNumber, "source artifact", sourceEvidence.Text));
            AddEvidenceSignals(
                KnowledgeCategory.MacroDefinition,
                $"Script: {Humanize(macroTitle)}",
                "Code artifact identified by filename; labeled context is preserved as evidence and behavior is not independently validated.",
                0.4m,
                ExtractionBasis.FilenameHeuristic,
                scriptEvidence,
                maximumEvidence: 24);
        }

        foreach (var (signal, evidence) in documentationDecisionEvidence)
        {
            AddEvidenceSignals(
                KnowledgeCategory.DecisionContext,
                $"Decision context: {HumanizeDecisionSignal(signal)}",
                "Documentation contains a passage tagged with this decision-context signal; the source claim is not independently validated.",
                0.52m,
                ExtractionBasis.ExplicitText,
                evidence,
                maximumEvidence: 12);
        }

        foreach (var triggerGroup in triggerRegistrations.GroupBy(
                     trigger => TriggerTitle(trigger.Kind),
                     StringComparer.Ordinal))
        {
            var evidence = new List<(int Line, string Signal, string Text)>();
            foreach (var trigger in triggerGroup)
            {
                evidence.Add((trigger.LineNumber, "trigger registration", trigger.SourceLine));
                evidence.Add((trigger.LineNumber, "configured condition and arguments", trigger.SourceLine));
                evidence.Add((trigger.LineNumber, "handler target", trigger.SourceLine));

                for (var precedingIndex = Math.Max(0, trigger.LineNumber - 4);
                     precedingIndex < trigger.LineNumber - 1;
                     precedingIndex++)
                {
                    var precedingLine = lines[precedingIndex];
                    if (string.IsNullOrWhiteSpace(precedingLine))
                    {
                        continue;
                    }
                    if (TryGetCommentText(precedingLine, extension, out var commentText))
                    {
                        if (UrgencyAnnotation.IsMatch(commentText))
                        {
                            evidence.Add((precedingIndex + 1, "explicit urgency annotation", precedingLine));
                        }
                        continue;
                    }
                    break;
                }

                if (!labels.TryGetValue(trigger.HandlerLabel, out var handlerLine))
                {
                    continue;
                }

                var scannedLines = 0;
                for (var handlerIndex = handlerLine; handlerIndex < lines.Length && scannedLines < 80; handlerIndex++)
                {
                    var handlerSourceLine = lines[handlerIndex];
                    if (Label.IsMatch(handlerSourceLine))
                    {
                        break;
                    }
                    if (string.IsNullOrWhiteSpace(handlerSourceLine) ||
                        TryGetCommentText(handlerSourceLine, extension, out _))
                    {
                        continue;
                    }

                    scannedLines++;
                    var sourceLineNumber = handlerIndex + 1;
                    if (evidence.Count(item => item.Signal == "handler response candidate") < 6)
                    {
                        evidence.Add((sourceLineNumber, "handler response candidate", handlerSourceLine));
                    }
                    if (FailureHandlingCandidate.IsMatch(handlerSourceLine))
                    {
                        evidence.Add((sourceLineNumber, "possible failure or recovery handling", handlerSourceLine));
                    }
                }
            }

            AddEvidenceSignals(
                KnowledgeCategory.ReactiveTriggerPattern,
                $"Trigger: {triggerGroup.Key}",
                "Static trigger registrations with condition and handler candidates. Urgency, runtime firing, outcome, and operational doctrine are not inferred.",
                0.66m,
                ExtractionBasis.StaticStructure,
                evidence);
        }

        foreach (var (label, lineNumber, sourceLine) in calls.DistinctBy(call => call.Name, StringComparer.OrdinalIgnoreCase))
        {
            if (!labels.TryGetValue(label, out var definitionLine))
            {
                continue;
            }
            AddSingle(
                KnowledgeCategory.MacroDefinition,
                $"Subroutine: {Humanize(label)}",
                "TWX subroutine label is called with gosub; tactical purpose is not inferred from the label.",
                0.76m,
                ExtractionBasis.StaticStructure,
                lineNumber,
                $"gosub reference (definition at line {definitionLine})",
                sourceLine);
        }

        foreach (var (mechanic, evidence) in mechanicEvidence)
        {
            AddEvidenceText(
                KnowledgeCategory.GameplayMechanic,
                mechanic,
                "The source text mentions this game mechanic; review the cited passage before treating it as authoritative.",
                0.55m,
                ExtractionBasis.ExplicitText,
                evidence);
        }

        if (guidanceLines.Count > 0)
        {
            AddEvidenceText(
                KnowledgeCategory.TacticalGuidance,
                $"Possible tactical guidance: {Humanize(macroTitle)}",
                "Decision-oriented language was found in comments; intent and game correctness require human review.",
                0.42m,
                ExtractionBasis.ExplicitText,
                guidanceLines.Take(8));
        }

        if (strategicLines.Count > 0)
        {
            AddEvidenceText(
                KnowledgeCategory.StrategicGuidance,
                $"Possible strategic guidance: {Humanize(macroTitle)}",
                "Goal, priority, or economic language was found in comments; strategy is not validated.",
                0.42m,
                ExtractionBasis.ExplicitText,
                strategicLines.Take(8));
        }

        if (worldFactLines.Count > 0)
        {
            AddEvidenceText(
                KnowledgeCategory.WorldKnowledge,
                $"World observations in {Humanize(macroTitle)}",
                "A sector warp or StarDock location statement was found; it may be stale and must not override current world-state data.",
                0.5m,
                ExtractionBasis.ExplicitText,
                worldFactLines.Take(8));
        }

        foreach (var pattern in ConstructionPatterns)
        {
            var evidence = patternEvidence[pattern.Name];
            if (evidence.Count == 0)
            {
                continue;
            }
            AddEvidenceText(
                KnowledgeCategory.MacroConstructionPattern,
                pattern.Name,
                pattern.Summary,
                0.65m,
                ExtractionBasis.StaticStructure,
                evidence);
        }

        return candidates
            .OrderBy(pair => pair.Key.Category)
            .ThenBy(pair => pair.Key.Title, StringComparer.Ordinal)
            .Select(pair =>
            {
                var candidate = pair.Value;
                return new KnowledgeNode(
                    StableId($"{documentId}\n{candidate.Category}\n{candidate.Title}"),
                    documentId,
                    candidate.Category,
                    candidate.Title,
                    candidate.Summary,
                    candidate.Confidence,
                    candidate.Basis,
                    candidate.Evidence);
            })
            .Concat(CooperativeDoctrineExtractor.Extract(documentId, fileName, extension, content))
            .ToArray();

        void AddSingle(
            KnowledgeCategory category,
            string title,
            string summary,
            decimal confidence,
            ExtractionBasis basis,
            int lineNumber,
            string signal,
            string text) =>
            AddEvidenceSignals(category, title, summary, confidence, basis, [(lineNumber, signal, text)]);

        void AddEvidenceText(
            KnowledgeCategory category,
            string title,
            string summary,
            decimal confidence,
            ExtractionBasis basis,
            IEnumerable<(int Line, string Text)> evidence) =>
            AddEvidenceSignals(category, title, summary, confidence, basis, evidence.Select(item =>
                (item.Line, "matched source evidence", item.Text)));

        void AddEvidenceSignals(
            KnowledgeCategory category,
            string title,
            string summary,
            decimal confidence,
            ExtractionBasis basis,
            IEnumerable<(int Line, string Signal, string Text)> evidence,
            int maximumEvidence = 8)
        {
            var key = (category, title);
            if (!candidates.TryGetValue(key, out var candidate))
            {
                candidate = new Candidate(category, title, summary, confidence, basis);
                candidates.Add(key, candidate);
            }
            foreach (var item in evidence)
            {
                if (candidate.Evidence.Count < maximumEvidence &&
                    !candidate.Evidence.Any(existing => existing.LineNumber == item.Line && existing.Signal == item.Signal))
                {
                    candidate.Evidence.Add(new KnowledgeEvidence(
                        item.Line,
                        item.Signal,
                        RedactAndLimit(item.Text)));
                }
            }
        }
    }

    private static IReadOnlyList<KnowledgeRelation> BuildRelations(IReadOnlyList<KnowledgeNode> nodes)
    {
        var macro = nodes.FirstOrDefault(node =>
            node.Category == KnowledgeCategory.MacroDefinition &&
            node.Title.StartsWith("Script:", StringComparison.Ordinal));
        if (macro is null)
        {
            return [];
        }
        return nodes
            .Where(node =>
                node.Category is KnowledgeCategory.MacroConstructionPattern or
                    KnowledgeCategory.ReactiveTriggerPattern or
                    KnowledgeCategory.CommunicationPattern or
                    KnowledgeCategory.CooperativeProcedure or
                    KnowledgeCategory.CorpProtocol or
                    KnowledgeCategory.EmergencyResponse or
                    KnowledgeCategory.ResourceTransfer or
                    KnowledgeCategory.RoleDefinition or
                    KnowledgeCategory.TacticalPattern ||
                (node.Category == KnowledgeCategory.MacroDefinition &&
                 node.Title.StartsWith("Subroutine:", StringComparison.Ordinal)))
            .Select(node =>
            {
                var isPattern = node.Category != KnowledgeCategory.MacroDefinition;
                return new KnowledgeRelation(
                    node.DocumentId,
                    macro.Id,
                    node.Id,
                    isPattern || node.Category == KnowledgeCategory.ReactiveTriggerPattern
                        ? KnowledgeRelationType.DemonstratesPattern
                        : KnowledgeRelationType.CallsSubroutine,
                    node.Confidence,
                    node.Basis,
                    node.Evidence);
            })
            .ToArray();
    }

    private static string TriggerTitle(string kind)
    {
        var normalized = kind.ToLowerInvariant() switch
        {
            "textline" => "Text line",
            "textout" => "Text output",
            "windowline" => "Window line",
            "var" => "Variable",
            "array" => "Array",
            _ => Humanize(kind)
        };
        return normalized.Length == 0
            ? "Unknown"
            : char.ToUpperInvariant(normalized[0]) + normalized[1..];
    }

    private static (int LineNumber, string Text) FirstNonEmptyLine(IReadOnlyList<string> lines)
    {
        for (var index = 0; index < lines.Count; index++)
        {
            if (!string.IsNullOrWhiteSpace(lines[index]))
            {
                return (index + 1, lines[index]);
            }
        }
        return (1, string.Empty);
    }

    private static bool TryGetCommentText(string line, string extension, out string commentText)
    {
        var trimmed = line.Trim();
        if (extension.Equals(".md", StringComparison.OrdinalIgnoreCase) ||
            extension.Equals(".txt", StringComparison.OrdinalIgnoreCase) ||
            IsExtractedDocumentText(extension))
        {
            commentText = trimmed.TrimStart('#', '>', '-', '*', ' ');
            return commentText.Length > 0;
        }

        foreach (var marker in new[] { "//", "#", ";" })
        {
            if (trimmed.StartsWith(marker, StringComparison.Ordinal))
            {
                commentText = trimmed[marker.Length..].Trim();
                return commentText.Length > 0;
            }
        }
        if (trimmed.StartsWith('*') && !trimmed.StartsWith("*/", StringComparison.Ordinal))
        {
            commentText = trimmed[1..].Trim();
            return commentText.Length > 0;
        }

        commentText = string.Empty;
        return false;
    }

    private static bool IsCodeFile(string extension) =>
        extension.ToLowerInvariant() is ".cts" or ".ts" or ".inc" or ".ets" or ".cs" or ".pas" or ".js";

    private static string LanguageFor(string extension) =>
        extension.ToLowerInvariant() switch
        {
            ".cts" => "TWXScript",
            ".ts" => "TWXInclude",
            ".inc" => "TWXInclude",
            ".ets" => "TWXScript",
            ".cs" => "CSharp",
            ".pas" => "Pascal",
            ".js" => "JavaScript",
            ".md" => "Markdown",
            ".txt" => "Text",
            ".html" or ".htm" => "HTML",
            ".doc" => "WordDocumentText",
            ".tid" => "TiddlyWiki",
            ".slt" => "TelixScript",
            ".key" => "TelixKeyMap",
            _ => "Unknown"
        };

    private static string PrepareSourceContent(string extension, byte[] bytes)
    {
        var content = DecodeSource(bytes);
        if (extension.Equals(".html", StringComparison.OrdinalIgnoreCase) ||
            extension.Equals(".htm", StringComparison.OrdinalIgnoreCase))
        {
            content = HtmlNonText.Replace(content, match => PreserveLineBreaks(match.Value));
            content = HtmlTag.Replace(content, match => $" {PreserveLineBreaks(match.Value)}");
            return WebUtility.HtmlDecode(content);
        }

        if (extension.Equals(".doc", StringComparison.OrdinalIgnoreCase))
        {
            ReadOnlySpan<byte> compoundDocumentHeader = [0xD0, 0xCF, 0x11, 0xE0, 0xA1, 0xB1, 0x1A, 0xE1];
            if (bytes.AsSpan().StartsWith(compoundDocumentHeader) ||
                content.Any(character => character == '\0' ||
                    char.IsControl(character) && character is not ('\r' or '\n' or '\t' or '\f')))
            {
                throw new InvalidDataException(
                    "Binary Word .doc files are not supported; convert the document to .docx or text before mining.");
            }
        }

        return content;
    }

    private static string PreserveLineBreaks(string value) =>
        string.Concat(value.Where(character => character is '\r' or '\n'));

    private static bool IsExtractedDocumentText(string extension) =>
        extension.Equals(".html", StringComparison.OrdinalIgnoreCase) ||
        extension.Equals(".htm", StringComparison.OrdinalIgnoreCase) ||
        extension.Equals(".doc", StringComparison.OrdinalIgnoreCase) ||
        extension.Equals(".tid", StringComparison.OrdinalIgnoreCase);

    private static string DecodeSource(byte[] bytes)
    {
        try
        {
            return new UTF8Encoding(false, true).GetString(bytes);
        }
        catch (DecoderFallbackException)
        {
            return Encoding.Latin1.GetString(bytes);
        }
    }

    private static string Humanize(string value)
    {
        var words = Regex.Replace(value, @"([a-z0-9])([A-Z])", "$1 $2")
            .Replace('_', ' ')
            .Replace('-', ' ');
        return string.Join(' ', words.Split(' ', StringSplitOptions.RemoveEmptyEntries));
    }

    private static string HumanizeDecisionSignal(string signal) =>
        char.ToUpperInvariant(signal[0]) + signal[1..];

    private static bool TryGetContextSignal(string text, out string signal)
    {
        signal = string.Empty;
        var separator = text.IndexOf(':');
        if (separator <= 0)
        {
            return false;
        }
        var label = Regex.Replace(
            text[..separator].Trim().TrimStart('-', '*').Trim().ToLowerInvariant(),
            @"\s+",
            " ");
        signal = label switch
        {
            "purpose" or "why" or "intent" => "purpose",
            "reason" or "reasons" => "intent",
            "goal" or "goals" or "observed goal" or "observed goals" or
                "objective" or "objectives" => "observed goals",
            "typical usage" or "usage" or "when to use" or "use when" => "typical usage",
            "trigger" or "triggers" or "observed trigger" or "observed triggers" => "observed triggers",
            "stop condition" or "stopping condition" or "stop conditions" or
                "stopping conditions" or "stop signal" or "stop signals" or "exit condition" =>
                "stopping conditions",
            "failure" or "failure condition" or "failure conditions" or "risk" or "risks" or
                "drawback" or "drawbacks" => "failure conditions and risks",
            "avoid" or "avoid when" or "avoidance condition" or "avoidance conditions" or
                "do not use when" => "avoidance conditions",
            "assumption" or "assumptions" => "assumptions",
            "benefit" or "benefits" or "advantage" or "advantages" => "benefits",
            "expected outcome" or "expected outcomes" or "expected result" or "expected results" =>
                "expected outcomes",
            "counterexample" or "counterexamples" or "exception" or "exceptions" => "counterexamples",
            "precondition" or "preconditions" or "prerequisite" or "prerequisites" or "requirements" =>
                "preconditions",
            "validation" or "validation check" or "validation checks" or
                "verification" or "verification check" or "verification checks" => "validation checks",
            "verification loop" or "verification loops" => "verification loops",
            "retry" or "retry pattern" or "retry patterns" or "retry condition" or "retry conditions" =>
                "retry patterns",
            "resume" or "resume pattern" or "resume patterns" => "resume patterns",
            "abort" or "abort condition" or "abort conditions" => "abort conditions",
            "escalation" or "escalation condition" or "escalation conditions" or
                "escalation signal" or "escalation signals" => "escalation conditions",
            "checkpoint" or "checkpoints" or "recovery checkpoint" or "recovery checkpoints" =>
                "recovery checkpoints",
            "recovery" or "recovery pattern" or "recovery patterns" => "recovery patterns",
            "input" or "inputs" => "inputs",
            "output" or "outputs" => "outputs",
            "alternative" or "alternatives" => "alternatives",
            _ => string.Empty
        };
        return signal.Length > 0 && text[(separator + 1)..].Trim().Length > 0;
    }

    private static string DocumentationSignalFor(string signal) => signal switch
    {
        "purpose" or "intent" => "intent",
        "observed goals" => "objective",
        "typical usage" => "usage conditions",
        "observed triggers" => "trigger conditions",
        "stopping conditions" => "stopping conditions",
        "failure conditions and risks" => "risks",
        "avoidance conditions" => "avoidance conditions",
        "assumptions" => "assumptions",
        "benefits" => "benefits",
        "expected outcomes" => "expected outcomes",
        "counterexamples" => "counterexamples",
        "preconditions" => "prerequisites",
        "validation checks" => "validation checks",
        "verification loops" => "verification loops",
        "retry patterns" => "retry patterns",
        "resume patterns" => "resume patterns",
        "abort conditions" => "abort conditions",
        "escalation conditions" => "escalation conditions",
        "recovery checkpoints" => "recovery checkpoints",
        "recovery patterns" => "recovery patterns",
        "inputs" => "inputs",
        "outputs" => "expected outcomes",
        "alternatives" => "alternatives",
        _ => signal
    };

    private static void AddDocumentationDecisionEvidence(
        IDictionary<string, List<(int Line, string Signal, string Text)>> evidenceBySignal,
        string signal,
        int lineNumber,
        string text)
    {
        if (!evidenceBySignal.TryGetValue(signal, out var evidence))
        {
            evidence = [];
            evidenceBySignal.Add(signal, evidence);
        }
        if (evidence.Count < 12 && !evidence.Any(item => item.Line == lineNumber))
        {
            evidence.Add((lineNumber, signal, RedactAndLimit(text)));
        }
    }

    private static bool IsDocumentationFile(string extension) =>
        extension.Equals(".md", StringComparison.OrdinalIgnoreCase) ||
        extension.Equals(".txt", StringComparison.OrdinalIgnoreCase) ||
        IsExtractedDocumentText(extension);

    private static Regex DocumentationPattern(string expression) =>
        new(expression, RegexOptions.IgnoreCase | RegexOptions.Compiled | RegexOptions.CultureInvariant);

    private static string RedactAndLimit(string evidence)
    {
        var redacted = SecretLikeValue.Replace(evidence, "$1$2[REDACTED]");
        return redacted.Length <= 240 ? redacted : redacted[..240];
    }

    private static string StableId(string value) =>
        Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(value))).ToLowerInvariant();

    private sealed record TriggerRegistration(
        string Kind,
        string Name,
        string HandlerLabel,
        string Arguments,
        int LineNumber,
        string SourceLine);

    private sealed class Candidate(
        KnowledgeCategory category,
        string title,
        string summary,
        decimal confidence,
        ExtractionBasis basis)
    {
        public KnowledgeCategory Category { get; } = category;
        public string Title { get; } = title;
        public string Summary { get; } = summary;
        public decimal Confidence { get; } = confidence;
        public ExtractionBasis Basis { get; } = basis;
        public List<KnowledgeEvidence> Evidence { get; } = [];
    }
}
