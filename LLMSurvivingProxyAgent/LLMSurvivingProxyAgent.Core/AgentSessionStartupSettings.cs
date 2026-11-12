namespace LLMSurvivingProxyAgent.Core;

public static class AgentSessionStartupSettings
{
    public const string AutoStartConfigurationKey = "Session:AutoStart";

    public static bool IsAutoStartEnabled(AgentContainer agent)
    {
        ArgumentNullException.ThrowIfNull(agent);
        if (!agent.Configuration.TryGetValue(AutoStartConfigurationKey, out string? value))
        {
            return true;
        }

        if (bool.TryParse(value, out bool enabled))
        {
            return enabled;
        }

        throw new InvalidDataException(
            $"Agent '{agent.AgentId}' has an invalid automatic-start setting.");
    }

    public static void SetAutoStartEnabled(AgentContainer agent, bool enabled)
    {
        ArgumentNullException.ThrowIfNull(agent);
        agent.Configuration[AutoStartConfigurationKey] =
            enabled.ToString(System.Globalization.CultureInfo.InvariantCulture);
    }
}
