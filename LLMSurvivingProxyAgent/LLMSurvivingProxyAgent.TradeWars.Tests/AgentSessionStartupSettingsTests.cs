using LLMSurvivingProxyAgent.Core;

namespace LLMSurvivingProxyAgent.TradeWars.Tests;

public sealed class AgentSessionStartupSettingsTests
{
    [Fact]
    public void MissingSettingPreservesAutomaticStartupByDefault()
    {
        Assert.True(AgentSessionStartupSettings.IsAutoStartEnabled(CreateAgent()));
    }

    [Theory]
    [InlineData("true", true)]
    [InlineData("false", false)]
    public void ReadsSavedAutomaticStartupSetting(string value, bool expected)
    {
        var agent = CreateAgent();
        agent.Configuration[AgentSessionStartupSettings.AutoStartConfigurationKey] = value;

        Assert.Equal(expected, AgentSessionStartupSettings.IsAutoStartEnabled(agent));
    }

    [Fact]
    public void RejectsInvalidAutomaticStartupSetting()
    {
        var agent = CreateAgent();
        agent.Configuration[AgentSessionStartupSettings.AutoStartConfigurationKey] = "sometimes";

        Assert.Throws<InvalidDataException>(() =>
            AgentSessionStartupSettings.IsAutoStartEnabled(agent));
    }

    [Fact]
    public void SettingAutomaticStartupUpdatesOnlyThatAgent()
    {
        var agent = CreateAgent();

        AgentSessionStartupSettings.SetAutoStartEnabled(agent, false);

        Assert.False(AgentSessionStartupSettings.IsAutoStartEnabled(agent));
    }

    private static AgentContainer CreateAgent() =>
        new()
        {
            AgentId = "meridian",
            DisplayName = "Meridian"
        };
}
