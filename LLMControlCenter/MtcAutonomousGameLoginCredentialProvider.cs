using System.Text.Json;
using System.Text.RegularExpressions;
using System.Security.Cryptography;
using LLMSurvivingProxyAgent.Runtime;

namespace MTC;

internal sealed class MtcAutonomousGameLoginCredentialProvider(
    string profilePath,
    Func<string> getCurrentGameName,
    bool createAccountIfNeeded = false) : IAutonomousGameLoginCredentialProvider
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true
    };
    private static readonly JsonSerializerOptions WriteJsonOptions = new()
    {
        WriteIndented = true
    };
    private static readonly Regex GameLetterPattern = new(
        @"\bGame[\s_-]+(?<letter>[A-Za-z])\b",
        RegexOptions.Compiled | RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);

    public AutonomousGameLoginCredentials? GetCredentials(string agentId)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(agentId);
        ArgumentException.ThrowIfNullOrWhiteSpace(profilePath);
        ArgumentNullException.ThrowIfNull(getCurrentGameName);

        string currentGameName = getCurrentGameName();
        Match activeGame = GameLetterPattern.Match(currentGameName);
        if (!activeGame.Success)
        {
            throw new InvalidOperationException(
                "MTC could not identify the active game's letter; autonomous login was blocked.");
        }

        string activeGameLetter = activeGame.Groups["letter"].Value.ToUpperInvariant();
        AutonomousGameLoginCredentials? configuredCredentials =
            ReadConfiguredCredentials(profilePath, agentId, activeGameLetter);
        if (configuredCredentials is not null)
        {
            return configuredCredentials;
        }

        return createAccountIfNeeded
            ? ReadOrCreateGeneratedCredentials(agentId, activeGameLetter)
            : null;
    }

    private static AutonomousGameLoginCredentials? ReadConfiguredCredentials(
        string path,
        string agentId,
        string activeGameLetter)
    {
        if (!File.Exists(path))
        {
            return null;
        }

        using JsonDocument document = JsonDocument.Parse(File.ReadAllText(path));
        if (!document.RootElement.TryGetProperty("AutonomousLogin", out JsonElement loginElement))
        {
            return null;
        }

        AutonomousLoginOptions options =
            loginElement.Deserialize<AutonomousLoginOptions>(JsonOptions)
            ?? throw new InvalidDataException("The local login profile is invalid.");
        if (!options.Enabled ||
            !string.Equals(options.AgentId, agentId, StringComparison.OrdinalIgnoreCase))
        {
            return null;
        }
        if (string.IsNullOrWhiteSpace(options.SelectedGame))
        {
            throw new InvalidDataException("The enabled local login profile has no selected game.");
        }

        AutonomousLoginGameProfile? game = options.Games.FirstOrDefault(entry =>
            string.Equals(entry.Key, options.SelectedGame, StringComparison.OrdinalIgnoreCase)).Value;
        if (game is null)
        {
            throw new InvalidDataException("The selected game is missing from the local login profile.");
        }
        if (!string.Equals(game.GameLetter, activeGameLetter, StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException(
                "The saved login profile does not match the active MTC game; autonomous login was blocked.");
        }

        return new AutonomousGameLoginCredentials(
            options.LoginName,
            game.GameLetter,
            game.Password,
            game.TraderAlias,
            game.AllowNewCharacterCreation,
            game.ShipName,
            game.HomePlanetName,
            options.SelectedGame,
            Path.GetFullPath(path));
    }

    private AutonomousGameLoginCredentials ReadOrCreateGeneratedCredentials(
        string agentId,
        string gameLetter)
    {
        string safeAgentId = string.Concat(agentId.Select(character =>
            char.IsAsciiLetterOrDigit(character) || character is '.' or '_' or '-'
                ? character
                : '_'));
        string generatedPath = Path.Combine(
            Path.GetDirectoryName(profilePath)
                ?? throw new InvalidOperationException("The login-profile directory is unavailable."),
            $"{Path.GetFileNameWithoutExtension(profilePath)}-{safeAgentId}-{gameLetter}.json");
        AutonomousGameLoginCredentials? existing =
            ReadConfiguredCredentials(generatedPath, agentId, gameLetter);
        if (existing is not null)
        {
            return existing;
        }

        var options = new AutonomousLoginOptions
        {
            Enabled = true,
            AgentId = agentId,
            LoginName = string.Empty,
            SelectedGame = gameLetter,
            Games =
            {
                [gameLetter] = new AutonomousLoginGameProfile
                {
                    GameLetter = gameLetter,
                    TraderAlias = string.Empty,
                    Password = CreateToken(18),
                    AllowNewCharacterCreation = true
                }
            }
        };
        Directory.CreateDirectory(Path.GetDirectoryName(generatedPath)!);
        string temporaryPath = $"{generatedPath}.{Guid.NewGuid():N}.tmp";
        File.WriteAllText(
            temporaryPath,
            JsonSerializer.Serialize(new { AutonomousLogin = options }, WriteJsonOptions));
        try
        {
            File.Move(temporaryPath, generatedPath);
        }
        catch (IOException) when (File.Exists(generatedPath))
        {
            File.Delete(temporaryPath);
            return ReadConfiguredCredentials(generatedPath, agentId, gameLetter)
                ?? throw new InvalidDataException(
                    "The generated game account profile could not be loaded.");
        }

        return ReadConfiguredCredentials(generatedPath, agentId, gameLetter)
            ?? throw new InvalidDataException(
                "The generated game account profile could not be loaded.");
    }

    private static string CreateToken(int length)
    {
        const string alphabet = "ABCDEFGHIJKLMNOPQRSTUVWXYZ0123456789";
        return string.Create(length, alphabet, static (characters, choices) =>
        {
            for (int index = 0; index < characters.Length; index++)
            {
                characters[index] = choices[RandomNumberGenerator.GetInt32(choices.Length)];
            }
        });
    }
}
