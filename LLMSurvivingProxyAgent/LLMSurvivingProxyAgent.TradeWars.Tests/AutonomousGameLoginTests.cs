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
    public async Task ExistingAccountLoginResumesRecognizedPauseBeforeLoginPrompts()
    {
        var session = new AutonomousExistingAccountLoginSession(
            new AutonomousGameLoginCredentials("Meridian8", "D", "Secret42"));
        var sentInputs = new List<string>();

        Task SendAsync(ReadOnlyMemory<byte> input, CancellationToken cancellationToken)
        {
            sentInputs.Add(Encoding.ASCII.GetString(input.Span));
            return Task.CompletedTask;
        }

        AutonomousExistingAccountLoginResult result =
            await session.HandlePromptAsync("Pause", SendAsync);

        Assert.Equal(AutonomousExistingAccountLoginResult.InputSent, result);
        Assert.Equal(["\r"], sentInputs);
        Assert.False(session.IsAuthenticated);
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
                AllowNewCharacterCreation: true,
                ShipName: "Starward Crown",
                HomePlanetName: "New Terra"));
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
        await session.HandlePromptAsync("TraderAliasChoice", SendAsync);
        await session.HandlePromptAsync("TraderAliasName", SendAsync);
        await session.HandlePromptAsync("TraderAliasPreview", SendAsync);
        await session.HandlePromptAsync("NameConfirmation", SendAsync);
        await session.HandlePromptAsync("ShipName", SendAsync);
        await session.HandlePromptAsync("NameConfirmation", SendAsync);
        await session.HandlePromptAsync("HomePlanetName", SendAsync);
        await session.HandlePromptAsync("NameConfirmation", SendAsync);
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
                "N",
                "MeridianTrader\r",
                "Y",
                "Starward Crown\r",
                "Y",
                "New Terra\r",
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

    [Fact]
    public async Task EchoedLoginNameIsNotTreatedAsRepeatedPrompt()
    {
        var session = new AutonomousExistingAccountLoginSession(
            new AutonomousGameLoginCredentials("NexusRider", "E", "Secret42"));
        var sentInputs = new List<string>();

        Task SendAsync(ReadOnlyMemory<byte> input, CancellationToken cancellationToken)
        {
            sentInputs.Add(Encoding.ASCII.GetString(input.Span));
            return Task.CompletedTask;
        }

        Assert.Equal(AutonomousExistingAccountLoginResult.InputSent, await session.HandlePromptAsync("BBSName", SendAsync));
        Assert.Equal(AutonomousExistingAccountLoginResult.Ignored, await session.HandlePromptAsync("BBSName", SendAsync));
        Assert.Equal(AutonomousExistingAccountLoginResult.InputSent, await session.HandlePromptAsync("ServerRegistration", SendAsync));

        Assert.Equal(["NexusRider\r", "E"], sentInputs);
    }

    [Fact]
    public async Task NewCharacterCommandBeforeShipConfirmationIsRejected()
    {
        var session = new AutonomousExistingAccountLoginSession(
            new AutonomousGameLoginCredentials(
                "NexusRider",
                "E",
                "Secret42",
                "NexusRider",
                AllowNewCharacterCreation: true,
                ShipName: "Starward Crown",
                HomePlanetName: "New Terra"));
        static Task SendAsync(ReadOnlyMemory<byte> input, CancellationToken cancellationToken) => Task.CompletedTask;

        foreach (string prompt in new[]
                 {
                     "BBSName", "ServerRegistration", "Menu", "NewCharacter", "Password", "PasswordConfirmation",
                     "TraderAlias", "TraderAliasChoice", "TraderAliasName", "TraderAliasPreview", "NameConfirmation", "ShipName"
                 })
        {
            await session.HandlePromptAsync(prompt, SendAsync);
        }
        await Assert.ThrowsAsync<InvalidDataException>(() => session.HandlePromptAsync("Command", SendAsync));
    }

    [Fact]
    public async Task NewCharacterWithoutHomePlanetPromptAuthenticatesAtCommand()
    {
        var session = new AutonomousExistingAccountLoginSession(
            new AutonomousGameLoginCredentials(
                "NexusRider",
                "E",
                "Secret42",
                "NexusRider",
                AllowNewCharacterCreation: true,
                ShipName: "Starward Crown",
                HomePlanetName: "New Terra"));
        static Task SendAsync(ReadOnlyMemory<byte> input, CancellationToken cancellationToken) => Task.CompletedTask;

        foreach (string prompt in new[]
                 {
                     "BBSName", "ServerRegistration", "Menu", "NewCharacter", "Password", "PasswordConfirmation",
                     "TraderAlias", "TraderAliasChoice", "TraderAliasName", "TraderAliasPreview", "NameConfirmation", "ShipName", "NameConfirmation"
                 })
        {
            await session.HandlePromptAsync(prompt, SendAsync);
        }

        Assert.Equal(AutonomousExistingAccountLoginResult.Authenticated, await session.HandlePromptAsync("Command", SendAsync));
    }

    [Fact]
    public async Task RejectedAliasIsReplacedWithDistinctFallbackThenFailsCleanly()
    {
        var session = new AutonomousExistingAccountLoginSession(
            new AutonomousGameLoginCredentials(
                "EchidnaEra",
                "E",
                "Secret42",
                "EchidnaEraPrime",
                AllowNewCharacterCreation: true,
                ShipName: "Starward Crown",
                HomePlanetName: "New Terra"));
        var sentInputs = new List<string>();

        Task SendAsync(ReadOnlyMemory<byte> input, CancellationToken cancellationToken)
        {
            sentInputs.Add(Encoding.ASCII.GetString(input.Span));
            return Task.CompletedTask;
        }

        foreach (string prompt in new[]
                 {
                     "BBSName", "ServerRegistration", "Menu", "NewCharacter", "Password", "PasswordConfirmation",
                     "TraderAlias", "TraderAliasChoice", "TraderAliasName"
                 })
        {
            await session.HandlePromptAsync(prompt, SendAsync);
        }
        Assert.Equal("EchidnaEraPrime\r", sentInputs[^1]);

        for (int retry = 0; retry < 2; retry++)
        {
            Assert.Equal(AutonomousExistingAccountLoginResult.InputSent, await session.HandlePromptAsync("TraderAliasName", SendAsync));
            string alias = sentInputs[^1].TrimEnd('\r');
            Assert.DoesNotContain("EchidnaEra", alias, StringComparison.OrdinalIgnoreCase);
            Assert.InRange(alias.Length, 3, 20);
        }
        await Assert.ThrowsAsync<InvalidDataException>(() => session.HandlePromptAsync("TraderAliasName", SendAsync));
        Assert.False(TradeWarsTelnetTelemetryDecoder.TryGetPromptSurface("What Alias do you want to use? NyxSiren", out string echo) &&
                     echo == "TraderAliasName");
    }

    [Fact]
    public async Task NewCharacterLeavesHomePlanetBeforeAuthenticatingAtCommand()
    {
        var session = new AutonomousExistingAccountLoginSession(
            new AutonomousGameLoginCredentials(
                "Meridian8",
                "E",
                "Secret42",
                "MeridianTrader",
                AllowNewCharacterCreation: true,
                ShipName: "Starward Crown",
                HomePlanetName: "New Terra"));
        var sentInputs = new List<string>();

        Task SendAsync(ReadOnlyMemory<byte> input, CancellationToken cancellationToken)
        {
            sentInputs.Add(Encoding.ASCII.GetString(input.Span));
            return Task.CompletedTask;
        }

        Assert.Equal(AutonomousExistingAccountLoginResult.Ignored, await session.HandlePromptAsync("Planet", SendAsync));
        foreach (string prompt in new[]
                 {
                     "BBSName", "ServerRegistration", "Menu", "NewCharacter", "Password", "PasswordConfirmation",
                     "TraderAlias", "TraderAliasChoice", "TraderAliasName", "TraderAliasPreview", "NameConfirmation", "ShipName", "NameConfirmation",
                     "HomePlanetName"
                 })
        {
            await session.HandlePromptAsync(prompt, SendAsync);
        }
        int beforePlanet = sentInputs.Count;
        Assert.Equal(AutonomousExistingAccountLoginResult.InputSent, await session.HandlePromptAsync("Planet", SendAsync));
        Assert.Equal(AutonomousExistingAccountLoginResult.InputSent, await session.HandlePromptAsync("Planet", SendAsync));
        Assert.Equal(AutonomousExistingAccountLoginResult.Ignored, await session.HandlePromptAsync("Planet", SendAsync));
        Assert.Equal(AutonomousExistingAccountLoginResult.Authenticated, await session.HandlePromptAsync("Command", SendAsync));

        Assert.Equal(["Q", "Q"], sentInputs.Skip(beforePlanet));
    }

    [Theory]
    [InlineData("Please enter your name (ENTER for none):", "BBSName")]
    [InlineData("Please enter your name", "BBSName")]
    [InlineData("Would you like to start a new character in this game? (Type Y or N)", "NewCharacter")]
    [InlineData("Would you like to create a new account? (Y/N)", "NewCharacter")]
    [InlineData("Do you wish to make up a new account?", "NewCharacter")]
    [InlineData("Password:", "Password")]
    [InlineData("Repeat password to verify.", "PasswordConfirmation")]
    [InlineData("What do you want to name your ship? (30 letters)", "ShipName")]
    [InlineData("What do you want to name your home planet? (Class O-J, Oceanic)", "HomePlanetName")]
    [InlineData("New Terra is what you want? (Y/N)", "NameConfirmation")]
    [InlineData("Do you wish to clear some avoids? (Y/N) [N]", "ClearAvoids")]
    [InlineData("Command [TL=00:00:00]:[1]", "Command")]
    [InlineData("Planet command (?=help) [D]", "Planet")]
    [InlineData("Use (N)ew Name or (B)BS Name [B] ?", "TraderAliasChoice")]
    [InlineData("What Alias do you want to use? ", "TraderAliasName")]
    [InlineData("Planet command [TL=00:00:00]:", "Planet")]
    [InlineData("Which Sector [369] ?", "MoveSector")]
    [InlineData("Stop in this sector (Y,N,E,I,R,S,D,P,?) (?=Help) [N] ?", "StopInSector")]
    [InlineData("Engage the Autopilot? (Y/N/Single step/Express) [Y]", "AutopilotConfirmation")]
    [InlineData("How many holds of Fuel Ore do you want to buy [20]?", "PortTradeQuantity")]
    [InlineData("How many holds of Equipment do you want to sell [2]?", "PortTradeQuantity")]
    [InlineData("Your offer [1,234] ?", "PortTradeOffer")]
    public void PromptSurfaceClassifierRecognizesTerminalText(string text, string expectedSurface)
    {
        Assert.True(TradeWarsTelnetTelemetryDecoder.TryGetPromptSurface(text, out string surface));
        Assert.Equal(expectedSurface, surface);
    }
}
