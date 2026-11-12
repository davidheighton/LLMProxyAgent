using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;

namespace TWXProxy.Core;

public sealed record QuickLoadEntry(string GroupName, string DisplayName, string RelativePath);

public sealed record QuickLoadGroup(string Name, IReadOnlyList<QuickLoadEntry> Entries);

public sealed record ScriptMenuMetadata(string? DisplayName, bool IsNonPreferredVariant);

public static class ProxyMenuCatalog
{
    public const string NativeMombotSectionName = "bot:mombot_native";

    private static readonly HashSet<string> ExcludedQuickDirectories = new(StringComparer.OrdinalIgnoreCase)
    {
        ".",
        "..",
        "include",
        "includes",
        "mombot",
        "mombot3",
        "mombot4p",
        "qubot",
        "zedbot",
    };

    public static IReadOnlyList<QuickLoadGroup> BuildQuickLoadGroups(string? programDir, string? scriptDirectory)
    {
        string scriptsRoot = ResolveScriptsRoot(programDir, scriptDirectory);
        if (!Directory.Exists(scriptsRoot))
            return Array.Empty<QuickLoadGroup>();

        var configSections = TwxpConfigStore.LoadSections(programDir);
        var quickLoadMap = ReadQuickLoadSection(configSections);
        IReadOnlyDictionary<string, ScriptMenuMetadata> scriptMetadata =
            ReadScriptMenuMetadata(scriptsRoot);
        var groups = new SortedDictionary<string, List<QuickLoadEntry>>(StringComparer.OrdinalIgnoreCase);
        var addedPaths = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (string file in Directory.EnumerateFiles(scriptsRoot))
        {
            string fileName = Path.GetFileName(file);
            if (!IsQuickLoadScriptFile(fileName))
                continue;

            string groupName = ResolveVirtualQuickGroup(fileName, quickLoadMap);
            AddQuickEntry(groups, groupName, fileName, fileName, scriptMetadata, addedPaths);
        }

        foreach (string directory in Directory.EnumerateDirectories(scriptsRoot))
        {
            string directoryName = Path.GetFileName(directory);
            if (ExcludedQuickDirectories.Contains(directoryName))
                continue;

            foreach (string file in Directory.EnumerateFiles(directory))
            {
                string fileName = Path.GetFileName(file);
                if (!IsQuickLoadScriptFile(fileName))
                    continue;

                string relativePath = directoryName + "/" + fileName;
                AddQuickEntry(groups, directoryName, fileName, relativePath, scriptMetadata, addedPaths);
            }
        }

        return groups
            .Select(kvp => new QuickLoadGroup(
                kvp.Key,
                kvp.Value
                    .OrderBy(entry => entry.DisplayName, StringComparer.OrdinalIgnoreCase)
                    .ToArray()))
            .ToArray();
    }

    public static IReadOnlyDictionary<string, ScriptMenuMetadata> ReadScriptMenuMetadata(
        string? scriptsRoot)
    {
        if (string.IsNullOrWhiteSpace(scriptsRoot))
            return new Dictionary<string, ScriptMenuMetadata>(StringComparer.OrdinalIgnoreCase);

        return ReadQuickLoadScriptMetadata(Path.GetFullPath(scriptsRoot));
    }

    public static IReadOnlyList<BotConfig> LoadBotConfigs(string? programDir, string? scriptDirectory, bool includeNative = false)
    {
        string scriptsRoot = ResolveScriptsRoot(programDir, scriptDirectory);
        var sections = TwxpConfigStore.LoadSections(programDir);
        if (sections.Count == 0)
            return Array.Empty<BotConfig>();
        return sections
            .Select(section => ParseBotConfigSection(section, scriptsRoot, requireScriptFile: true, includeNative: includeNative))
            .Where(bot => bot != null)
            .Cast<BotConfig>()
            .ToArray();
    }

    public static BotConfig? ParseBotConfigSection(
        TwxpConfigSection section,
        string? programDir,
        string? scriptDirectory,
        bool requireScriptFile,
        bool includeNative = false)
    {
        string scriptsRoot = ResolveScriptsRoot(programDir, scriptDirectory);
        return ParseBotConfigSection(section, scriptsRoot, requireScriptFile, includeNative);
    }

    public static bool IsNativeBotSection(TwxpConfigSection section)
    {
        if (section == null)
            return false;

        if (string.Equals(section.Name, NativeMombotSectionName, StringComparison.OrdinalIgnoreCase))
            return true;

        return section.Values.TryGetValue("Native", out string? nativeValue) && ParseBool(nativeValue, false);
    }

    public static bool IsNativeBotConfig(BotConfig? config)
    {
        if (config == null)
            return false;

        if (config.Properties.TryGetValue("Native", out string? nativeValue) && ParseBool(nativeValue, false))
            return true;

        return string.Equals(config.Alias, GetBotAlias(NativeMombotSectionName), StringComparison.OrdinalIgnoreCase);
    }

    public static string GetBotAlias(string sectionName)
    {
        if (string.IsNullOrWhiteSpace(sectionName))
            return string.Empty;

        return sectionName.StartsWith("bot:", StringComparison.OrdinalIgnoreCase)
            ? sectionName["bot:".Length..].Trim()
            : sectionName.Trim();
    }

    private static BotConfig? ParseBotConfigSection(
        TwxpConfigSection section,
        string scriptsRoot,
        bool requireScriptFile,
        bool includeNative)
    {
        if (section == null || !section.Name.StartsWith("bot:", StringComparison.OrdinalIgnoreCase))
            return null;

        if (!includeNative && IsNativeBotSection(section))
            return null;

        string alias = GetBotAlias(section.Name);
        Dictionary<string, string> values = section.Values;
        if (!values.TryGetValue("Name", out string? botName) || string.IsNullOrWhiteSpace(botName))
            return null;

        if (!values.TryGetValue("Script", out string? scriptList) || string.IsNullOrWhiteSpace(scriptList))
            return null;

        List<string> normalizedScripts = scriptList
            .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Select(script => script.Replace('\\', '/'))
            .Where(script => !string.IsNullOrWhiteSpace(script))
            .ToList();

        string? firstScript = normalizedScripts.FirstOrDefault();
        if (string.IsNullOrWhiteSpace(firstScript))
            return null;

        if (requireScriptFile)
        {
            string candidatePath = Path.Combine(scriptsRoot, firstScript.Replace('/', Path.DirectorySeparatorChar));
            if (!File.Exists(candidatePath))
                return null;
        }

        var properties = new Dictionary<string, string>(values, StringComparer.OrdinalIgnoreCase);
        return new BotConfig
        {
            Alias = alias,
            Name = botName.Trim(),
            ScriptFile = firstScript,
            ScriptFiles = normalizedScripts,
            Description = values.TryGetValue("Description", out string? description) ? description : string.Empty,
            AutoStart = !values.TryGetValue("AutoStart", out string? autoStart) || ParseBool(autoStart, true),
            NameVar = values.TryGetValue("NameVar", out string? nameVar) ? nameVar : string.Empty,
            CommsVar = values.TryGetValue("CommsVar", out string? commsVar) ? commsVar : string.Empty,
            LoginScript = values.TryGetValue("LoginScript", out string? loginScript) ? loginScript : string.Empty,
            Theme = values.TryGetValue("Theme", out string? theme) ? theme : string.Empty,
            Properties = properties,
        };
    }

    private static void AddQuickEntry(
        IDictionary<string, List<QuickLoadEntry>> groups,
        string groupName,
        string displayName,
        string relativePath,
        IReadOnlyDictionary<string, ScriptMenuMetadata> scriptMetadata,
        ISet<string> addedPaths)
    {
        string normalizedPath = relativePath.Replace('\\', '/');
        if (!addedPaths.Add(normalizedPath))
            return;

        if (scriptMetadata.TryGetValue(normalizedPath, out ScriptMenuMetadata? metadata))
        {
            if (metadata.IsNonPreferredVariant)
                return;

            if (!string.IsNullOrWhiteSpace(metadata.DisplayName))
                displayName = metadata.DisplayName;
        }

        if (!groups.TryGetValue(groupName, out List<QuickLoadEntry>? entries))
        {
            entries = new List<QuickLoadEntry>();
            groups[groupName] = entries;
        }

        entries.Add(new QuickLoadEntry(groupName, displayName, normalizedPath));
    }

    private static Dictionary<string, ScriptMenuMetadata> ReadQuickLoadScriptMetadata(string scriptsRoot)
    {
        string catalogPath = Path.Combine(scriptsRoot, "catalog.json");
        var metadata = new Dictionary<string, ScriptMenuMetadata>(StringComparer.OrdinalIgnoreCase);
        if (!File.Exists(catalogPath))
            return metadata;

        using JsonDocument document = JsonDocument.Parse(File.ReadAllText(catalogPath));
        JsonElement root = document.RootElement;
        if (!root.TryGetProperty("library", out JsonElement library) ||
            library.ValueKind != JsonValueKind.String ||
            !string.Equals(library.GetString(), "LLMProxyScripts", StringComparison.Ordinal))
        {
            return metadata;
        }

        if (!root.TryGetProperty("entries", out JsonElement entries) ||
            entries.ValueKind != JsonValueKind.Array)
        {
            throw new InvalidDataException(
                $"Script catalog '{catalogPath}' must contain an entries array.");
        }

        foreach (JsonElement entry in entries.EnumerateArray())
        {
            if (entry.ValueKind != JsonValueKind.Object ||
                !entry.TryGetProperty("file", out JsonElement file) ||
                file.ValueKind != JsonValueKind.String ||
                string.IsNullOrWhiteSpace(file.GetString()))
            {
                throw new InvalidDataException(
                    $"Script catalog '{catalogPath}' contains an entry without a file path.");
            }

            string relativePath = file.GetString()!.Replace('\\', '/');
            if (relativePath.StartsWith("/", StringComparison.Ordinal) ||
                relativePath.Split('/').Any(segment => segment is "." or ".."))
            {
                throw new InvalidDataException(
                    $"Script catalog '{catalogPath}' contains an invalid relative path '{relativePath}'.");
            }

            bool hasVariantGroup =
                entry.TryGetProperty("variant_group", out JsonElement variantGroup) &&
                variantGroup.ValueKind == JsonValueKind.String &&
                !string.IsNullOrWhiteSpace(variantGroup.GetString());
            bool isNonPreferredVariant =
                hasVariantGroup &&
                entry.TryGetProperty("preferred_variant", out JsonElement preferredVariant) &&
                preferredVariant.ValueKind == JsonValueKind.False;
            string? displayName = hasVariantGroup
                ? variantGroup.GetString()
                : null;

            if (!metadata.TryAdd(
                    relativePath,
                    new ScriptMenuMetadata(displayName, isNonPreferredVariant)))
            {
                throw new InvalidDataException(
                    $"Script catalog '{catalogPath}' contains duplicate path '{relativePath}'.");
            }
        }

        return metadata;
    }

    private static string ResolveVirtualQuickGroup(string fileName, IReadOnlyDictionary<string, string> quickLoadMap)
    {
        string virtualGroup = "Misc";
        if (fileName.StartsWith("__", StringComparison.Ordinal))
            virtualGroup = "_Favorite";
        else if (fileName.StartsWith("z-", StringComparison.OrdinalIgnoreCase))
            virtualGroup = "Zed / Archie";

        string virtualName = fileName.StartsWith("_", StringComparison.Ordinal)
            ? fileName[1..]
            : fileName;

        int underscoreIndex = virtualName.IndexOf('_');
        if (underscoreIndex > 0)
        {
            string key = virtualName[..(underscoreIndex + 1)];
            if (quickLoadMap.TryGetValue(key, out string? configuredGroup) && !string.IsNullOrWhiteSpace(configuredGroup))
                virtualGroup = configuredGroup;
        }

        return virtualGroup;
    }

    private static Dictionary<string, string> ReadQuickLoadSection(IReadOnlyList<TwxpConfigSection> sections)
    {
        foreach (TwxpConfigSection section in sections)
        {
            if (section.Name.Equals("QuickLoad", StringComparison.OrdinalIgnoreCase))
                return new Dictionary<string, string>(section.Values, StringComparer.OrdinalIgnoreCase);
        }

        return new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
    }

    private static string ResolveScriptsRoot(string? programDir, string? scriptDirectory)
    {
        if (!string.IsNullOrWhiteSpace(scriptDirectory))
            return Path.GetFullPath(scriptDirectory);

        string root = string.IsNullOrWhiteSpace(programDir)
            ? GetDefaultProgramDir()
            : programDir;
        return Path.GetFullPath(Path.Combine(root, "scripts"));
    }

    private static string GetDefaultProgramDir()
    {
        if (OperatingSystem.IsWindows())
            return WindowsInstallInfo.GetInstalledProgramDirOrDefault();

        return AppContext.BaseDirectory;
    }

    private static bool IsQuickLoadScriptFile(string? fileName)
    {
        if (string.IsNullOrWhiteSpace(fileName))
            return false;

        return fileName.EndsWith(".ts", StringComparison.OrdinalIgnoreCase)
            || fileName.EndsWith(".cts", StringComparison.OrdinalIgnoreCase);
    }

    private static bool ParseBool(string value, bool fallback)
    {
        if (string.IsNullOrWhiteSpace(value))
            return fallback;

        if (bool.TryParse(value, out bool parsed))
            return parsed;

        return value.Equals("1", StringComparison.OrdinalIgnoreCase)
            || value.Equals("yes", StringComparison.OrdinalIgnoreCase)
            || value.Equals("y", StringComparison.OrdinalIgnoreCase);
    }
}
