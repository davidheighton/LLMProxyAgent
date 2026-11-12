namespace LLMSurvivingProxyAgent.Runtime;

public sealed record AutonomousGameLoginCredentials(
    string LoginName,
    string GameLetter,
    string Password,
    string TraderAlias = "",
    bool AllowNewCharacterCreation = false,
    string ShipName = "",
    string HomePlanetName = "",
    string GameProfileKey = "",
    string CredentialProfilePath = "");

public interface IAutonomousGameLoginCredentialProvider
{
    AutonomousGameLoginCredentials? GetCredentials(string agentId);
}

public interface IAutonomousGameLoginInputSender
{
    Task SendLoginInputAsync(
        string agentId,
        ReadOnlyMemory<byte> input,
        CancellationToken cancellationToken = default);
}

public enum AutonomousExistingAccountLoginResult
{
    Ignored,
    InputSent,
    DeclinedAccountCreation,
    Authenticated
}

public sealed class AutonomousExistingAccountLoginSession
{
    private readonly TradeWarsLoginPromptSequence _sequence;
    private readonly bool _allowNewCharacterCreation;

    public AutonomousExistingAccountLoginSession(AutonomousGameLoginCredentials credentials)
    {
        ArgumentNullException.ThrowIfNull(credentials);
        ValidateCredentials(credentials);

        _allowNewCharacterCreation = credentials.AllowNewCharacterCreation;
        _sequence = new TradeWarsLoginPromptSequence(
            credentials.GameLetter,
            allowAuthenticatedCommandPrompt: true);
        _sequence.SetLoginName(credentials.LoginName);
        _sequence.SetExistingGamePassword(credentials.Password);
        if (_allowNewCharacterCreation)
        {
            if (string.IsNullOrWhiteSpace(credentials.TraderAlias))
            {
                throw new ArgumentException(
                    "A trader alias is required when new-character creation is enabled.",
                    nameof(credentials));
            }
            if (!IsValidCharacterName(credentials.ShipName) ||
                !IsValidCharacterName(credentials.HomePlanetName))
            {
                throw new ArgumentException(
                    "Ship and home planet names are required when new-character creation is enabled.",
                    nameof(credentials));
            }

            _sequence.SetNewCharacterCredentials(
                credentials.TraderAlias,
                credentials.Password,
                credentials.ShipName,
                credentials.HomePlanetName);
        }
    }

    public bool IsAuthenticated { get; private set; }

    public async Task<AutonomousExistingAccountLoginResult> HandlePromptAsync(
        string promptSurface,
        Func<ReadOnlyMemory<byte>, CancellationToken, Task> sendAsync,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(promptSurface);
        ArgumentNullException.ThrowIfNull(sendAsync);

        if (promptSurface == "NewCharacter" && !_allowNewCharacterCreation)
        {
            await sendAsync(new byte[] { (byte)'N' }, cancellationToken)
                .ConfigureAwait(false);
            return AutonomousExistingAccountLoginResult.DeclinedAccountCreation;
        }
        if (promptSurface == "UnsupportedAccountSetup")
        {
            throw new InvalidDataException("The server requested an unsupported account setup flow.");
        }

        LoginPromptResult result = await _sequence.HandlePromptAsync(
            promptSurface,
            sendAsync,
            cancellationToken).ConfigureAwait(false);
        if (result == LoginPromptResult.Authenticated)
        {
            IsAuthenticated = true;
            return AutonomousExistingAccountLoginResult.Authenticated;
        }

        return result == LoginPromptResult.InputSent
            ? AutonomousExistingAccountLoginResult.InputSent
            : AutonomousExistingAccountLoginResult.Ignored;
    }

    private static void ValidateCredentials(AutonomousGameLoginCredentials credentials)
    {
        if (credentials.LoginName.Length is < 1 or > 64 ||
            credentials.LoginName.Any(character => !char.IsAsciiLetterOrDigit(character)))
        {
            throw new ArgumentException(
                "The configured game login name must contain 1-64 ASCII letters or digits.",
                nameof(credentials));
        }

        if (credentials.GameLetter.Length != 1 || !char.IsAsciiLetter(credentials.GameLetter[0]))
        {
            throw new ArgumentException(
                "The configured game letter must be one ASCII letter.",
                nameof(credentials));
        }

        if (credentials.Password.Length is < 1 or > 128 ||
            credentials.Password.Any(character => character is < ' ' or > '~'))
        {
            throw new ArgumentException(
                "The configured game password must contain 1-128 printable ASCII characters.",
                nameof(credentials));
        }
    }

    private static bool IsValidCharacterName(string value) =>
        !string.IsNullOrWhiteSpace(value) &&
        value.Length is >= 3 and <= 30 &&
        value == value.Trim() &&
        value.All(character => char.IsAsciiLetter(character) || character == ' ');
}
