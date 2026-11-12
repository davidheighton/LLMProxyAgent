using System.Text.Json;
using System.Text.Json.Nodes;

namespace LLMSurvivingProxyAgent.Runtime;

public sealed class AutonomousLoginCredentialStore(string profilePath)
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true
    };
    private readonly SemaphoreSlim _writeLock = new(1, 1);

    public Task SaveLoginNameAsync(string loginName, CancellationToken cancellationToken = default) =>
        UpdateProfileAsync(
            login =>
            {
                login["LoginName"] = loginName;
            },
            cancellationToken);

    public Task SaveTraderAliasAsync(
        string gameProfile,
        string traderAlias,
        CancellationToken cancellationToken = default) =>
        UpdateProfileAsync(
            login =>
            {
                GetGameProfile(login, gameProfile)["TraderAlias"] = traderAlias;
            },
            cancellationToken);

    public Task SaveCharacterNamesAsync(
        string gameProfile,
        string shipName,
        string homePlanetName,
        CancellationToken cancellationToken = default) =>
        UpdateProfileAsync(
            login =>
            {
                var profile = GetGameProfile(login, gameProfile);
                profile["ShipName"] = shipName;
                profile["HomePlanetName"] = homePlanetName;
            },
            cancellationToken);

    public Task SaveGameCredentialsAsync(
        string gameProfile,
        string traderAlias,
        string password,
        CancellationToken cancellationToken = default) =>
        UpdateProfileAsync(
            login =>
            {
                var profile = GetGameProfile(login, gameProfile);
                profile["TraderAlias"] = traderAlias;
                profile["Password"] = password;
            },
            cancellationToken);

    private async Task UpdateProfileAsync(
        Action<JsonObject> update,
        CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(profilePath);
        await _writeLock.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            var fullPath = Path.GetFullPath(profilePath);
            var directory = Path.GetDirectoryName(fullPath)
                ?? throw new InvalidOperationException("The login profile path must include a directory.");
            Directory.CreateDirectory(directory);

            var root = File.Exists(fullPath)
                ? JsonNode.Parse(await File.ReadAllTextAsync(fullPath, cancellationToken).ConfigureAwait(false))
                    as JsonObject
                : new JsonObject();
            if (root is null)
            {
                throw new InvalidDataException("The local game login profile must contain a JSON object.");
            }

            var login = root["AutonomousLogin"] as JsonObject;
            if (login is null)
            {
                throw new InvalidDataException(
                    "The local game login profile must contain an AutonomousLogin object.");
            }
            update(login);

            var temporaryPath = fullPath + "." + Guid.NewGuid().ToString("N") + ".tmp";
            try
            {
                await File.WriteAllTextAsync(
                    temporaryPath,
                    root.ToJsonString(JsonOptions),
                    cancellationToken).ConfigureAwait(false);
                File.Move(temporaryPath, fullPath, overwrite: true);
            }
            finally
            {
                if (File.Exists(temporaryPath))
                {
                    File.Delete(temporaryPath);
                }
            }
        }
        finally
        {
            _writeLock.Release();
        }
    }

    private static JsonObject GetGameProfile(JsonObject login, string gameProfile)
    {
        var gamesNode = login["Games"];
        if (gamesNode is null)
        {
            var games = new JsonObject();
            login["Games"] = games;
            gamesNode = games;
        }
        if (gamesNode is not JsonObject gameProfiles)
        {
            throw new InvalidDataException("The local login profile Games value must be a JSON object.");
        }

        var configuredKey = gameProfiles.Select(entry => entry.Key).FirstOrDefault(key =>
            string.Equals(key, gameProfile, StringComparison.OrdinalIgnoreCase));
        if (configuredKey is null)
        {
            configuredKey = gameProfile;
            gameProfiles[configuredKey] = new JsonObject();
        }

        return gameProfiles[configuredKey] as JsonObject
            ?? throw new InvalidDataException(
                $"The local login profile for game '{gameProfile}' must be a JSON object.");
    }
}
