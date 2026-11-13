namespace LLMSurvivingProxyAgent.Core;

public sealed record LlmInteractionRecord(
    string InteractionId,
    string AgentId,
    string Role,
    string Provider,
    string Model,
    int Attempt,
    DateTimeOffset StartedAt,
    DateTimeOffset CompletedAt,
    string Prompt,
    string Response,
    bool Succeeded,
    string? Error,
    string? AppliedOutcome = null)
{
    public TimeSpan Duration => CompletedAt - StartedAt;
    public bool IsInProgress { get; init; }

    /// <summary>The JSON schema the answer was constrained to, so recorded prompts can be replayed faithfully.</summary>
    public string? RequestSchema { get; init; }
}

public interface ILlmInteractionHistoryStore
{
    void Record(LlmInteractionRecord interaction);
    void RecordAppliedOutcome(string interactionId, string outcome);
    IReadOnlyList<LlmInteractionRecord> GetRecent(string agentId, int limit = 50);
    IReadOnlyList<LlmInteractionRecord> GetRecentByRole(string agentId, string role, int limit = 50);
}
