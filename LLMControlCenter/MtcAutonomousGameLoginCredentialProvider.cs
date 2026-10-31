using System.Text.Json;
using System.Text.RegularExpressions;
using LLMSurvivingProxyAgent.Runtime;

namespace MTC;

internal sealed class MtcAutonomousGameLoginCredentialProvider(
    string profilePath,
    Func<string> getCurrentGameName) : IAutonomousGameLoginCredentialProvider
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true
    };
    private static readonly Regex GameLetterPattern = new(
        @"\bGame[\s_-]+(?<letter>[A-Za-z])\b",
        RegexOptions.Compiled | RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);

    public AutonomousGameLoginCredentials? GetCredentials(string agentId)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(agentId);
        ArgumentException.ThrowIfNullOrWhiteSpace(profilePath);
        ArgumentNullException.ThrowIfNull(getCurrentGameName);

        if (!File.Exists(profilePath))
        {
            return null;
        }

        using JsonDocument document = JsonDocument.Parse(File.ReadAllText(profilePath));
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

        string currentGameName = getCurrentGameName();
        Match activeGame = GameLetterPattern.Match(currentGameName);
        if (!activeGame.Success)
        {
            throw new InvalidOperationException(
                "MTC could not identify the active game's letter; autonomous login was blocked.");
        }

        string activeGameLetter = activeGame.Groups["letter"].Value;
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
            game.AllowNewCharacterCreation);
    }
}
