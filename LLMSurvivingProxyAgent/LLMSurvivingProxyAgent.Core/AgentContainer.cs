namespace LLMSurvivingProxyAgent.Core;

public enum AgentStatus
{
    Offline,
    Starting,
    Running,
    Thinking,
    Paused,
    Dead,
    WaitingForReentry,
    WaitingForBigBang
}

public sealed class AgentContainer
{
    public required string AgentId { get; init; }
    public required string DisplayName { get; set; }
    public string Description { get; set; } = string.Empty;
    public DateTimeOffset CreatedDate { get; init; } = DateTimeOffset.UtcNow;
    public string PreferredModel { get; set; } = string.Empty;
    public string PreferredProvider { get; set; } = string.Empty;
    public AgentStatus Status { get; set; } = AgentStatus.Offline;
    public List<string> Goals { get; init; } = [];
    public Dictionary<string, string> Configuration { get; init; } = [];
    public List<AgentJournalEntry> Journal { get; init; } = [];
    public AgentStatistics Statistics { get; init; } = new();
}

public sealed record AgentJournalEntry(DateTimeOffset OccurredAt, string Kind, string Message);

public sealed class AgentStatistics
{
    public long DecisionsMade { get; set; }
    public long MacrosExecuted { get; set; }
    public long SuccessfulMacros { get; set; }
}
