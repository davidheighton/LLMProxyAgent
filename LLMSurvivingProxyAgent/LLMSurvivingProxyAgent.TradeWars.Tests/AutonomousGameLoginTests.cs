using System.Text;
using LLMSurvivingProxyAgent.Runtime;

namespace LLMSurvivingProxyAgent.TradeWars.Tests;

public sealed class AutonomousGameLoginTests
{
    [Fact]
    public async Task ExistingAccountLoginSubmitsOnlyExpectedLoginStages()
    {
        var session = new AutonomousExistingAccountLoginSession(
            new AutonomousGameLoginCredentials("Meridian8", "D", "Secret42"));
        var sentInputs = new List<string>();

        Task SendAsync(ReadOnlyMemory<byte> input, CancellationToken cancellationToken)
        {
            sentInputs.Add(Encoding.ASCII.GetString(input.Span));
            return Task.CompletedTask;
        }

        await session.HandlePromptAsync("BBSName", SendAsync);
        await session.HandlePromptAsync("ServerRegistration", SendAsync);
        await session.HandlePromptAsync("Pause", SendAsync);
        await session.HandlePromptAsync("Menu", SendAsync);
        await session.HandlePromptAsync("DailyLog", SendAsync);
        await session.HandlePromptAsync("Password", SendAsync);
        AutonomousExistingAccountLoginResult result =
            await session.HandlePromptAsync("Command", SendAsync);

        Assert.Equal(
            ["Meridian8\r", "D", "\r", "T\r", "\r", "Secret42\r"],
            sentInputs);
        Assert.Equal(AutonomousExistingAccountLoginResult.Authenticated, result);
        Assert.True(session.IsAuthenticated);
    }

    [Fact]
    public async Task ExistingAccountLoginAttachesToAuthenticatedCommandPrompt()
    {
        var session = new AutonomousExistingAccountLoginSession(
            new AutonomousGameLoginCredentials("Meridian8", "D", "Secret42"));
        var inputCount = 0;

        Task SendAsync(ReadOnlyMemory<byte> input, CancellationToken cancellationToken)
        {
            inputCount++;
            return Task.CompletedTask;
        }

        AutonomousExistingAccountLoginResult result =
            await session.HandlePromptAsync("Command", SendAsync);

        Assert.Equal(AutonomousExistingAccountLoginResult.Authenticated, result);
        Assert.True(session.IsAuthenticated);
        Assert.Equal(0, inputCount);
    }

    [Fact]
    public async Task ExistingAccountLoginAttachesToAuthenticatedStopInSectorPrompt()
    {
        var session = new AutonomousExistingAccountLoginSession(
            new AutonomousGameLoginCredentials("Meridian8", "D", "Secret42"));
        var inputCount = 0;

        Task SendAsync(ReadOnlyMemory<byte> input, CancellationToken cancellationToken)
        {
            inputCount++;
            return Task.CompletedTask;
        }

        AutonomousExistingAccountLoginResult result =
            await session.HandlePromptAsync("StopInSector", SendAsync);

        Assert.Equal(AutonomousExistingAccountLoginResult.Authenticated, result);
        Assert.True(session.IsAuthenticated);
        Assert.Equal(0, inputCount);
    }

    [Fact]
    public async Task ExistingAccountLoginAttachesToAuthenticatedMoveSectorPrompt()
    {
        var session = new AutonomousExistingAccountLoginSession(
            new AutonomousGameLoginCredentials("Meridian8", "D", "Secret42"));
        var inputCount = 0;

        Task SendAsync(ReadOnlyMemory<byte> input, CancellationToken cancellationToken)
        {
            inputCount++;
            return Task.CompletedTask;
        }

        AutonomousExistingAccountLoginResult result =
            await session.HandlePromptAsync("MoveSector", SendAsync);

        Assert.Equal(AutonomousExistingAccountLoginResult.Authenticated, result);
        Assert.True(session.IsAuthenticated);
        Assert.Equal(0, inputCount);
    }

    [Fact]
    public async Task ExistingAccountLoginAttachesToAuthenticatedPortTradePrompt()
    {
        var session = new AutonomousExistingAccountLoginSession(
            new AutonomousGameLoginCredentials("Meridian8", "D", "Secret42"));
        var inputCount = 0;

        Task SendAsync(ReadOnlyMemory<byte> input, CancellationToken cancellationToken)
        {
            inputCount++;
            return Task.CompletedTask;
        }

        AutonomousExistingAccountLoginResult result =
            await session.HandlePromptAsync("PortTradeQuantity", SendAsync);

        Assert.Equal(AutonomousExistingAccountLoginResult.Authenticated, result);
        Assert.True(session.IsAuthenticated);
        Assert.Equal(0, inputCount);
    }

    [Fact]
    public async Task ExistingAccountLoginAttachesToAuthenticatedAutopilotPrompt()
    {
        var session = new AutonomousExistingAccountLoginSession(
            new AutonomousGameLoginCredentials("Meridian8", "D", "Secret42"));
        var inputCount = 0;

        Task SendAsync(ReadOnlyMemory<byte> input, CancellationToken cancellationToken)
        {
            inputCount++;
            return Task.CompletedTask;
        }

        AutonomousExistingAccountLoginResult result =
            await session.HandlePromptAsync("AutopilotConfirmation", SendAsync);

        Assert.Equal(AutonomousExistingAccountLoginResult.Authenticated, result);
        Assert.True(session.IsAuthenticated);
        Assert.Equal(0, inputCount);
    }

    [Fact]
    public async Task ExistingAccountLoginDeclinesNewCharacterCreation()
    {
        var session = new AutonomousExistingAccountLoginSession(
            new AutonomousGameLoginCredentials("Meridian8", "D", "Secret42"));
        var sentInputs = new List<string>();

        Task SendAsync(ReadOnlyMemory<byte> input, CancellationToken cancellationToken)
        {
            sentInputs.Add(Encoding.ASCII.GetString(input.Span));
            return Task.CompletedTask;
        }

        await session.HandlePromptAsync("BBSName", SendAsync);
        await session.HandlePromptAsync("ServerRegistration", SendAsync);
        await session.HandlePromptAsync("Pause", SendAsync);
        await session.HandlePromptAsync("Menu", SendAsync);

        AutonomousExistingAccountLoginResult result =
            await session.HandlePromptAsync("NewCharacter", SendAsync);

        Assert.Equal(
            ["Meridian8\r", "D", "\r", "T\r", "N"],
            sentInputs);
        Assert.Equal(AutonomousExistingAccountLoginResult.DeclinedAccountCreation, result);
        Assert.False(session.IsAuthenticated);
    }

    [Fact]
    public async Task ConfiguredNewCharacterCreationUsesSavedAliasAndPassword()
    {
        var session = new AutonomousExistingAccountLoginSession(
            new AutonomousGameLoginCredentials(
                "Meridian8",
                "D",
                "Secret42",
                "MeridianTrader",
                AllowNewCharacterCreation: true));
        var sentInputs = new List<string>();

        Task SendAsync(ReadOnlyMemory<byte> input, CancellationToken cancellationToken)
        {
            sentInputs.Add(Encoding.ASCII.GetString(input.Span));
            return Task.CompletedTask;
        }

        await session.HandlePromptAsync("BBSName", SendAsync);
        await session.HandlePromptAsync("ServerRegistration", SendAsync);
        await session.HandlePromptAsync("Pause", SendAsync);
        await session.HandlePromptAsync("Menu", SendAsync);
        AutonomousExistingAccountLoginResult creationResult =
            await session.HandlePromptAsync("NewCharacter", SendAsync);
        await session.HandlePromptAsync("Password", SendAsync);
        await session.HandlePromptAsync("PasswordConfirmation", SendAsync);
        AutonomousExistingAccountLoginResult passwordEchoResult =
            await session.HandlePromptAsync("Password", SendAsync);
        await session.HandlePromptAsync("TraderAlias", SendAsync);
        await session.HandlePromptAsync("TraderAliasPreview", SendAsync);
        AutonomousExistingAccountLoginResult authenticationResult =
            await session.HandlePromptAsync("Command", SendAsync);

        Assert.Equal(
            [
                "Meridian8\r",
                "D",
                "\r",
                "T\r",
                "Y",
                "Secret42\r",
                "Secret42\r",
                "NMeridianTrader\r",
                "Y.\r",
                "Y"
            ],
            sentInputs);
        Assert.Equal(AutonomousExistingAccountLoginResult.InputSent, creationResult);
        Assert.Equal(AutonomousExistingAccountLoginResult.Ignored, passwordEchoResult);
        Assert.Equal(AutonomousExistingAccountLoginResult.Authenticated, authenticationResult);
        Assert.True(session.IsAuthenticated);
    }

    [Fact]
    public void ConfiguredNewCharacterCreationRequiresSavedTraderAlias()
    {
        Assert.Throws<ArgumentException>(() =>
            new AutonomousExistingAccountLoginSession(
                new AutonomousGameLoginCredentials(
                    "Meridian8",
                    "D",
                    "Secret42",
                    AllowNewCharacterCreation: true)));
    }

    [Theory]
    [InlineData("Please enter your name (ENTER for none):", "BBSName")]
    [InlineData("Please enter your name", "BBSName")]
    [InlineData("Would you like to start a new character in this game? (Type Y or N)", "NewCharacter")]
    [InlineData("Password:", "Password")]
    [InlineData("Repeat password to verify.", "PasswordConfirmation")]
    [InlineData("Do you wish to clear some avoids? (Y/N) [N]", "ClearAvoids")]
    [InlineData("Command [TL=00:00:00]:[1]", "Command")]
    [InlineData("Which Sector [369] ?", "MoveSector")]
    [InlineData("Stop in this sector (Y,N,E,I,R,S,D,P,?) (?=Help) [N] ?", "StopInSector")]
    [InlineData("Engage the Autopilot? (Y/N/Single step/Express) [Y]", "AutopilotConfirmation")]
    [InlineData("How many holds of Fuel Ore do you want to buy [20]?", "PortTradeQuantity")]
    [InlineData("How many holds of Equipment do you want to sell [2]?", "PortTradeQuantity")]
    public void PromptSurfaceClassifierRecognizesTerminalText(string text, string expectedSurface)
    {
        Assert.True(TradeWarsTelnetTelemetryDecoder.TryGetPromptSurface(text, out string surface));
        Assert.Equal(expectedSurface, surface);
    }
}
