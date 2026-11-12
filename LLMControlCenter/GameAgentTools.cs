using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using LLMSurvivingProxyAgent.Core;

namespace MTC;

internal sealed class GameAgentToolDescriptor
{
    public string Name { get; init; } = string.Empty;
    public string Description { get; init; } = string.Empty;
    public bool CanExecuteGameCommand { get; init; }
    public bool RequiresApproval { get; init; }
}

internal sealed class GameAgentToolCallResult
{
    public string ToolName { get; init; } = string.Empty;
    public bool Success { get; init; }
    public bool WouldRequireApproval { get; init; }
    public string Message { get; init; } = string.Empty;
    public Dictionary<string, string> Data { get; init; } = [];
}

internal sealed record GameAgentScriptCatalogEntry(string File, string Category);

internal sealed record GameAgentScriptCatalogSnapshot(
    bool Available,
    string Status,
    int TotalEntries,
    IReadOnlyList<GameAgentScriptCatalogEntry> Entries);

internal enum GameAgentToolActionKind
{
    RunScript,
    StopScript,
    RunMombotCommand,
    RespondToPrompt,
}

internal sealed record GameAgentToolAction(
    GameAgentToolActionKind Kind,
    string ScriptReference = "",
    string ScriptName = "",
    int? ScriptId = null,
    string Command = "",
    string ExpectedPrompt = "");

internal static class GameAgentToolRegistry
{
    private static readonly IReadOnlyDictionary<string, string> ApprovedScripts =
        new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["trading-cashing/2-worldtrade.ts"] = "trading-cashing/2-worldtrade.ts",
            ["ship-upgrades/ls-dock-shopper-v33.ts"] = "ship-upgrades/ls-dock-shopper-v33.ts",
        };

    private static readonly HashSet<string> AllowedMombotCommands = new(StringComparer.OrdinalIgnoreCase)
    {
        "find", "getvar", "help", "mow", "param", "port", "pscan", "refurb", "refresh",
        "sector", "status", "stop", "twarp",
    };

    private static readonly object ScriptCatalogCacheLock = new();
    private static readonly Dictionary<string, CachedScriptCatalog> ScriptCatalogCache =
        new(StringComparer.OrdinalIgnoreCase);

    private sealed record CachedScriptCatalog(
        DateTime LastWriteTimeUtc,
        long Length,
        GameAgentScriptCatalogSnapshot Snapshot);

    public static IReadOnlyList<GameAgentToolDescriptor> DescribeTools()
        =>
        [
            new()
            {
                Name = "observe_context",
                Description = "Read the compact live game context and recent observed events.",
                CanExecuteGameCommand = false,
                RequiresApproval = false,
            },
            new()
            {
                Name = "recommend_action",
                Description = "Return the deterministic copilot's next structured action recommendation without sending anything.",
                CanExecuteGameCommand = false,
                RequiresApproval = false,
            },
            new()
            {
                Name = "query_sector",
                Description = "Read known database details for the current or adjacent sector.",
                CanExecuteGameCommand = false,
                RequiresApproval = false,
            },
            new()
            {
                Name = "query_route",
                Description = "Plan a route from known database warps. Not implemented in the observer baseline.",
                CanExecuteGameCommand = false,
                RequiresApproval = false,
            },
            new()
            {
                Name = "list_scripts",
                Description = "List scripts currently running in the interpreter; this does not start or stop scripts.",
                CanExecuteGameCommand = false,
                RequiresApproval = false,
            },
            new()
            {
                Name = "search_script_catalog",
                Description = "Search local TWX catalog filenames and categories. Results are unreviewed metadata; this never executes scripts.",
                CanExecuteGameCommand = false,
                RequiresApproval = false,
            },
            new()
            {
                Name = "propose_command",
                Description = "Draft a command for user review without sending anything.",
                CanExecuteGameCommand = false,
                RequiresApproval = true,
            },
            new()
            {
                Name = "send_command",
                Description = "Send raw terminal input to the game stream.",
                CanExecuteGameCommand = true,
                RequiresApproval = true,
            },
            new()
            {
                Name = "run_script",
                Description = "Arguments: {\"script\":\"trading-cashing/2-worldtrade.ts\"} or {\"script\":\"ship-upgrades/ls-dock-shopper-v33.ts\"}. Start only one reviewed script when the user explicitly requests an action and the live prompt is compatible.",
                CanExecuteGameCommand = true,
                RequiresApproval = false,
            },
            new()
            {
                Name = "stop_script",
                Description = "Arguments: {\"id\":<running interpreter id>}. Stop only a running, non-system instance of a reviewed script. Never stop all scripts.",
                CanExecuteGameCommand = true,
                RequiresApproval = false,
            },
            new()
            {
                Name = "respond_to_prompt",
                Description = "Arguments: {\"script_name\":\"<approved script filename>\",\"expected_prompt\":\"<exact prompt>\",\"response\":\"<choice or quantity>\"}. Answer only the current observed prompt of an approved interactive script; visible choices and numeric limits are validated.",
                CanExecuteGameCommand = true,
                RequiresApproval = false,
            },
            new()
            {
                Name = "run_mombot_command",
                Description = "Arguments: {\"command\":\"status\"}. Run one local/self Mombot command from the approved non-combat command set. Remote private/subspace messages are not action sources.",
                CanExecuteGameCommand = true,
                RequiresApproval = false,
            },
        ];

    public static bool TryNormalizeApprovedScript(string? reference, out string normalized)
    {
        normalized = string.Empty;
        if (string.IsNullOrWhiteSpace(reference))
            return false;

        string candidate = reference.Trim().Replace('\\', '/').TrimStart('/');
        if (!ApprovedScripts.TryGetValue(candidate, out string? approved))
            return false;

        normalized = approved;
        return true;
    }

    public static bool IsApprovedScriptReference(string? reference)
        => TryNormalizeApprovedScript(reference, out _);

    public static bool IsApprovedRunningScriptReference(string? reference)
    {
        if (string.IsNullOrWhiteSpace(reference))
            return false;

        string candidate = reference.Trim().Replace('\\', '/').TrimEnd('/');
        if (candidate.Contains("/../", StringComparison.Ordinal) ||
            candidate.StartsWith("../", StringComparison.Ordinal))
        {
            return false;
        }

        return ApprovedScripts.Values.Any(approved =>
            string.Equals(candidate, approved, StringComparison.OrdinalIgnoreCase) ||
            candidate.EndsWith('/' + approved, StringComparison.OrdinalIgnoreCase));
    }

    public static bool IsSameApprovedScript(string? runningReference, string approvedReference)
    {
        if (!TryNormalizeApprovedScript(approvedReference, out string approved) ||
            string.IsNullOrWhiteSpace(runningReference))
        {
            return false;
        }

        string candidate = runningReference.Trim().Replace('\\', '/').TrimEnd('/');
        return string.Equals(candidate, approved, StringComparison.OrdinalIgnoreCase) ||
               candidate.EndsWith('/' + approved, StringComparison.OrdinalIgnoreCase);
    }

    public static bool IsAllowedMombotCommand(string? command, out string normalized)
    {
        normalized = string.Empty;
        if (string.IsNullOrWhiteSpace(command) || command.Length > 80 ||
            command.Contains('|') || command.Contains('\r') || command.Contains('\n'))
        {
            return false;
        }

        foreach (char character in command)
        {
            if (!char.IsAsciiLetterOrDigit(character) &&
                character is not (' ' or '.' or '_' or '-' or '$' or '~'))
            {
                return false;
            }
        }

        string candidate = string.Join(
            ' ',
            command.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries));
        int commandEnd = candidate.IndexOf(' ');
        string name = commandEnd < 0 ? candidate : candidate[..commandEnd];
        if (!AllowedMombotCommands.Contains(name))
            return false;

        normalized = candidate;
        return true;
    }

    public static bool IsSafePromptResponse(string? response)
    {
        if (string.IsNullOrWhiteSpace(response) || response.Length > 32)
            return false;

        return response.All(character => character is >= ' ' and <= '~');
    }

    public static bool TryParseModelAction(
        string content,
        out GameAgentToolAction? action,
        out string error)
    {
        action = null;
        error = string.Empty;
        if (string.IsNullOrWhiteSpace(content) || content.Length > 4096)
            return false;

        string json = content.Trim();
        if (json.StartsWith("```", StringComparison.Ordinal))
        {
            int firstLineEnd = json.IndexOf('\n');
            int closingFence = json.LastIndexOf("```", StringComparison.Ordinal);
            if (firstLineEnd < 0 || closingFence <= firstLineEnd)
                return false;
            json = json[(firstLineEnd + 1)..closingFence].Trim();
        }

        try
        {
            using JsonDocument document = JsonDocument.Parse(json);
            JsonElement root = document.RootElement;
            if (root.ValueKind != JsonValueKind.Object ||
                root.EnumerateObject().Count() != 1 ||
                !root.TryGetProperty("tool_call", out JsonElement toolCall) ||
                toolCall.ValueKind != JsonValueKind.Object ||
                toolCall.EnumerateObject().Count() != 2 ||
                !toolCall.TryGetProperty("name", out JsonElement nameElement) ||
                nameElement.ValueKind != JsonValueKind.String ||
                !toolCall.TryGetProperty("arguments", out JsonElement arguments) ||
                arguments.ValueKind != JsonValueKind.Object)
            {
                return false;
            }

            string name = nameElement.GetString() ?? string.Empty;
            switch (name)
            {
                case "run_script":
                    if (!HasExactArguments(arguments, "script") ||
                        !TryReadString(arguments, "script", out string script) ||
                        !TryNormalizeApprovedScript(script, out string approvedScript))
                    {
                        error = "Only the reviewed World Trade and StarDock Shopper script references can be started.";
                        return false;
                    }

                    action = new GameAgentToolAction(GameAgentToolActionKind.RunScript, ScriptReference: approvedScript);
                    return true;

                case "stop_script":
                    if (!HasExactArguments(arguments, "id") ||
                        !arguments.TryGetProperty("id", out JsonElement idElement) ||
                        idElement.ValueKind != JsonValueKind.Number ||
                        !idElement.TryGetInt32(out int id) ||
                        id <= 0)
                    {
                        error = "stop_script requires one positive running-script id.";
                        return false;
                    }

                    action = new GameAgentToolAction(GameAgentToolActionKind.StopScript, ScriptId: id);
                    return true;

                case "run_mombot_command":
                    if (!HasExactArguments(arguments, "command") ||
                        !TryReadString(arguments, "command", out string command) ||
                        !IsAllowedMombotCommand(command, out string normalizedCommand))
                    {
                        error = "That Mombot command is not in the local, non-combat allowlist.";
                        return false;
                    }

                    action = new GameAgentToolAction(GameAgentToolActionKind.RunMombotCommand, Command: normalizedCommand);
                    return true;

                case "respond_to_prompt":
                    if (!HasExactArguments(arguments, "script_name", "expected_prompt", "response") ||
                        !TryReadString(arguments, "script_name", out string scriptName) ||
                        !string.Equals(scriptName, Path.GetFileName(scriptName), StringComparison.Ordinal) ||
                        !ScriptInputPromptPolicy.IsApprovedScript(scriptName) ||
                        !TryReadString(arguments, "expected_prompt", out string expectedPrompt) ||
                        expectedPrompt.Length > 4_000 ||
                        !TryReadString(arguments, "response", out string response) ||
                        !IsSafePromptResponse(response))
                    {
                        error = "respond_to_prompt requires an approved script name, exact observed prompt text, and a short printable response.";
                        return false;
                    }

                    action = new GameAgentToolAction(
                        GameAgentToolActionKind.RespondToPrompt,
                        ScriptName: scriptName.Trim(),
                        Command: response.Trim(),
                        ExpectedPrompt: expectedPrompt.Trim());
                    return true;

                default:
                    error = $"Unknown Game Agent tool '{name}'.";
                    return false;
            }
        }
        catch (JsonException)
        {
            return false;
        }
    }

    private static bool HasExactArguments(JsonElement arguments, params string[] names)
    {
        if (arguments.EnumerateObject().Count() != names.Length)
            return false;

        return names.All(name => arguments.TryGetProperty(name, out _));
    }

    private static bool TryReadString(JsonElement arguments, string name, out string value)
    {
        value = string.Empty;
        if (!arguments.TryGetProperty(name, out JsonElement element) ||
            element.ValueKind != JsonValueKind.String)
        {
            return false;
        }

        value = element.GetString() ?? string.Empty;
        return !string.IsNullOrWhiteSpace(value);
    }

    public static GameAgentToolCallResult ObserveContext(GameAgentContextSnapshot context)
        => new()
        {
            ToolName = "observe_context",
            Success = true,
            Message = "Context observed without changing gameplay.",
            Data = new Dictionary<string, string>
            {
                ["game"] = context.GameName,
                ["connected"] = context.Connected ? "true" : "false",
                ["sector"] = context.CurrentSector > 0 ? context.CurrentSector.ToString() : string.Empty,
                ["prompt"] = context.CurrentPrompt,
                ["recentEvents"] = context.RecentEvents.Count.ToString(),
                ["onlinePlayers"] = string.Join("; ", context.OnlinePlayers),
                ["runningScripts"] = string.Join("; ", context.RunningScripts.Select(script => script.Name)),
                ["botMode"] = context.Bot.Mode,
            },
        };

    public static GameAgentToolCallResult RecommendAction(GameAgentContextSnapshot context)
        => GameAgentCopilot.RecommendAction(context);

    public static GameAgentToolCallResult QuerySector(GameAgentContextSnapshot context, int sectorNumber)
    {
        GameAgentSectorSnapshot? sector = null;
        if (context.CurrentSectorDetails?.Number == sectorNumber)
            sector = context.CurrentSectorDetails;
        sector ??= context.AdjacentSectors.FirstOrDefault(candidate => candidate.Number == sectorNumber);

        if (sector == null)
        {
            return new GameAgentToolCallResult
            {
                ToolName = "query_sector",
                Success = false,
                Message = $"Sector {sectorNumber} is not in the compact live snapshot.",
            };
        }

        return new GameAgentToolCallResult
        {
            ToolName = "query_sector",
            Success = true,
            Message = $"Sector {sector.Number} details returned from local database snapshot.",
            Data = new Dictionary<string, string>
            {
                ["sector"] = sector.Number.ToString(),
                ["explored"] = sector.Explored,
                ["warpsOut"] = string.Join(" ", sector.WarpsOut),
                ["warpsIn"] = string.Join(" ", sector.WarpsIn),
                ["port"] = sector.Port,
                ["planets"] = string.Join("; ", sector.Planets),
                ["fighters"] = sector.Fighters,
                ["armids"] = sector.ArmidMines,
                ["limpets"] = sector.LimpetMines,
            },
        };
    }

    public static GameAgentToolCallResult ListScripts()
        => ListScripts([]);

    public static GameAgentToolCallResult ListScripts(IReadOnlyList<GameAgentRunningScriptSnapshot> runningScripts)
    {
        if (runningScripts.Count == 0)
        {
            return new GameAgentToolCallResult
            {
                ToolName = "list_scripts",
                Success = true,
                Message = "No running scripts are visible in the current interpreter snapshot.",
            };
        }

        return new GameAgentToolCallResult
        {
            ToolName = "list_scripts",
            Success = true,
            Message = $"{runningScripts.Count} running script(s) returned from the interpreter snapshot.",
            Data = runningScripts.Take(24).ToDictionary(
                script => script.Id.ToString(),
                script => $"{script.Name}{(script.Paused ? " paused" : string.Empty)}{(script.IsBot ? " bot" : string.Empty)}"),
        };
    }

    public static GameAgentToolCallResult SearchScriptCatalog(string scriptsDirectory, string query)
    {
        string lowerQuery = query.ToLowerInvariant();
        if (!new[]
            {
                "script", "twx", "macro", "scan", "avoid", "sector", "intel", "status", "navigation",
                "trade", "world", "upgrade", "refurb", "holds", "fighter", "shield", "stardock",
                "class 0", "class0", "menu", "shopper"
            }
                .Any(term => lowerQuery.Contains(term, StringComparison.Ordinal)))
        {
            return new GameAgentToolCallResult
            {
                ToolName = "search_script_catalog",
                Success = true,
                Message = "No script-catalog search was needed for this prompt.",
            };
        }

        GameAgentScriptCatalogSnapshot catalog = ReadScriptCatalog(
            scriptsDirectory,
            maximumEntries: int.MaxValue);
        if (!catalog.Available)
        {
            return new GameAgentToolCallResult
            {
                ToolName = "search_script_catalog",
                Success = false,
                Message = catalog.Status,
            };
        }

        string[] terms = query
            .Split([' ', '\t', '\r', '\n', '.', '/', '\\', '-', '_', ':', ','], StringSplitOptions.RemoveEmptyEntries)
            .Where(term => term.Length >= 3)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();
        var matches = catalog.Entries
            .Select(entry => new
            {
                entry.File,
                entry.Category,
                Score = terms.Sum(term =>
                    (entry.File.Contains(term, StringComparison.OrdinalIgnoreCase) ? 2 : 0) +
                    (entry.Category.Contains(term, StringComparison.OrdinalIgnoreCase) ? 1 : 0)),
            })
            .Where(entry => entry.Score > 0)
            .OrderByDescending(entry => entry.Score)
            .ThenBy(entry => entry.File, StringComparer.OrdinalIgnoreCase)
            .Take(8)
            .ToArray();
        Dictionary<string, string> categoryCounts = matches.Length > 0
            ? []
            : catalog.Entries
                .Select(entry => entry.Category)
                .Where(category => !string.IsNullOrWhiteSpace(category))
                .GroupBy(category => category, StringComparer.OrdinalIgnoreCase)
                .OrderBy(group => group.Key, StringComparer.OrdinalIgnoreCase)
                .ToDictionary(
                    group => $"category_{group.Key}",
                    group => group.Count().ToString(),
                    StringComparer.Ordinal);

        return new GameAgentToolCallResult
        {
            ToolName = "search_script_catalog",
            Success = true,
            Message = matches.Length == 0
                ? $"No TWX catalog filenames matched. The catalog has {catalog.TotalEntries} readable entries; category counts are provided for discovery. Entries are unreviewed metadata; no script was executed."
                : $"Matched {matches.Length} local TWX catalog filename(s). Filenames and categories do not verify behavior or safety; no script was executed.",
            Data = matches.Length == 0
                ? categoryCounts
                : matches
                    .Select((match, index) => new KeyValuePair<string, string>(
                        $"candidate_{index + 1}",
                        $"{match.File} (category: {match.Category})"))
                    .ToDictionary(pair => pair.Key, pair => pair.Value, StringComparer.Ordinal),
        };
    }

    public static GameAgentScriptCatalogSnapshot ReadScriptCatalog(
        string scriptsDirectory,
        int maximumEntries = 64)
    {
        if (maximumEntries < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(maximumEntries));
        }

        if (string.IsNullOrWhiteSpace(scriptsDirectory))
        {
            return UnavailableScriptCatalog(
                "The local TWX script catalog is unavailable because no scripts directory is configured.");
        }

        try
        {
            string catalogPath = Path.GetFullPath(Path.Combine(scriptsDirectory, "catalog.json"));
            lock (ScriptCatalogCacheLock)
            {
                var catalogFile = new FileInfo(catalogPath);
                catalogFile.Refresh();
                if (!catalogFile.Exists)
                {
                    ScriptCatalogCache.Remove(catalogPath);
                    return UnavailableScriptCatalog(
                        "The local TWX script catalog was not found in the configured scripts directory.");
                }

                if (ScriptCatalogCache.TryGetValue(catalogPath, out CachedScriptCatalog? cached) &&
                    cached.LastWriteTimeUtc == catalogFile.LastWriteTimeUtc &&
                    cached.Length == catalogFile.Length)
                {
                    return LimitScriptCatalog(cached.Snapshot, maximumEntries);
                }

                DateTime sourceLastWriteTimeUtc = catalogFile.LastWriteTimeUtc;
                long sourceLength = catalogFile.Length;
                GameAgentScriptCatalogSnapshot parsedCatalog = ParseScriptCatalog(catalogPath);
                if (!parsedCatalog.Available)
                {
                    ScriptCatalogCache.Remove(catalogPath);
                    return parsedCatalog;
                }

                catalogFile.Refresh();
                if (catalogFile.Exists &&
                    catalogFile.LastWriteTimeUtc == sourceLastWriteTimeUtc &&
                    catalogFile.Length == sourceLength)
                {
                    ScriptCatalogCache[catalogPath] = new CachedScriptCatalog(
                        sourceLastWriteTimeUtc,
                        sourceLength,
                        parsedCatalog);
                }
                else
                {
                    ScriptCatalogCache.Remove(catalogPath);
                }

                return LimitScriptCatalog(parsedCatalog, maximumEntries);
            }
        }
        catch (JsonException exception)
        {
            return UnavailableScriptCatalog(
                $"The local TWX script catalog contains invalid JSON: {exception.Message}");
        }
        catch (IOException exception)
        {
            return UnavailableScriptCatalog(
                $"The local TWX script catalog could not be read: {exception.Message}");
        }
        catch (UnauthorizedAccessException exception)
        {
            return UnavailableScriptCatalog(
                $"The local TWX script catalog could not be accessed: {exception.Message}");
        }
    }

    private static GameAgentScriptCatalogSnapshot ParseScriptCatalog(string catalogPath)
    {
        using JsonDocument catalog = JsonDocument.Parse(File.ReadAllText(catalogPath));
        if (!catalog.RootElement.TryGetProperty("entries", out JsonElement entries) ||
            entries.ValueKind != JsonValueKind.Array)
        {
            return UnavailableScriptCatalog(
                "The local TWX script catalog has no readable entries list.");
        }

        GameAgentScriptCatalogEntry[] validEntries = entries
            .EnumerateArray()
            .Where(entry => entry.ValueKind == JsonValueKind.Object)
            .Select(entry => new GameAgentScriptCatalogEntry(
                entry.TryGetProperty("file", out JsonElement fileElement) &&
                fileElement.ValueKind == JsonValueKind.String
                    ? fileElement.GetString() ?? string.Empty
                    : string.Empty,
                entry.TryGetProperty("category", out JsonElement categoryElement) &&
                categoryElement.ValueKind == JsonValueKind.String
                    ? categoryElement.GetString() ?? string.Empty
                    : string.Empty))
            .Where(entry =>
                !string.IsNullOrWhiteSpace(entry.File) &&
                !Path.IsPathRooted(entry.File) &&
                !entry.File.Split(['/', '\\'], StringSplitOptions.RemoveEmptyEntries)
                    .Contains("..", StringComparer.Ordinal) &&
                (entry.File.EndsWith(".ts", StringComparison.OrdinalIgnoreCase) ||
                 entry.File.EndsWith(".cts", StringComparison.OrdinalIgnoreCase)))
            .OrderBy(entry => entry.Category, StringComparer.OrdinalIgnoreCase)
            .ThenBy(entry => entry.File, StringComparer.OrdinalIgnoreCase)
            .ToArray();

        return new GameAgentScriptCatalogSnapshot(
            true,
            $"Loaded {validEntries.Length} safe relative TWX script references. Names and categories are unreviewed metadata; no script was executed.",
            validEntries.Length,
            validEntries);
    }

    private static GameAgentScriptCatalogSnapshot LimitScriptCatalog(
        GameAgentScriptCatalogSnapshot catalog,
        int maximumEntries) =>
        catalog with { Entries = catalog.Entries.Take(maximumEntries).ToArray() };

    private static GameAgentScriptCatalogSnapshot UnavailableScriptCatalog(string status) =>
        new(false, status, 0, []);

    public static GameAgentToolCallResult ListScriptsLegacyPlaceholder()
        => new()
        {
            ToolName = "list_scripts",
            Success = true,
            Message = "Script listing is scaffolded; runtime script inventory is not wired yet.",
        };

    public static GameAgentToolCallResult ProposeCommand(string command)
        => new()
        {
            ToolName = "propose_command",
            Success = true,
            WouldRequireApproval = true,
            Message = $"Proposed command only, not sent: {command}",
        };

    public static GameAgentToolCallResult DisabledAction(string toolName)
        => new()
        {
            ToolName = toolName,
            Success = false,
            WouldRequireApproval = true,
            Message = "Direct model command execution is disabled. Action requests use the Game Agent request runner or JSON-RPC action methods.",
        };
}
