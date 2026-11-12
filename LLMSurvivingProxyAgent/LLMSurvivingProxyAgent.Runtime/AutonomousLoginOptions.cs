namespace LLMSurvivingProxyAgent.Runtime;

public sealed class AutonomousLoginOptions
{
    public bool Enabled { get; set; }
    public string AgentId { get; set; } = "meridian";
    public string LoginName { get; set; } = string.Empty;
    public string SelectedGame { get; set; } = string.Empty;
    public Dictionary<string, AutonomousLoginGameProfile> Games { get; set; } =
        new(StringComparer.OrdinalIgnoreCase);
}

public sealed class AutonomousLoginGameProfile
{
    public string GameLetter { get; set; } = string.Empty;
    public string TraderAlias { get; set; } = string.Empty;
    public string ShipName { get; set; } = string.Empty;
    public string HomePlanetName { get; set; } = string.Empty;
    public string Password { get; set; } = string.Empty;
    public bool AllowNewCharacterCreation { get; set; }
}
