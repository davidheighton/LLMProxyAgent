using LLMSurvivingProxyAgent.Core;

namespace LLMSurvivingProxyAgent.Runtime;

public sealed class LlmInteractionHistoryStore : ILlmInteractionHistoryStore
{
    public const int MaximumInteractionsPerAgent = 100;

    private readonly object _sync = new();
    private readonly Dictionary<string, Dictionary<string, List<LlmInteractionRecord>>> _interactions =
        new(StringComparer.OrdinalIgnoreCase);

    public void Record(LlmInteractionRecord interaction)
    {
        ArgumentNullException.ThrowIfNull(interaction);
        ArgumentException.ThrowIfNullOrWhiteSpace(interaction.InteractionId);
        ArgumentException.ThrowIfNullOrWhiteSpace(interaction.AgentId);
        ArgumentException.ThrowIfNullOrWhiteSpace(interaction.Role);
        ArgumentException.ThrowIfNullOrWhiteSpace(interaction.Provider);
        ArgumentException.ThrowIfNullOrWhiteSpace(interaction.Model);

        lock (_sync)
        {
            if (!_interactions.TryGetValue(interaction.AgentId, out var roleInteractions))
            {
                roleInteractions = new Dictionary<string, List<LlmInteractionRecord>>(
                    StringComparer.OrdinalIgnoreCase);
                _interactions.Add(interaction.AgentId, roleInteractions);
            }

            if (!roleInteractions.TryGetValue(interaction.Role, out var agentInteractions))
            {
                agentInteractions = [];
                roleInteractions.Add(interaction.Role, agentInteractions);
            }

            int existingIndex = agentInteractions.FindIndex(
                item => item.InteractionId == interaction.InteractionId);
            if (existingIndex >= 0)
            {
                agentInteractions.RemoveAt(existingIndex);
            }

            agentInteractions.Insert(0, interaction);
            if (agentInteractions.Count > MaximumInteractionsPerAgent)
            {
                agentInteractions.RemoveRange(
                    MaximumInteractionsPerAgent,
                    agentInteractions.Count - MaximumInteractionsPerAgent);
            }
        }
    }

    public void RecordAppliedOutcome(string interactionId, string outcome)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(interactionId);
        ArgumentException.ThrowIfNullOrWhiteSpace(outcome);

        lock (_sync)
        {
            foreach (var roleInteractions in _interactions.Values)
            {
                foreach (var agentInteractions in roleInteractions.Values)
                {
                    int index = agentInteractions.FindIndex(
                        interaction => interaction.InteractionId == interactionId);
                    if (index >= 0)
                    {
                        agentInteractions[index] = agentInteractions[index] with
                        {
                            AppliedOutcome = outcome
                        };
                        return;
                    }
                }
            }
        }

        throw new KeyNotFoundException($"LLM interaction '{interactionId}' was not found.");
    }

    public IReadOnlyList<LlmInteractionRecord> GetRecent(string agentId, int limit = 50)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(agentId);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(limit);

        lock (_sync)
        {
            return _interactions.TryGetValue(agentId, out var roleInteractions)
                ? roleInteractions.Values
                    .SelectMany(interactions => interactions)
                    .OrderByDescending(interaction => interaction.StartedAt)
                    .Take(limit)
                    .ToArray()
                : [];
        }
    }

    public IReadOnlyList<LlmInteractionRecord> GetRecentByRole(
        string agentId,
        string role,
        int limit = 50)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(agentId);
        ArgumentException.ThrowIfNullOrWhiteSpace(role);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(limit);

        lock (_sync)
        {
            return _interactions.TryGetValue(agentId, out var roleInteractions) &&
                roleInteractions.TryGetValue(role, out var interactions)
                    ? interactions.Take(Math.Min(limit, MaximumInteractionsPerAgent)).ToArray()
                    : [];
        }
    }
}
