using System.Text;

namespace LLMSurvivingProxyAgent.Runtime;

internal sealed class TradeWarsLoginPromptSequence(
    string gameLetter,
    bool allowAuthenticatedCommandPrompt = false)
{
    private const int MaximumPauseResponses = 6;
    private int _pauseResponses;
    private string? _loginName;
    private string? _traderAlias;
    private string? _shipName;
    private string? _homePlanetName;
    private string? _password;
    private bool _loginNameSent;
    private bool _gameLetterSent;
    private bool _menuChoiceSent;
    private bool _newCharacterOffered;
    private bool _passwordSent;
    private bool _passwordConfirmationSent;
    private bool _traderAliasSent;
    private bool _traderAliasChoiceSent;
    private int _aliasRetries;
    private const int MaximumAliasRetries = 2;
    private bool _traderAliasPreviewAccepted;
    private bool _shipNameSent;
    private bool _shipNameConfirmed;
    private bool _homePlanetNameSent;
    private bool _homePlanetNameConfirmed;
    private int _planetExitsSent;
    private const int MaximumPlanetExits = 2;

    public bool PasswordSubmitted => _passwordSent;

    // Home-planet naming and its confirmation are optional on some games; the confirmed ship name is the last
    // step every new character completes.
    private bool NewCharacterSetupComplete => !_newCharacterOffered || _shipNameConfirmed;
    public bool ShouldStopAfterProgress => _menuChoiceSent || _newCharacterOffered || _passwordSent;

    public void SetLoginName(string loginName) => _loginName = loginName;

    public void SetTraderAlias(string traderAlias) => _traderAlias = traderAlias;

    public void SetExistingGamePassword(string password) => _password = password;

    public void SetNewCharacterCredentials(
        string traderAlias,
        string password,
        string shipName,
        string homePlanetName)
    {
        _traderAlias = traderAlias;
        _password = password;
        _shipName = shipName;
        _homePlanetName = homePlanetName;
    }

    public async Task<LoginPromptResult> HandlePromptAsync(
        string surface,
        Func<ReadOnlyMemory<byte>, CancellationToken, Task> sendAsync,
        CancellationToken cancellationToken)
    {
        switch (surface)
        {
            case "BBSName" when !_loginNameSent:
                await SendRequiredValueAsync(_loginName, "login name", sendAsync, cancellationToken)
                    .ConfigureAwait(false);
                _loginNameSent = true;
                return LoginPromptResult.InputSent;
            // The server echoes the typed name on the same prompt line; that echo is not a new prompt.
            case "BBSName" when _loginNameSent && !_gameLetterSent:
                return LoginPromptResult.Ignored;
            case "ServerRegistration" when _loginNameSent && !_gameLetterSent:
                await sendAsync(Encoding.ASCII.GetBytes(gameLetter), cancellationToken).ConfigureAwait(false);
                _gameLetterSent = true;
                return LoginPromptResult.InputSent;
            case "ServerRegistration" when _gameLetterSent:
                return LoginPromptResult.Ignored;
            case "Pause" when _pauseResponses < MaximumPauseResponses:
                _pauseResponses++;
                await SendAsync("*", sendAsync, cancellationToken).ConfigureAwait(false);
                return LoginPromptResult.InputSent;
            case "Menu" when _gameLetterSent && !_menuChoiceSent:
                _menuChoiceSent = true;
                await SendAsync("T*", sendAsync, cancellationToken).ConfigureAwait(false);
                return LoginPromptResult.InputSent;
            case "DailyLog" when _menuChoiceSent:
                await SendAsync("*", sendAsync, cancellationToken).ConfigureAwait(false);
                return LoginPromptResult.InputSent;
            case "ClearAvoids":
                await sendAsync(Encoding.ASCII.GetBytes("N"), cancellationToken).ConfigureAwait(false);
                return LoginPromptResult.InputSent;
            case "NewCharacter" when _menuChoiceSent && !_newCharacterOffered:
                await sendAsync(Encoding.ASCII.GetBytes("Y"), cancellationToken).ConfigureAwait(false);
                _newCharacterOffered = true;
                return LoginPromptResult.InputSent;
            case "Password" when _newCharacterOffered && !_passwordSent:
                await SendRequiredValueAsync(_password, "new game password", sendAsync, cancellationToken)
                    .ConfigureAwait(false);
                _passwordSent = true;
                return LoginPromptResult.InputSent;
            case "Password" when _newCharacterOffered && !_passwordConfirmationSent:
                await SendRequiredValueAsync(_password, "new game password", sendAsync, cancellationToken)
                    .ConfigureAwait(false);
                _passwordConfirmationSent = true;
                return LoginPromptResult.InputSent;
            case "Password" when !_passwordSent:
                await SendRequiredValueAsync(_password, "existing game password", sendAsync, cancellationToken)
                    .ConfigureAwait(false);
                _passwordSent = true;
                return LoginPromptResult.InputSent;
            case "PasswordConfirmation" when _newCharacterOffered && _passwordSent &&
                                                !_passwordConfirmationSent:
                await SendRequiredValueAsync(_password, "new game password", sendAsync, cancellationToken)
                    .ConfigureAwait(false);
                _passwordConfirmationSent = true;
                return LoginPromptResult.InputSent;
            case "Password" when _newCharacterOffered && _passwordConfirmationSent:
                return LoginPromptResult.Ignored;
            // The alias is answered at the server's own prompts; typing it after the introductory line was
            // sometimes discarded when the server flushed input before asking for the alias.
            case "TraderAlias" when _newCharacterOffered:
                return LoginPromptResult.Ignored;
            case "TraderAliasChoice" when _newCharacterOffered && !_traderAliasChoiceSent && !_traderAliasSent:
                await sendAsync(Encoding.ASCII.GetBytes("N"), cancellationToken).ConfigureAwait(false);
                _traderAliasChoiceSent = true;
                return LoginPromptResult.InputSent;
            case "TraderAliasChoice" when _newCharacterOffered:
                return LoginPromptResult.Ignored;
            case "TraderAliasName" when _newCharacterOffered && !_traderAliasSent:
                await SendRequiredValueAsync(_traderAlias, "trader alias", sendAsync, cancellationToken)
                    .ConfigureAwait(false);
                _traderAliasSent = true;
                return LoginPromptResult.InputSent;
            // A repeated alias prompt means the server rejected the alias (for example, it contained the BBS name).
            case "TraderAliasName" when _newCharacterOffered && !_traderAliasPreviewAccepted &&
                                        _aliasRetries < MaximumAliasRetries:
                _aliasRetries++;
                _traderAlias = AutonomousTestWorker.DeriveDistinctTraderAlias(_loginName ?? string.Empty);
                await SendRequiredValueAsync(_traderAlias, "trader alias", sendAsync, cancellationToken)
                    .ConfigureAwait(false);
                return LoginPromptResult.InputSent;
            case "TraderAliasName" when _newCharacterOffered && !_traderAliasPreviewAccepted:
                throw new InvalidDataException("The server rejected every generated trader alias.");
            case "TraderAliasName" when _newCharacterOffered:
                return LoginPromptResult.Ignored;
            case "TraderAliasPreview" when _newCharacterOffered && _traderAliasSent &&
                                               !_traderAliasPreviewAccepted:
                return LoginPromptResult.Ignored;
            case "TraderAliasPreview" when _traderAliasPreviewAccepted:
                return LoginPromptResult.Ignored;
            case "NameConfirmation" when _newCharacterOffered && _traderAliasSent &&
                                           !_traderAliasPreviewAccepted:
                await SendAsync("Y", sendAsync, cancellationToken).ConfigureAwait(false);
                _traderAliasPreviewAccepted = true;
                return LoginPromptResult.InputSent;
            case "ShipName" when _newCharacterOffered && _traderAliasPreviewAccepted &&
                                 !_shipNameSent:
                await SendRequiredValueAsync(_shipName, "ship name", sendAsync, cancellationToken)
                    .ConfigureAwait(false);
                _shipNameSent = true;
                return LoginPromptResult.InputSent;
            case "NameConfirmation" when _newCharacterOffered && _shipNameSent &&
                                           !_shipNameConfirmed:
                await SendAsync("Y", sendAsync, cancellationToken).ConfigureAwait(false);
                _shipNameConfirmed = true;
                return LoginPromptResult.InputSent;
            case "HomePlanetName" when _newCharacterOffered && _shipNameConfirmed &&
                                       !_homePlanetNameSent:
                await SendRequiredValueAsync(
                    _homePlanetName,
                    "home planet name",
                    sendAsync,
                    cancellationToken).ConfigureAwait(false);
                _homePlanetNameSent = true;
                return LoginPromptResult.InputSent;
            case "NameConfirmation" when _newCharacterOffered && _homePlanetNameSent &&
                                           !_homePlanetNameConfirmed:
                await SendAsync("Y", sendAsync, cancellationToken).ConfigureAwait(false);
                _homePlanetNameConfirmed = true;
                return LoginPromptResult.InputSent;
            // New characters (and characters that logged off while landed) start at the planet prompt.
            case "Planet" when _passwordSent && NewCharacterSetupComplete &&
                               _planetExitsSent < MaximumPlanetExits:
                _planetExitsSent++;
                await SendAsync("Q", sendAsync, cancellationToken).ConfigureAwait(false);
                return LoginPromptResult.InputSent;
            case "PortTradeQuantity" when allowAuthenticatedCommandPrompt:
            case "Command" when allowAuthenticatedCommandPrompt &&
                                NewCharacterSetupComplete:
            case "Command" when _passwordSent && NewCharacterSetupComplete:
            case "MoveSector" when allowAuthenticatedCommandPrompt:
            case "StopInSector" when allowAuthenticatedCommandPrompt:
            case "AutopilotConfirmation" when allowAuthenticatedCommandPrompt:
                return LoginPromptResult.Authenticated;
            case "UnsupportedAccountSetup":
                throw new InvalidDataException(
                    "The server requested an unsupported account setup flow.");
            case "Pause" when _pauseResponses >= MaximumPauseResponses:
                throw new InvalidDataException("The login flow exceeded its allowed pause count.");
            case "BBSName" or "Menu" or "NewCharacter" or "Password" or "TraderAliasPreview" or
                "PasswordConfirmation" or "TraderAlias" or "ShipName" or "HomePlanetName" or
                "NameConfirmation" or "Command":
                throw new InvalidDataException(
                    $"The login flow received an unexpected or repeated '{surface}' prompt.");
            default:
                return LoginPromptResult.Ignored;
        }
    }

    private static Task SendRequiredValueAsync(
        string? value,
        string valueName,
        Func<ReadOnlyMemory<byte>, CancellationToken, Task> sendAsync,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            throw new InvalidDataException($"No {valueName} is available for the observed prompt.");
        }

        return SendAsync(value + "*", sendAsync, cancellationToken);
    }

    private static Task SendAsync(
        string input,
        Func<ReadOnlyMemory<byte>, CancellationToken, Task> sendAsync,
        CancellationToken cancellationToken) =>
        sendAsync(Encoding.ASCII.GetBytes(input.Replace('*', '\r')), cancellationToken);
}

internal enum LoginPromptResult
{
    Ignored,
    InputSent,
    Authenticated
}
