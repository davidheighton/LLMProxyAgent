using LLMSurvivingProxyAgent.CorpusBuilder;

return await CorpusBuilderCommand.RunAsync(args);

internal static class CorpusBuilderCommand
{
    public static async Task<int> RunAsync(string[] args)
    {
        if (args.Length == 0 || args[0] is "--help" or "-h")
        {
            PrintUsage();
            return args.Length == 0 ? 2 : 0;
        }

        var command = args[0].ToLowerInvariant();
        if (command is not ("build" or "inventory" or "url-inventory" or "web-seed"))
        {
            Console.Error.WriteLine($"Unknown command: {args[0]}");
            PrintUsage();
            return 2;
        }

        string? output = null;
        string? inventoryOutput = null;
        string? input = null;
        string? source = null;
        var version = "1.0.0";
        var versionSpecified = false;
        var roots = new List<CorpusRoot>();
        try
        {
            for (var index = 1; index < args.Length; index++)
            {
                switch (args[index])
                {
                    case "--root":
                        roots.Add(ParseRoot(RequireValue(args, ref index)));
                        break;
                    case "--output":
                        output = RequireValue(args, ref index);
                        break;
                    case "--inventory":
                        inventoryOutput = RequireValue(args, ref index);
                        break;
                    case "--input":
                        input = RequireValue(args, ref index);
                        break;
                    case "--version":
                        version = RequireValue(args, ref index);
                        versionSpecified = true;
                        break;
                    case "--source":
                        source = RequireValue(args, ref index);
                        break;
                    default:
                        throw new ArgumentException($"Unknown option: {args[index]}");
                }
            }
            if ((command is "url-inventory" or "web-seed") && string.IsNullOrWhiteSpace(input))
            {
                throw new ArgumentException("--input is required for web-source commands.");
            }
            if (command is not ("url-inventory" or "web-seed") && input is not null)
            {
                throw new ArgumentException("--input is only supported by web-source commands.");
            }
            if (command == "url-inventory" &&
                (roots.Count > 0 || inventoryOutput is not null || source is not null || versionSpecified))
            {
                throw new ArgumentException(
                    "url-inventory accepts only --input and --output.");
            }
            if (command == "web-seed" &&
                (roots.Count > 0 || inventoryOutput is not null || source is not null))
            {
                throw new ArgumentException(
                    "web-seed accepts --input, --output, and optional --version.");
            }
            if (command is not ("url-inventory" or "web-seed") && roots.Count == 0)
            {
                throw new ArgumentException("At least one --root must be provided.");
            }
            if (string.IsNullOrWhiteSpace(output))
            {
                throw new ArgumentException("--output is required.");
            }
            if (command == "build" && string.IsNullOrWhiteSpace(source))
            {
                throw new ArgumentException("--source is required for build.");
            }
        }
        catch (ArgumentException exception)
        {
            Console.Error.WriteLine(exception.Message);
            PrintUsage();
            return 2;
        }

        if (command == "url-inventory")
        {
            try
            {
                var inventory = await new WebSourceInventoryService()
                    .WriteInventoryAsync(input!, output!);
                Console.WriteLine(
                    $"Inventoried {inventory.Sources.Count} URL candidates without fetching pages: {Path.GetFullPath(output!)}");
                return 0;
            }
            catch (Exception exception) when (
                exception is ArgumentException or IOException or InvalidDataException)
            {
                Console.Error.WriteLine($"URL inventory failed: {exception.Message}");
                return 2;
            }
        }

        if (command == "web-seed")
        {
            try
            {
                var webSeedResult = await new WebSourceKnowledgePackageService()
                    .BuildAsync(input!, output!, version);
                Console.WriteLine(
                    $"Built reviewed web-source seed v{version}: {webSeedResult.DocumentsExported} source documents, {webSeedResult.NodesExported} knowledge observations.");
                Console.WriteLine($"Seed package: {webSeedResult.PackagePath}");
                return 0;
            }
            catch (Exception exception) when (
                exception is ArgumentException or IOException or InvalidDataException)
            {
                Console.Error.WriteLine($"Web-source seed build failed: {exception.Message}");
                return 2;
            }
        }

        var service = new CorpusBuildService();
        if (command == "inventory")
        {
            var path = inventoryOutput ?? output;
            var inventory = await service.WriteInventoryAsync(roots, version, path);
            Console.WriteLine(
                $"Inventoried {inventory.Sources.Sum(sourceSummary => sourceSummary.TotalFiles)} files across {inventory.Sources.Count} roots: {Path.GetFullPath(path)}");
            return 0;
        }

        inventoryOutput ??= Path.ChangeExtension(Path.GetFullPath(output), ".inventory.json");
        var result = await service.BuildAsync(new CorpusBuildOptions(
            source!,
            version,
            output!,
            inventoryOutput,
            roots));
        Console.WriteLine(
            $"Built corpus v{version}: {result.DocumentsExported} documents, {result.NodesExported} knowledge nodes.");
        Console.WriteLine($"Seed package: {result.PackagePath}");
        Console.WriteLine($"Inventory: {result.InventoryPath}");
        return 0;
    }

    private static CorpusRoot ParseRoot(string value)
    {
        var equals = value.IndexOf('=');
        var colon = value.IndexOf(':');
        if (colon <= 0 || equals <= colon + 1 || equals == value.Length - 1 ||
            !Enum.TryParse<CorpusSourceKind>(value[(colon + 1)..equals], ignoreCase: true, out var kind) ||
            !Enum.IsDefined(kind))
        {
            throw new ArgumentException(
                $"Invalid root '{value}'. Use id:kind=path, where kind is one of {string.Join(", ", Enum.GetNames<CorpusSourceKind>())}.");
        }

        var id = value[..colon].Trim();
        var path = value[(equals + 1)..].Trim();
        if (id.Length == 0 || path.Length == 0)
        {
            throw new ArgumentException("Corpus root IDs and paths cannot be empty.");
        }
        return new CorpusRoot(id, kind, Path.GetFullPath(path));
    }

    private static string RequireValue(string[] args, ref int index)
    {
        if (index + 1 >= args.Length || args[index + 1].StartsWith("--", StringComparison.Ordinal))
        {
            throw new ArgumentException($"Option '{args[index]}' requires a value.");
        }
        return args[++index];
    }

    private static void PrintUsage()
    {
        Console.Error.WriteLine("""
            Usage:
              corpus-builder inventory --output inventory.json --root <id>:<kind>=<path> [...]
              corpus-builder build --output corpus.seedpkg --version <version> --source <name>
                  [--inventory inventory.json] --root <id>:<kind>=<path> [...]
              corpus-builder url-inventory --input urls.json --output web-sources.inventory.json
              corpus-builder web-seed --input reviewed-web-sources.json --output web-sources.seedpkg

            Root kinds: TwxScripts, PinkScripts, MomBot, Twx30, Zed,
                        NodeBot, Documentation, Data, RuntimeArtifacts, Other, SavedGameLogs.
            RuntimeArtifacts roots are inventoried but never mined into a seed.
            SavedGameLogs roots extract repeated incoming menu key/label layouts only.
            url-inventory validates and classifies supplied HTTP(S) URLs; it does not fetch pages.
            web-seed packages reviewed, paraphrased source observations; it does not fetch pages.
            """);
    }
}
