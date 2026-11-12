using LLMSurvivingProxyAgent.Core;

namespace LLMSurvivingProxyAgent.Runtime;

public sealed record CoachingResult(bool Succeeded, string Message, PlaybookEntry? Entry = null);

/// <summary>
/// The coach's write path into an agent's playbook: tips and rules (from the dashboard or the in-game
/// <c>!coach</c> command), reviews of agent-learned lessons, and annotations on recorded decisions.
/// Coach entries are stored under the agent's memory lineage, or for all agents.
/// </summary>
public sealed class PlaybookCoachingService(
    IPlaybookStore store,
    PlaybookSettings settings,
    IGameSituationProvider? situationProvider = null,
    TimeProvider? timeProvider = null)
{
    private readonly TimeProvider _time = timeProvider ?? TimeProvider.System;

    public PlaybookSettings Settings => settings;

    public string ResolveOwner(string agentId, bool allAgents) =>
        allAgents ? PlaybookEntry.AllAgents : settings.ResolveMemoryId(agentId);

    /// <summary>Applies one <c>!coach ...</c> line typed by the coach.</summary>
    public async Task<CoachingResult> ApplyCommandAsync(
        string agentId,
        string commandText,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(agentId);
        CoachCommand command = CoachCommandParser.Parse(commandText ?? string.Empty);
        switch (command.Kind)
        {
            case CoachCommandKind.Help:
                return new CoachingResult(true, command.Message);
            case CoachCommandKind.Error:
                return new CoachingResult(false, command.Message);
        }

        IReadOnlyList<string> appliesWhen = command.AppliesWhen ?? [];
        string situationNote = string.Empty;
        if (command.UseCurrentSituation)
        {
            IReadOnlySet<string>? current = await CaptureSituationTagsAsync(agentId, cancellationToken)
                .ConfigureAwait(false);
            if (current is null || current.Count == 0)
            {
                return new CoachingResult(false, "'here' needs a synchronized game snapshot; none is available yet.");
            }
            appliesWhen = [.. appliesWhen.Concat(CoachCommandParser.SelectSituationTags(current)).Distinct(StringComparer.Ordinal)];
            situationNote = $" Situation: {PlaybookSituation.Format(current)}.";
        }

        CoachingResult result = await AddEntryAsync(
                ResolveOwner(agentId, allAgents: false),
                command.Tier,
                command.Scope,
                command.ForbiddenAction,
                appliesWhen,
                CoachCommandParser.ToTitle(command.Guidance),
                command.Guidance,
                $"Coached in game.{situationNote}",
                cancellationToken)
            .ConfigureAwait(false);
        if (!result.Succeeded || result.Entry is not { } entry)
        {
            return result;
        }

        string kind = entry.Tier == PlaybookTier.Rule ? $"Rule (never {entry.ForbiddenAction})" : "Tip";
        string scope = entry.Scope == PlaybookScope.Game ? $" for game '{entry.GameKey}'" : string.Empty;
        return result with
        {
            Message = $"{kind} saved{scope}; applies when {PlaybookSituation.Format(entry.AppliesWhen)}."
        };
    }

    public async Task<CoachingResult> AddEntryAsync(
        string ownerId,
        PlaybookTier tier,
        PlaybookScope scope,
        AutonomousTestAction? forbiddenAction,
        IEnumerable<string> appliesWhen,
        string title,
        string guidance,
        string rationale,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(ownerId);
        IReadOnlyList<string> conditions = PlaybookSituation.Normalize(appliesWhen ?? [], out IReadOnlyList<string> invalid);
        if (invalid.Count > 0)
        {
            return new CoachingResult(false, $"Unknown situation tag(s): {string.Join(", ", invalid)}.");
        }
        if (string.IsNullOrWhiteSpace(guidance))
        {
            return new CoachingResult(false, "Guidance is required.");
        }
        if (tier == PlaybookTier.Rule && forbiddenAction is null or AutonomousTestAction.None)
        {
            return new CoachingResult(false, "A rule must name the action it forbids.");
        }
        if (scope == PlaybookScope.Game && string.IsNullOrWhiteSpace(settings.GameKey))
        {
            return new CoachingResult(false, "No current game is known, so a game-scoped entry cannot be saved.");
        }

        DateTimeOffset now = _time.GetUtcNow();
        var entry = new PlaybookEntry(
            Guid.NewGuid().ToString("N"),
            ownerId,
            PlaybookSource.Coach,
            tier,
            scope,
            scope == PlaybookScope.Game ? settings.GameKey : null,
            string.IsNullOrWhiteSpace(title) ? CoachCommandParser.ToTitle(guidance) : title.Trim(),
            guidance.Trim(),
            rationale?.Trim() ?? string.Empty,
            conditions,
            now)
        {
            UpdatedAt = now,
            ForbiddenAction = tier == PlaybookTier.Rule ? forbiddenAction : null
        };
        await store.UpsertEntryAsync(entry, cancellationToken).ConfigureAwait(false);
        return new CoachingResult(true, "Saved.", entry);
    }

    /// <summary>Endorse (or un-endorse) an agent-learned lesson; endorsed lessons start from a coach-level prior.</summary>
    public Task<CoachingResult> EndorseAsync(string entryId, bool endorsed, string? note, CancellationToken cancellationToken = default) =>
        UpdateAsync(entryId, entry => entry with
        {
            CoachEndorsed = entry.Source == PlaybookSource.Distilled && endorsed,
            CoachNote = string.IsNullOrWhiteSpace(note) ? entry.CoachNote : note.Trim(),
            Status = endorsed ? PlaybookStatus.Active : entry.Status
        }, cancellationToken);

    public Task<CoachingResult> SetStatusAsync(string entryId, PlaybookStatus status, CancellationToken cancellationToken = default) =>
        UpdateAsync(entryId, entry => entry with { Status = status }, cancellationToken);

    public Task<CoachingResult> EditAsync(
        string entryId,
        string guidance,
        IEnumerable<string> appliesWhen,
        string? note,
        CancellationToken cancellationToken = default)
    {
        IReadOnlyList<string> conditions = PlaybookSituation.Normalize(appliesWhen ?? [], out IReadOnlyList<string> invalid);
        if (invalid.Count > 0)
        {
            return Task.FromResult(new CoachingResult(false, $"Unknown situation tag(s): {string.Join(", ", invalid)}."));
        }
        if (string.IsNullOrWhiteSpace(guidance))
        {
            return Task.FromResult(new CoachingResult(false, "Guidance is required."));
        }

        return UpdateAsync(entryId, entry => entry with
        {
            Guidance = guidance.Trim(),
            Title = entry.Source == PlaybookSource.Coach ? CoachCommandParser.ToTitle(guidance) : entry.Title,
            AppliesWhen = conditions,
            CoachNote = string.IsNullOrWhiteSpace(note) ? entry.CoachNote : note.Trim(),
            // Editing a learned lesson is a form of endorsement: the coach vouched for the new wording.
            CoachEndorsed = entry.CoachEndorsed || entry.Source == PlaybookSource.Distilled
        }, cancellationToken);
    }

    /// <summary>
    /// Turns the coach's verdict on one recorded decision into a tip scoped to that decision's situation.
    /// When the decision relied on a learned lesson, the note is attached to that lesson too.
    /// </summary>
    public async Task<CoachingResult> AnnotateStepAsync(
        string agentId,
        TrajectoryStep step,
        bool wasGoodDecision,
        string note,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(agentId);
        ArgumentNullException.ThrowIfNull(step);
        if (string.IsNullOrWhiteSpace(note))
        {
            return new CoachingResult(false, "Write what the agent should learn from this decision.");
        }

        IReadOnlyList<string> appliesWhen = CoachCommandParser.SelectSituationTags(
            step.SituationTags.ToHashSet(StringComparer.Ordinal));
        string verdict = wasGoodDecision ? "Good decision" : "Poor decision";
        CoachingResult result = await AddEntryAsync(
                ResolveOwner(agentId, allAgents: false),
                PlaybookTier.Tip,
                PlaybookScope.Universal,
                null,
                appliesWhen,
                CoachCommandParser.ToTitle(note),
                note.Trim(),
                $"{verdict}: {step.Action} (step {step.StepIndex} of episode {step.EpisodeId}) — {Truncate(step.Rationale, 200)}",
                cancellationToken)
            .ConfigureAwait(false);

        if (result.Succeeded &&
            PlaybookCitation.TryGetEntryId(step.CitedKnowledgeNodeId, out string citedId) &&
            await store.GetEntryAsync(citedId, cancellationToken).ConfigureAwait(false) is { Source: PlaybookSource.Distilled } cited)
        {
            await store.UpsertEntryAsync(
                    cited with
                    {
                        CoachNote = $"{verdict} when relied on at step {step.StepIndex}: {Truncate(note.Trim(), 300)}",
                        UpdatedAt = _time.GetUtcNow()
                    },
                    cancellationToken)
                .ConfigureAwait(false);
        }
        return result;
    }

    private async Task<CoachingResult> UpdateAsync(
        string entryId,
        Func<PlaybookEntry, PlaybookEntry> change,
        CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(entryId);
        PlaybookEntry? entry = await store.GetEntryAsync(entryId, cancellationToken).ConfigureAwait(false);
        if (entry is null)
        {
            return new CoachingResult(false, "That playbook entry no longer exists.");
        }

        PlaybookEntry updated = change(entry) with { UpdatedAt = _time.GetUtcNow() };
        await store.UpsertEntryAsync(updated, cancellationToken).ConfigureAwait(false);
        return new CoachingResult(true, "Saved.", updated);
    }

    private async Task<IReadOnlySet<string>?> CaptureSituationTagsAsync(string agentId, CancellationToken cancellationToken)
    {
        if (situationProvider is null)
        {
            return null;
        }

        GameSituationReport situation = await situationProvider
            .CaptureSituationAsync(agentId, cancellationToken)
            .ConfigureAwait(false);
        if (!GameSnapshotValidation.IsValid(situation.Snapshot))
        {
            return null;
        }

        int? starDock = situation.IntegratedGameClient?.StarDockSector;
        return PlaybookSituation.Describe(situation.Snapshot, new PlaybookSituation.Extras(starDock));
    }

    private static string Truncate(string value, int maximumLength) =>
        value.Length <= maximumLength ? value : value[..maximumLength];
}
