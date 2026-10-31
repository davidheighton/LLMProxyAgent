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
    private string? _password;
    private bool _loginNameSent;
    private bool _gameLetterSent;
    private bool _menuChoiceSent;
    private bool _newCharacterOffered;
    private bool _passwordSent;
    private bool _passwordConfirmationSent;
    private bool _traderAliasSent;
    private bool _traderAliasPreviewAccepted;
    private bool _shipNameHandled;

    public bool PasswordSubmitted => _passwordSent;
    public bool ShouldStopAfterProgress => _menuChoiceSent || _newCharacterOffered || _passwordSent;

    public void SetLoginName(string loginName) => _loginName = loginName;

    public void SetTraderAlias(string traderAlias) => _traderAlias = traderAlias;

    public void SetExistingGamePassword(string password) => _password = password;

    public void SetNewCharacterCredentials(string traderAlias, string password)
    {
        _traderAlias = traderAlias;
        _password = password;
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
            case "ServerRegistration" when _loginNameSent && !_gameLetterSent:
                await sendAsync(Encoding.ASCII.GetBytes(gameLetter), cancellationToken).ConfigureAwait(false);
                _gameLetterSent = true;
                return LoginPromptResult.InputSent;
            case "ServerRegistration" when _gameLetterSent:
                return LoginPromptResult.Ignored;
            case "Pause" when _gameLetterSent && _pauseResponses < MaximumPauseResponses:
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
            case "TraderAlias" when _newCharacterOffered && !_traderAliasSent:
                await SendRequiredValueAsync("N" + _traderAlias, "trader alias", sendAsync, cancellationToken)
                    .ConfigureAwait(false);
                _traderAliasSent = true;
                return LoginPromptResult.InputSent;
            case "TraderAliasPreview" when _newCharacterOffered && _traderAliasSent &&
                                               !_traderAliasPreviewAccepted:
                await SendAsync("Y.*", sendAsync, cancellationToken).ConfigureAwait(false);
                await SendAsync("Y", sendAsync, cancellationToken).ConfigureAwait(false);
                _traderAliasPreviewAccepted = true;
                _shipNameHandled = true;
                return LoginPromptResult.InputSent;
            case "TraderAliasPreview" when _traderAliasPreviewAccepted:
                return LoginPromptResult.Ignored;
            case "ShipName" when _newCharacterOffered && _passwordSent && _shipNameHandled:
                return LoginPromptResult.Ignored;
            case "ShipName" or "HomePlanetName" when _newCharacterOffered && _passwordSent:
                await SendAsync(".*Y", sendAsync, cancellationToken).ConfigureAwait(false);
                return LoginPromptResult.InputSent;
            case "PortTradeQuantity" when allowAuthenticatedCommandPrompt:
            case "Command" when _passwordSent || allowAuthenticatedCommandPrompt:
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
                "PasswordConfirmation" or "TraderAlias" or "ShipName" or "HomePlanetName" or "Command":
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
