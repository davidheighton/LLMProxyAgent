using LLMSurvivingProxyAgent.Core;
using LLMSurvivingProxyAgent.Knowledge;
using LLMSurvivingProxyAgent.Runtime;
using Microsoft.Data.Sqlite;

namespace LLMSurvivingProxyAgent.TradeWars.Tests;

public sealed class KnowledgeBackedAutonomousActionKnowledgeProviderTests
{
    [Fact]
    public async Task RetrievesRelevantExplorationAndTradingKnowledgeForTheAgent()
    {
        var temporaryDirectory = Path.Combine(Path.GetTempPath(), $"twbot-action-knowledge-{Guid.NewGuid():N}");
        Directory.CreateDirectory(temporaryDirectory);
        try
        {
            var connectionString = new SqliteConnectionStringBuilder
            {
                DataSource = Path.Combine(temporaryDirectory, "knowledge.db"),
                ForeignKeys = true,
                Pooling = false
            }.ToString();
            var knowledgeStore = new SqliteKnowledgeStore(connectionString);
            await knowledgeStore.InitializeAsync();

            var evidence = new KnowledgeEvidence(17, "script sequence", "Scan adjacent sectors before selecting a route.");
            var document = new KnowledgeDocument(
                "scan-and-trade",
                "test-seed",
                "scripts/scan-and-trade.ts",
                "scan-and-trade.ts",
                "scan-and-trade-hash",
                "text",
                DateTimeOffset.UtcNow,
                [
                    new KnowledgeNode(
                        "scanner-guidance",
                        "scan-and-trade",
                        KnowledgeCategory.TacticalGuidance,
                        "Scanner exploration",
                        "Use scanner data to assess sectors before navigation.",
                        0.95m,
                        ExtractionBasis.ExplicitText,
                        [evidence]),
                    new KnowledgeNode(
                        "port-trading-guidance",
                        "scan-and-trade",
                        KnowledgeCategory.GameplayMechanic,
                        "Port trading",
                        "Check port trading, available holds, fuel, turns, and credits before buying.",
                        0.9m,
                        ExtractionBasis.ExplicitText,
                        [evidence]),
                    new KnowledgeNode(
                        "port-trade-procedure",
                        "scan-and-trade",
                        KnowledgeCategory.MacroConstructionPattern,
                        "Port trade procedure",
                        "For a known compatible port pair, buy only what the buyer accepts, route one hop at a time, sell the cargo, and verify credits.",
                        0.9m,
                        ExtractionBasis.ExplicitText,
                        [evidence]),
                    new KnowledgeNode(
                        "prompt-recovery-procedure",
                        "scan-and-trade",
                        KnowledgeCategory.MacroConstructionPattern,
                        "Prompt recovery procedure",
                        "When a script is blocked at an unfamiliar prompt, inspect the visible prompt and gather evidence before choosing a supported answer.",
                        0.9m,
                        ExtractionBasis.ExplicitText,
                        [evidence]),
                    new KnowledgeNode(
                        "script-menu-guidance",
                        "scan-and-trade",
                        KnowledgeCategory.TacticalGuidance,
                        "Script menu guidance",
                        "Use source-linked TWX script and menu data only as a routine description; verify the active prompt before selecting the next action.",
                        0.9m,
                        ExtractionBasis.ExplicitText,
                        [evidence])
                ],
                []);
            await knowledgeStore.ImportDocumentsAsync([document]);

            var provider = new KnowledgeBackedAutonomousActionKnowledgeProvider(knowledgeStore);
            var result = await provider.GetRelevantKnowledgeAsync("meridian");

            Assert.Contains(result, item => item.NodeId == "scanner-guidance" && item.Layer == "Tactical");
            Assert.Contains(result, item => item.NodeId == "port-trading-guidance" && item.Layer == "Facts");
            Assert.Contains(result, item => item.NodeId == "port-trade-procedure" && item.Layer == "Procedural");
            Assert.Contains(result, item => item.NodeId == "prompt-recovery-procedure" && item.Layer == "Procedural");
            Assert.Contains(result, item => item.NodeId == "script-menu-guidance" && item.Layer == "Tactical");
            Assert.Contains(
                Assert.Single(result, item => item.NodeId == "scanner-guidance").Evidence,
                item => item.Contains("line 17", StringComparison.Ordinal));
        }
        finally
        {
            Directory.Delete(temporaryDirectory, recursive: true);
        }
    }
}
