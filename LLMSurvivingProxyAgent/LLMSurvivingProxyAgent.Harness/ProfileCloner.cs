using System.Text.Json;
using System.Text.Json.Nodes;

namespace LLMSurvivingProxyAgent.Harness;

public enum MapSeed
{
    CopyBase,
    Empty
}

public sealed record ClonedProfile(
    string ProfileFileName,
    string ProfilePath,
    string GameName,
    string DatabasePath,
    bool ShipCatalogCopied,
    bool MapCopied);

/// <summary>
/// Creates a per-episode copy of an MTC game profile. MTC holds an exclusive lock per profile, so parallel
/// episodes need their own profile, TWX database, and game folder. The clone's name keeps the base
/// "Game X" text so MTC derives the same game letter, while the new agent ID yields a new character.
/// </summary>
public static class ProfileCloner
{
    private static readonly JsonSerializerOptions WriteOptions = new() { WriteIndented = true };

    public static ClonedProfile Clone(string baseProfilePath, string agentId, MapSeed mapSeed)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(baseProfilePath);
        ArgumentException.ThrowIfNullOrWhiteSpace(agentId);
        string basePath = Path.GetFullPath(baseProfilePath);
        string gamesDirectory = Path.GetDirectoryName(basePath)
            ?? throw new InvalidOperationException("The base profile has no directory.");
        JsonObject profile = JsonNode.Parse(File.ReadAllText(basePath)) as JsonObject
            ?? throw new InvalidDataException("The base profile is not a JSON object.");
        string baseName = profile["Name"]?.GetValue<string>() is { Length: > 0 } name
            ? name
            : Path.GetFileNameWithoutExtension(basePath);

        string gameName = $"{baseName} - {agentId}";
        string profileFileName = $"{gameName}.json";
        string profilePath = Path.Combine(gamesDirectory, profileFileName);
        if (File.Exists(profilePath))
        {
            throw new InvalidOperationException($"Episode profile '{profileFileName}' already exists.");
        }

        string databasePath = Path.Combine(gamesDirectory, $"{gameName}.xdb");
        bool mapCopied = false;
        if (mapSeed == MapSeed.CopyBase &&
            profile["DatabasePath"]?.GetValue<string>() is { Length: > 0 } baseDatabase &&
            File.Exists(baseDatabase))
        {
            if (IsLockedByAnotherProcess(basePath))
            {
                throw new InvalidOperationException(
                    $"The base profile '{Path.GetFileName(basePath)}' is open in MTC. Close it before cloning its map, " +
                    "or use --map empty.");
            }
            File.Copy(baseDatabase, databasePath, overwrite: false);
            mapCopied = true;
        }

        profile["Id"] = Guid.NewGuid().ToString();
        profile["Name"] = gameName;
        profile["DatabasePath"] = databasePath;
        profile["AutoReconnect"] = true;
        profile["LoginName"] = string.Empty;
        profile["Password"] = string.Empty;
        if (profile["Mtc"] is JsonObject mtc)
        {
            // The base character's cached ship state must not leak into a new character's first decisions.
            mtc.Remove("State");
        }
        if (profile["mombot"] is JsonObject mombot)
        {
            mombot["Enabled"] = false;
            mombot["AutoStart"] = false;
        }

        string gameFolder = Path.Combine(gamesDirectory, gameName);
        Directory.CreateDirectory(gameFolder);
        string baseCatalog = Path.Combine(gamesDirectory, baseName, "ships.cfg");
        bool catalogCopied = false;
        if (File.Exists(baseCatalog))
        {
            File.Copy(baseCatalog, Path.Combine(gameFolder, "ships.cfg"), overwrite: true);
            catalogCopied = true;
        }

        File.WriteAllText(profilePath, profile.ToJsonString(WriteOptions));
        return new ClonedProfile(profileFileName, profilePath, gameName, databasePath, catalogCopied, mapCopied);
    }

    public static bool BaseShipCatalogExists(string baseProfilePath)
    {
        string basePath = Path.GetFullPath(baseProfilePath);
        string baseName = JsonNode.Parse(File.ReadAllText(basePath))?["Name"]?.GetValue<string>()
            ?? Path.GetFileNameWithoutExtension(basePath);
        return File.Exists(Path.Combine(Path.GetDirectoryName(basePath)!, baseName, "ships.cfg"));
    }

    /// <summary>Mirrors MTC's GameFileLock: a profile is open when its .lock file is held exclusively.</summary>
    public static bool IsLockedByAnotherProcess(string profilePath)
    {
        string lockPath = Path.GetFullPath(profilePath) + ".lock";
        if (!File.Exists(lockPath))
        {
            return false;
        }

        try
        {
            using var stream = new FileStream(lockPath, FileMode.Open, FileAccess.ReadWrite, FileShare.None);
            return false;
        }
        catch (IOException)
        {
            return true;
        }
    }
}
