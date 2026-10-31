using LLMSurvivingProxyAgent.Agent;
using LLMSurvivingProxyAgent.Core;
using LLMSurvivingProxyAgent.Goals;
using LLMSurvivingProxyAgent.Knowledge;
using Microsoft.Data.Sqlite;

namespace LLMSurvivingProxyAgent.TradeWars.Tests;

public sealed class GoalProposalTests
{
    [Fact]
    public async Task ProposalRemainsPendingUntilExplicitSelectionAndCompletionDoesNotAutoAdvance()
    {
        var temporaryDirectory = Path.Combine(Path.GetTempPath(), $"twbot-goals-{Guid.NewGuid():N}");
        Directory.CreateDirectory(temporaryDirectory);
        var connectionString = new SqliteConnectionStringBuilder
        {
            DataSource = Path.Combine(temporaryDirectory, "agent.db"),
            ForeignKeys = true,
            Pooling = false
        }.ToString();

        try
        {
            var agentStore = new SqliteAgentStore(connectionString);
            await agentStore.InitializeAsync();
            await agentStore.SaveAgentAsync(new AgentContainer
            {
                AgentId = "test-agent",
                DisplayName = "Test Agent"
            });

            var store = new SqliteGoalProposalStore(connectionString);
            await store.InitializeAsync();
            var service = new GoalProposalService(store);
            var proposal = await service.ProposeAsync(CreateDraft());

            Assert.Equal(GoalProposalStatus.PendingSelection, proposal.Status);
            var pending = Assert.Single(await store.GetProposalsAsync(
                "test-agent",
                GoalProposalStatus.PendingSelection));
            Assert.Single(pending.SupportingEvidence);
            Assert.Equal(50_000L, pending.StateSnapshot.SynchronizedCredits);

            Assert.Empty(await store.GetObjectivesAsync("test-agent", ObjectiveStatus.Active));
            var objective = await service.SelectProposalAsync(
                "test-agent",
                proposal.ProposalId,
                "The strategic reviewer selected this objective based on the current snapshot.");
            Assert.Equal(proposal.ProposalId, objective.SourceProposalId);
            Assert.Equal(proposal.Goal, objective.Goal);

            var nextCandidate = await service.ProposeAsync(CreateDraft() with
            {
                Goal = "Improve exploration readiness",
                Confidence = 0.86m
            });
            Assert.Equal(GoalProposalStatus.PendingSelection, nextCandidate.Status);
            Assert.Single(await store.GetObjectivesAsync("test-agent", ObjectiveStatus.Active));

            var completed = await service.CompleteObjectiveAsync("test-agent", objective.ObjectiveId);
            Assert.Equal(ObjectiveStatus.Completed, completed.Status);
            Assert.Empty(await store.GetObjectivesAsync("test-agent", ObjectiveStatus.Active));
            Assert.Contains(
                await store.GetProposalsAsync("test-agent", GoalProposalStatus.PendingSelection),
                proposal => proposal.ProposalId == nextCandidate.ProposalId);
            Assert.Contains(
                await store.GetObjectivesAsync("test-agent", ObjectiveStatus.Completed),
                completed => completed.ObjectiveId == objective.ObjectiveId);
        }
        finally
        {
            Directory.Delete(temporaryDirectory, recursive: true);
        }
    }

    [Fact]
    public async Task ModelCanSelectCandidateDespiteConfidenceAndReplaceTheActiveObjective()
    {
        var temporaryDirectory = Path.Combine(Path.GetTempPath(), $"twbot-goals-{Guid.NewGuid():N}");
        Directory.CreateDirectory(temporaryDirectory);
        var connectionString = new SqliteConnectionStringBuilder
        {
            DataSource = Path.Combine(temporaryDirectory, "agent.db"),
            ForeignKeys = true,
            Pooling = false
        }.ToString();

        try
        {
            var agentStore = new SqliteAgentStore(connectionString);
            await agentStore.InitializeAsync();
            await agentStore.SaveAgentAsync(new AgentContainer
            {
                AgentId = "test-agent",
                DisplayName = "Test Agent"
            });
            var store = new SqliteGoalProposalStore(connectionString);
            await store.InitializeAsync();
            var now = DateTimeOffset.UtcNow;
            var singleSource = CreateStoredProposal(
                "single-source",
                [CreateEvidence("node-one", "script-one")],
                now.AddMinutes(-1));
            var independentSources = CreateStoredProposal(
                "independent-sources",
                [
                    CreateEvidence("node-two", "script-two"),
                    CreateEvidence("node-three", "guide-three")
                ],
                now);
            await store.SaveProposalAsync(singleSource);
            await store.SaveProposalAsync(independentSources);

            var initialObjective = await store.SelectProposalAsync(
                "test-agent",
                "single-source",
                "Selected because the current state supports this candidate.");
            var selected = await store.SelectProposalAsync(
                "test-agent",
                "independent-sources",
                "The reviewed state makes this candidate the better current objective.");

            Assert.NotNull(selected);
            Assert.Equal("independent-sources", selected.SourceProposalId);
            Assert.Equal(
                initialObjective.ObjectiveId,
                Assert.Single(await store.GetObjectivesAsync("test-agent", ObjectiveStatus.Superseded)).ObjectiveId);
            Assert.Equal(
                "independent-sources",
                Assert.Single(await store.GetObjectivesAsync("test-agent", ObjectiveStatus.Active)).SourceProposalId);
        }
        finally
        {
            Directory.Delete(temporaryDirectory, recursive: true);
        }
    }

    [Fact]
    public async Task ProposalWaitsForReasoningInsteadOfConfidencePolicy()
    {
        var temporaryDirectory = Path.Combine(Path.GetTempPath(), $"twbot-goals-{Guid.NewGuid():N}");
        Directory.CreateDirectory(temporaryDirectory);
        var connectionString = new SqliteConnectionStringBuilder
        {
            DataSource = Path.Combine(temporaryDirectory, "agent.db"),
            ForeignKeys = true,
            Pooling = false
        }.ToString();

        try
        {
            var agentStore = new SqliteAgentStore(connectionString);
            await agentStore.InitializeAsync();
            await agentStore.SaveAgentAsync(new AgentContainer
            {
                AgentId = "test-agent",
                DisplayName = "Test Agent"
            });
            var store = new SqliteGoalProposalStore(connectionString);
            await store.InitializeAsync();
            var service = new GoalProposalService(store);
            var proposal = await service.ProposeAsync(CreateDraft() with { Confidence = 0.74m });

            Assert.Equal(GoalProposalStatus.PendingSelection, proposal.Status);
            Assert.Single(await store.GetProposalsAsync("test-agent", GoalProposalStatus.PendingSelection));
            Assert.Empty(await store.GetObjectivesAsync("test-agent"));
        }
        finally
        {
            Directory.Delete(temporaryDirectory, recursive: true);
        }
    }

    [Fact]
    public async Task ProposalAllowsStateOnlyEvidenceAndRejectsFutureSnapshots()
    {
        var store = new RecordingGoalProposalStore();
        var service = new GoalProposalService(store);

        await service.ProposeAsync(CreateDraft() with { SupportingEvidence = [] });
        await Assert.ThrowsAsync<ArgumentException>(() => service.ProposeAsync(
            CreateDraft() with
            {
                StateSnapshot = new GameSnapshot(null, null, null, null, DateTimeOffset.UtcNow.AddHours(1))
            }));
        Assert.Equal(1, store.SaveCount);
    }

    private static GoalProposalDraft CreateDraft()
    {
        var node = new KnowledgeNode(
            "knowledge-node",
            "knowledge-document",
            KnowledgeCategory.TacticalGuidance,
            "Possible trading guidance",
            "Comment-derived recommendation; review before relying on it.",
            0.6m,
            ExtractionBasis.ExplicitText,
            [new KnowledgeEvidence(12, "comment", "Trade when conditions are favorable.")]);
        return new GoalProposalDraft(
            "test-agent",
            "Build a defensive fighter reserve",
            "Current credits and ship strength may support improving defensive readiness.",
            0.74m,
            ["Credits are synchronized.", "The current ship can safely carry the additional fighters."],
            new GameSnapshot(12, 50_000, 100, null, DateTimeOffset.UtcNow),
            [new GoalEvidence(node, [])]);
    }

    private static GoalProposal CreateStoredProposal(
        string proposalId,
        IReadOnlyList<GoalEvidence> evidence,
        DateTimeOffset proposedAt) =>
        new(
            proposalId,
            "test-agent",
            proposalId,
            "Rationale",
            0.8m,
            [],
            new GameSnapshot(12, 50_000, 100, null, proposedAt),
            evidence,
            GoalProposalStatus.PendingSelection,
            proposedAt,
            null,
            null);

    private static GoalEvidence CreateEvidence(string nodeId, string documentId) =>
        new(
            new KnowledgeNode(
                nodeId,
                documentId,
                KnowledgeCategory.TacticalGuidance,
                nodeId,
                "Evidence",
                0.8m,
                ExtractionBasis.ExplicitText,
                [new KnowledgeEvidence(1, "comment", "Evidence")]),
            []);

    private sealed class RecordingGoalProposalStore : IGoalProposalStore
    {
        public int SaveCount { get; private set; }

        public Task InitializeAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;

        public Task SaveProposalAsync(GoalProposal proposal, CancellationToken cancellationToken = default)
        {
            SaveCount++;
            return Task.CompletedTask;
        }

        public Task<IReadOnlyList<GoalProposal>> GetProposalsAsync(
            string? agentId = null,
            GoalProposalStatus? status = null,
            int limit = 100,
            CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyList<GoalProposal>>([]);

        public Task<AgentObjective> SelectProposalAsync(
            string agentId,
            string proposalId,
            string selectionReason,
            CancellationToken cancellationToken = default) =>
            Task.FromException<AgentObjective>(new NotImplementedException());

        public Task<AgentObjective> CompleteObjectiveAsync(
            string agentId,
            string objectiveId,
            CancellationToken cancellationToken = default) =>
            Task.FromException<AgentObjective>(new NotImplementedException());

        public Task<IReadOnlyList<AgentObjective>> GetObjectivesAsync(
            string? agentId = null,
            ObjectiveStatus? status = null,
            int limit = 100,
            CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyList<AgentObjective>>([]);
    }
}
