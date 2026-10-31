using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Collections.Concurrent;
using LLMSurvivingProxyAgent.Core;
using LLMSurvivingProxyAgent.Knowledge;

namespace LLMSurvivingProxyAgent.Runtime;

public sealed class AgentExperienceKnowledgeRecorder(
    IKnowledgeStore runtimeKnowledgeStore) : IAgentExperienceRecorder
{
    private const int MaximumRecordedTextLength = 10_000;
    private const int MaximumPlanSectorCount = 1_000;
    private readonly ConcurrentDictionary<string, SemaphoreSlim> _sessionLocks = new(StringComparer.Ordinal);
    private readonly ConcurrentDictionary<string, int> _lastRecordedTurns = new(StringComparer.Ordinal);

    public async Task RecordMacroExecutionAsync(
        MacroExecutionRecord record,
        GameSnapshot? beforeSnapshot = null,
        GameSnapshot? afterSnapshot = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(record);
        ArgumentException.ThrowIfNullOrWhiteSpace(record.AgentId);
        ArgumentException.ThrowIfNullOrWhiteSpace(record.ExecutionId);
        ArgumentException.ThrowIfNullOrWhiteSpace(record.MacroId);
        ArgumentException.ThrowIfNullOrWhiteSpace(record.MacroName);
        ArgumentNullException.ThrowIfNull(record.Result);
        ArgumentNullException.ThrowIfNull(record.Notes);
        ArgumentNullException.ThrowIfNull(record.PlanSectors);
        if (!Enum.IsDefined(record.Status) ||
            !Enum.IsDefined(record.Category) ||
            record.StartTime == default ||
            record.EndTime < record.StartTime ||
            record.Result.Length > MaximumRecordedTextLength ||
            record.Notes.Length > MaximumRecordedTextLength ||
            record.PlanSectors.Count > MaximumPlanSectorCount ||
            record.PlanSectors.Any(sector => sector < 1) ||
            !IsValidSnapshotPair(record, beforeSnapshot, afterSnapshot))
        {
            throw new ArgumentException("Macro telemetry is invalid for agent-experience recording.", nameof(record));
        }

        var experienceId = Hash($"{record.AgentId}\n{record.ExecutionId}");
        var experience = new AgentExperienceEvidence(
            record.ExecutionId,
            record.AgentId,
            "MacroExecution",
            record.Status.ToString(),
            record.StartTime,
            record.EndTime,
            record.Result,
            record.Notes,
            record.PlanSectors.ToArray(),
            GameOutcomeMeasured: false,
            BeforeSnapshot: beforeSnapshot,
            AfterSnapshot: afterSnapshot);
        var snapshotSummary = (beforeSnapshot, afterSnapshot) switch
        {
            (not null, not null) => "Synchronized before/after game-state snapshots were captured as observations only.",
            (not null, null) => "Only a synchronized before-state snapshot was captured; no after-state snapshot is available.",
            (null, not null) => "Only a synchronized after-state snapshot was captured; no before-state snapshot is available.",
            _ => "No synchronized before/after game-state snapshots were available."
        };
        var evidenceText =
            $"Recorded macro-engine status '{record.Status}' for '{record.MacroName}'. " +
            $"{snapshotSummary} This does not establish that the intended in-game outcome occurred.";
        var node = new KnowledgeNode(
            $"agent-experience-node-{experienceId}",
            $"agent-experience-doc-{experienceId}",
            KnowledgeCategory.AgentExperience,
            $"Agent experience: {record.MacroName} ({record.Status})",
            "Recorded macro telemetry and optional synchronized state observations; no game outcome or profitability was measured.",
            1m,
            ExtractionBasis.RuntimeObservation,
            [
                new KnowledgeEvidence(1, "macro execution telemetry", evidenceText)
                {
                    Details = new KnowledgeEvidenceDetails
                    {
                        AgentExperience = experience
                    }
                }
            ]);
        var serializedExperience = JsonSerializer.SerializeToUtf8Bytes(experience);
        var document = new KnowledgeDocument(
            $"agent-experience-doc-{experienceId}",
            "agent-experience",
            $"macro-executions/{experienceId}.json",
            $"{experienceId}.json",
            Convert.ToHexString(SHA256.HashData(serializedExperience)),
            "json",
            record.EndTime,
            [node],
            [])
        {
            OwnerAgentId = record.AgentId
        };

        await runtimeKnowledgeStore.ReplaceAgentDocumentAsync(
            record.AgentId,
            document,
            cancellationToken);
    }

    public async Task RecordTurnObservationAsync(
        string agentId,
        string sessionId,
        int turnNumber,
        GameSnapshot beforeSnapshot,
        GameSnapshot afterSnapshot,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(agentId);
        ArgumentException.ThrowIfNullOrWhiteSpace(sessionId);
        ArgumentNullException.ThrowIfNull(beforeSnapshot);
        ArgumentNullException.ThrowIfNull(afterSnapshot);
        if (agentId.Length > 200 ||
            sessionId.Length > 200 ||
            turnNumber < 1 ||
            !GameSnapshotValidation.IsValid(beforeSnapshot) ||
            !GameSnapshotValidation.IsValid(afterSnapshot) ||
            afterSnapshot.ObservedAt <= beforeSnapshot.ObservedAt)
        {
            throw new ArgumentException(
                "A turn observation requires an ordered turn number and valid synchronized snapshots.");
        }

        var sessionKey = Hash($"{agentId}\n{sessionId}");
        var sessionLock = _sessionLocks.GetOrAdd(sessionKey, _ => new SemaphoreSlim(1, 1));
        await sessionLock.WaitAsync(cancellationToken);
        try
        {
            if (!_lastRecordedTurns.TryGetValue(sessionKey, out var lastRecordedTurn))
            {
                lastRecordedTurn = await LoadLastRecordedTurnAsync(
                    agentId,
                    sessionId,
                    sessionKey,
                    cancellationToken);
                _lastRecordedTurns.TryAdd(sessionKey, lastRecordedTurn);
            }
            if (turnNumber != lastRecordedTurn + 1)
            {
                throw new InvalidOperationException(
                    $"Turn {turnNumber} is not the next consecutive observation for session '{sessionId}'.");
            }

            var experienceId = $"{sessionKey}-turn-{turnNumber}";
            var documentId = $"agent-experience-turn-{sessionKey}-turn-{turnNumber:D10}";
            var experience = new AgentExperienceEvidence(
                experienceId,
                agentId,
                "TurnObservation",
                "Observed",
                beforeSnapshot.ObservedAt,
                afterSnapshot.ObservedAt,
                "Before and after synchronized snapshots were recorded for this turn.",
                "Raw state observations only; no outcome, cause, or lesson was inferred.",
                [],
                GameOutcomeMeasured: false,
                BeforeSnapshot: beforeSnapshot,
                AfterSnapshot: afterSnapshot,
                SessionId: sessionId,
                TurnNumber: turnNumber);
            var node = new KnowledgeNode(
                $"agent-experience-node-{sessionKey}-turn-{turnNumber:D10}",
                documentId,
                KnowledgeCategory.AgentExperience,
                $"Turn observation {turnNumber}",
                "Synchronized before/after game-state snapshots; no outcome or causation inferred.",
                1m,
                ExtractionBasis.RuntimeObservation,
                [
                    new KnowledgeEvidence(
                        1,
                        "synchronized turn snapshots",
                        $"Recorded before/after snapshots for turn {turnNumber}. " +
                        "The observations do not establish an action result or cause.")
                    {
                        Details = new KnowledgeEvidenceDetails
                        {
                            AgentExperience = experience
                        }
                    }
                ]);
            var contentHash = Convert.ToHexString(
                SHA256.HashData(JsonSerializer.SerializeToUtf8Bytes(experience)));
            var document = new KnowledgeDocument(
                documentId,
                "agent-experience-turns",
                $"sessions/{sessionKey}/turn-{turnNumber:D10}.json",
                $"turn-{turnNumber:D10}.json",
                contentHash,
                "json",
                afterSnapshot.ObservedAt,
                [node],
                [])
            {
                OwnerAgentId = agentId
            };

            await runtimeKnowledgeStore.ReplaceAgentDocumentAsync(
                agentId,
                document,
                cancellationToken);
            _lastRecordedTurns[sessionKey] = turnNumber;
        }
        finally
        {
            sessionLock.Release();
        }
    }

    public async Task RecordSituationObservationAsync(
        string agentId,
        string sessionId,
        GameSituationReport report,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(agentId);
        ArgumentException.ThrowIfNullOrWhiteSpace(sessionId);
        ArgumentNullException.ThrowIfNull(report);
        if (agentId.Length > 200 ||
            sessionId.Length > 200 ||
            !IsRecordableSituation(report))
        {
            throw new ArgumentException(
                "A situation observation requires a connected, authenticated, healthy report with a valid snapshot.",
                nameof(report));
        }

        var snapshot = report.Snapshot!;
        var sessionKey = Hash($"{agentId}\n{sessionId}");
        var experienceId = $"{sessionKey}-situation-{Hash(report.CapturedAt.ToString("O"))}";
        var documentId = $"agent-experience-situation-{experienceId}";
        var experience = new AgentExperienceEvidence(
            experienceId,
            agentId,
            "SituationObservation",
            "Observed",
            snapshot.ObservedAt,
            snapshot.ObservedAt,
            "An authenticated, synchronized situation report was captured.",
            "Observed state only; no action outcome, cause, or lesson was inferred.",
            [],
            GameOutcomeMeasured: false,
            AfterSnapshot: snapshot,
            SessionId: sessionId)
        {
            SituationReport = report
        };
        var knownValues = new List<string>();
        if (snapshot.CurrentSector is { } sector)
        {
            knownValues.Add($"sector {sector}");
        }
        if (snapshot.SynchronizedCredits is { } credits)
        {
            knownValues.Add($"{credits} credits");
        }
        if (snapshot.Turns is { } turns)
        {
            knownValues.Add($"{turns} turns");
        }
        if (snapshot.Ship is { } ship)
        {
            knownValues.Add($"ship type {ship.ShipType}");
        }

        var evidenceText =
            $"Captured an authenticated situation report at {report.CapturedAt:O}. " +
            $"Observed values: {(knownValues.Count == 0 ? "none" : string.Join(", ", knownValues))}. " +
            $"Unknown fields: {(report.UnknownFields.Count == 0 ? "none" : string.Join(", ", report.UnknownFields))}. " +
            "This is a raw observation and does not imply an action outcome.";
        var node = new KnowledgeNode(
            $"agent-experience-node-{documentId}",
            documentId,
            KnowledgeCategory.AgentExperience,
            "Authenticated situation observation",
            "Trusted synchronized state captured as agent-owned experience; no outcome or lesson inferred.",
            1m,
            ExtractionBasis.RuntimeObservation,
            [
                new KnowledgeEvidence(1, "authenticated situation report", evidenceText)
                {
                    Details = new KnowledgeEvidenceDetails
                    {
                        AgentExperience = experience
                    }
                }
            ]);
        var contentHash = Convert.ToHexString(
            SHA256.HashData(JsonSerializer.SerializeToUtf8Bytes(experience)));
        var document = new KnowledgeDocument(
            documentId,
            "agent-experience-situations",
            $"sessions/{sessionKey}/situations/{Hash(report.CapturedAt.ToString("O"))}.json",
            $"{Hash(report.CapturedAt.ToString("O"))}.json",
            contentHash,
            "json",
            report.CapturedAt,
            [node],
            [])
        {
            OwnerAgentId = agentId
        };

        await runtimeKnowledgeStore.ReplaceAgentDocumentAsync(
            agentId,
            document,
            cancellationToken);
    }

    private static bool IsRecordableSituation(GameSituationReport report) =>
        report.Presence == GamePresenceState.Connected &&
        report.Authentication == GameAuthenticationState.Authenticated &&
        report.Synchronization == GameSynchronizationState.Healthy &&
        report.CapturedAt != default &&
        !string.IsNullOrWhiteSpace(report.CurrentPrompt) &&
        report.Snapshot is { Authenticated: true } snapshot &&
        GameSnapshotValidation.IsValid(snapshot) &&
        report.UnknownFields is not null &&
        report.UnknownFields.Count <= 100 &&
        report.UnknownFields.All(field => !string.IsNullOrWhiteSpace(field) && field.Length <= 200) &&
        report.UnknownFields.Distinct(StringComparer.Ordinal).Count() == report.UnknownFields.Count;

    private async Task<int> LoadLastRecordedTurnAsync(
        string agentId,
        string sessionId,
        string sessionKey,
        CancellationToken cancellationToken)
    {
        var documentPrefix = $"agent-experience-turn-{sessionKey}-turn-";
        var turnDocuments = (await runtimeKnowledgeStore.GetAgentDocumentsAsync(agentId, cancellationToken))
            .Where(document => document.RootId == "agent-experience-turns" &&
                document.Id.StartsWith(documentPrefix, StringComparison.Ordinal))
            .ToArray();
        var experiences = turnDocuments
            .SelectMany(document => document.Nodes)
            .SelectMany(node => node.Evidence)
            .Select(evidence => evidence.Details?.AgentExperience)
            .ToArray();
        if (turnDocuments.Any(document => document.Nodes.Count != 1) ||
            experiences.Length != turnDocuments.Length ||
            experiences.Any(experience =>
                experience is null ||
                experience.EventKind != "TurnObservation" ||
                experience.AgentId != agentId ||
                experience.SessionId != sessionId ||
                experience.TurnNumber is null))
        {
            throw new InvalidDataException(
                $"Turn-observation documents for '{sessionId}' contain invalid records.");
        }

        var recordedTurns = experiences
            .Select(experience => experience!.TurnNumber!.Value)
            .Order()
            .ToArray();
        if (!recordedTurns.SequenceEqual(Enumerable.Range(1, recordedTurns.Length)))
        {
            throw new InvalidDataException(
                $"Turn-observation documents for '{sessionId}' contain a gap or duplicate.");
        }

        return recordedTurns.Length;
    }

    private static bool IsValidSnapshotPair(
        MacroExecutionRecord record,
        GameSnapshot? beforeSnapshot,
        GameSnapshot? afterSnapshot) =>
        (beforeSnapshot is null ||
         GameSnapshotValidation.IsValid(beforeSnapshot) &&
         beforeSnapshot.ObservedAt <= record.StartTime) &&
        (afterSnapshot is null ||
         GameSnapshotValidation.IsValid(afterSnapshot) &&
         afterSnapshot.ObservedAt >= record.EndTime) &&
        (beforeSnapshot is null ||
         afterSnapshot is null ||
         beforeSnapshot.ObservedAt <= afterSnapshot.ObservedAt);

    private static string Hash(string value) =>
        Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(value))).ToLowerInvariant();
}
