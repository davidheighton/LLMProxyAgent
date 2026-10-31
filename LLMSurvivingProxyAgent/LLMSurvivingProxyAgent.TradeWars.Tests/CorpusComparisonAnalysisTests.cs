using LLMSurvivingProxyAgent.CorpusBuilder;
using LLMSurvivingProxyAgent.Knowledge;
using LLMSurvivingProxyAgent.Packaging;

namespace LLMSurvivingProxyAgent.TradeWars.Tests;

public sealed class CorpusComparisonAnalysisTests
{
    [Fact]
    public void ComparesObservedTriggerAndMacroContextAcrossIndependentFamilies()
    {
        var sources = new[]
        {
            Summary("twx", CorpusSourceKind.TwxScripts),
            Summary("unencrypted", CorpusSourceKind.TwxScripts),
            Summary("pink", CorpusSourceKind.PinkScripts),
            Summary("mombot", CorpusSourceKind.MomBot),
            Summary("zed", CorpusSourceKind.Zed),
            Summary("node", CorpusSourceKind.NodeBot),
            Summary("guides", CorpusSourceKind.Documentation),
            Summary("logs", CorpusSourceKind.SavedGameLogs)
        };
        var documents = new[]
        {
            Document(
                "doc-twx",
                "twx",
                "SST.ts",
                Trigger(
                    "trigger-twx",
                    "setTextLineTrigger bust :handleBust \"You are busted\"",
                    extraEvidence:
                    [
                        new KnowledgeEvidence(2, "handler response candidate", "echo \"Alert the operator\""),
                        new KnowledgeEvidence(3, "possible failure or recovery handling", "goto :retryTrade"),
                        new KnowledgeEvidence(4, "explicit urgency annotation", "# urgent response")
                    ]),
                Script("script-twx", "Purpose: SST supports port trading for credits.",
                    "Goal: Preserve credits through port trading.",
                    "Preconditions: A verified route and available port are required.",
                    "Validation: Verify the route before trading.",
                    "Retry pattern: Retry after the port reports no stock.",
                    "Resume pattern: Resume from the saved sector checkpoint.",
                    "Abort condition: Abort when credits fall below reserve.",
                    "Escalation condition: Alert the operator on a hostile signal.",
                    "Recovery checkpoint: Save the current sector before trading.",
                    "Verification loop: Re-check inventory after trading.",
                    "Stopping condition: Stop after the reserve is restored.",
                    "Failure condition: abort and retry when the port is unavailable.")),
            Document(
                "doc-port-code",
                "twx",
                "port.ts",
                NamedScript("script-port", "doc-port-code", "Port")),
            Document(
                "doc-unencrypted",
                "unencrypted",
                "Bust.ts",
                Trigger("trigger-unencrypted", "setTextLineTrigger bustAlert :onBust \"Busted\"")),
            Document(
                "doc-pink",
                "pink",
                "SST.cts",
                Trigger("trigger-pink", "setEventTrigger busted :recover \"Trade busted\""),
                Trigger(
                    "trigger-pink-opportunity",
                    "setTextLineTrigger scan :search \"profitable port pair opportunity\"",
                    "doc-pink"),
                Response("response-pink", "doc-pink", "notify the operator"),
                Script("script-pink", "Purpose: SST trades between ports for profit.",
                    "Failure condition: recover by retrying another port.")),
            Document(
                "doc-mombot",
                "mombot",
                "SST.cts",
                Response("response-mombot", "doc-mombot", "notify the operator"),
                Script("script-mombot", "Purpose: Use SST to earn trading credits.")),
            Document(
                "doc-zed",
                "zed",
                "misc.cts",
                Response("response-zed", "doc-zed", "notify the operator"),
                Pattern("validation-zed", "Result verification")),
            Document("doc-node", "node", "bot.js"),
            GuideDocument(
                "doc-guide",
                "guides",
                "sst-guide.md",
                Decision("decision-guide", "doc-guide", "usage conditions",
                    "Use SST when credits are low and turns are available.")),
            GuideDocument(
                "doc-pink-guide",
                "pink",
                "trade-notes.txt",
                Decision("decision-pink-guide", "doc-pink-guide", "usage conditions",
                    "Use SST when a safe route exists."),
                Decision("decision-pink-risk", "doc-pink-guide", "risks",
                    "Avoid SST in hostile space due to competition.")),
            GuideDocument(
                "doc-unrelated-guide",
                "guides",
                "unrelated.md",
                Decision("decision-unrelated", "doc-unrelated-guide", "usage conditions",
                    "SST2 may be used when credits are low.")),
            GuideDocument(
                "doc-sst-help",
                "mombot",
                "SST.txt",
                Decision("decision-sst-help", "doc-sst-help", "prerequisites",
                    "A verified route is required.")),
            GuideDocument(
                "doc-port-guide",
                "guides",
                "port-guide.md",
                Decision("decision-port-guide", "doc-port-guide", "usage conditions",
                    "Use port when stock is available.")),
            Document(
                "doc-logs",
                "logs",
                "saved-game-log.log",
                new KnowledgeNode(
                    "log-menu",
                    "doc-logs",
                    KnowledgeCategory.MenuStructure,
                    "Menu: Main Command",
                    "Observed menu options.",
                    0.9m,
                    ExtractionBasis.StaticStructure,
                    []))
        };

        var report = new CorpusComparisonAnalysisService().Analyze(documents, sources);

        var bust = Assert.Single(report.TriggerComparisons, item => item.EventType == "Bust detection");
        Assert.Equal(3, bust.RegistrationCount);
        Assert.Equal(3, bust.SourceDocumentCount);
        Assert.Equal(2, bust.SourceFamilyCount);
        Assert.Equal(["Pink", "TWX"], bust.Families.Select(family => family.Family).ToArray());
        Assert.Contains("MOMBot", bust.NotObservedInScannedFamilies);
        Assert.Contains("Zed", bust.NotObservedInScannedFamilies);
        Assert.DoesNotContain("SavedGameLogs", report.SourceFamiliesScanned);
        Assert.Equal(["Documentation", "MOMBot", "Pink"], report.DocumentationFamiliesScanned);
        var opportunity = Assert.Single(
            report.TriggerComparisons,
            item => item.EventType == "Opportunity scan");
        Assert.Equal(["Pink"], opportunity.Families.Select(family => family.Family).ToArray());
        Assert.Equal("Low", opportunity.ConsensusStrength);

        var economicPurpose = Assert.Single(
            report.MacroContextComparisons,
            item => item.MacroName.Equals("SST", StringComparison.OrdinalIgnoreCase) &&
                item.ContextSignal == "Purpose" &&
                item.Topic == "Economic");
        Assert.Equal(3, economicPurpose.SourceFamilyCount);
        Assert.Equal("Moderate", economicPurpose.ConsensusStrength);
        Assert.Equal(["MOMBot", "Pink", "TWX"],
            economicPurpose.Families.Select(family => family.Family).ToArray());
        Assert.Contains(report.MacroContextComparisons, item =>
            item.ContextSignal == "Preconditions" &&
            item.Topic == "Exploration" &&
            item.Families.Single().Family == "TWX" &&
            item.ConsensusStrength == "Low");

        var recovery = Assert.Single(
            report.MacroContextComparisons,
            item => item.MacroName.Equals("SST", StringComparison.OrdinalIgnoreCase) &&
                item.ContextSignal == "Failure conditions and risks" &&
                item.Topic == "Recovery");
        Assert.Equal(2, recovery.SourceFamilyCount);
        Assert.Contains(report.Limitations, limitation => limitation.Contains(
            "not verified doctrine",
            StringComparison.OrdinalIgnoreCase));
        Assert.Contains(report.Limitations, limitation => limitation.Contains(
            "source filename",
            StringComparison.OrdinalIgnoreCase));
        Assert.Contains(report.Limitations, limitation => limitation.Contains(
            "escalation rules",
            StringComparison.OrdinalIgnoreCase));
        Assert.Contains(report.Limitations, limitation => limitation.Contains(
            "source-family coverage bucket",
            StringComparison.OrdinalIgnoreCase));

        var handlerResponse = Assert.Single(
            report.PatternComparisons,
            item => item.PatternType == "Trigger handler candidate" &&
                item.Pattern == "Notify or report");
        Assert.Equal("High", handlerResponse.ConsensusStrength);
        Assert.Contains("not validated doctrine", handlerResponse.Interpretation, StringComparison.OrdinalIgnoreCase);
        Assert.Equal(["MOMBot", "Pink", "TWX", "Zed"],
            handlerResponse.Families.Select(family => family.Family).ToArray());
        Assert.Contains("NodeBot", handlerResponse.NotObservedInScannedFamilies);
        var recoveryPattern = Assert.Single(
            report.PatternComparisons,
            item => item.PatternType == "Possible recovery handling" &&
                item.Pattern == "Retry");
        Assert.Equal(["TWX"], recoveryPattern.Families.Select(family => family.Family).ToArray());
        var urgency = Assert.Single(
            report.PatternComparisons,
            item => item.PatternType == "Explicit urgency annotation" &&
                item.Pattern == "Urgent");
        Assert.Contains("Zed", urgency.NotObservedInScannedFamilies);
        Assert.Contains(report.PatternComparisons, item =>
            item.PatternType == "Explicit stopping condition" &&
            item.Pattern == "Labeled stopping-condition context" &&
            item.Families.Single().Family == "TWX");
        Assert.Contains(report.PatternComparisons, item =>
            item.PatternType == "Construction pattern" &&
            item.Pattern == "Result verification" &&
            item.Families.Single().Family == "Zed");
        var documentedUsage = Assert.Single(
            report.DecisionContextComparisons,
            item => item.Signal == "usage conditions" && item.Topic == "Economic");
        Assert.Equal(2, documentedUsage.SourceFamilyCount);
        Assert.Equal("Moderate", documentedUsage.ConsensusStrength);
        var documentedRisk = Assert.Single(
            report.DecisionContextComparisons,
            item => item.Signal == "risks" && item.Topic == "Combat");
        Assert.Contains("Documentation", documentedRisk.NotObservedInScannedFamilies);
        var sstNetwork = Assert.Single(report.ClaimNetworks, network => network.MacroName == "SST");
        Assert.Equal(3, sstNetwork.CodeArtifactCount);
        Assert.Equal(3, sstNetwork.CodeFamilies.Count);
        Assert.Contains(sstNetwork.CodeArtifacts, artifact =>
            artifact.NodeId == "script-twx" &&
            artifact.RootId == "twx" &&
            artifact.SourcePath == "SST.ts");
        Assert.Equal(4, sstNetwork.ContextEvidenceCount);
        Assert.Equal(3, sstNetwork.ContextDocumentCount);
        Assert.Equal(3, sstNetwork.ContextFamilyCount);
        Assert.Contains(sstNetwork.ContextClaims, claim =>
            claim.SourcePath == "trade-notes.txt" &&
            claim.Signal == "risks" &&
            claim.LineNumber == 1 &&
            claim.DecisionContextNodeId == "decision-pink-risk" &&
            claim.MatchedTerm == "SST");
        Assert.Contains(sstNetwork.ContextClaims, claim =>
            claim.SourcePath == "SST.txt" &&
            claim.Signal == "prerequisites" &&
            claim.MatchBasis == "Document filename");
        Assert.DoesNotContain(sstNetwork.ContextClaims, claim =>
            claim.SourcePath == "unrelated.md");
        Assert.Contains("co-reference candidates", sstNetwork.Interpretation);
        Assert.DoesNotContain(report.ClaimNetworks, network =>
            network.MacroName.Equals("Port", StringComparison.OrdinalIgnoreCase));
        Assert.Contains(report.Limitations, limitation =>
            limitation.Contains("exact name match", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void IncludesCooperativeDoctrineAsObservedPatterns()
    {
        var source = Summary("pink", CorpusSourceKind.PinkScripts);
        var document = Document(
            "doc-pink-cooperation",
            "pink",
            "Furb.txt",
            new KnowledgeNode(
                "cooperative-furb",
                "doc-pink-cooperation",
                KnowledgeCategory.CooperativeProcedure,
                "Cooperative procedure: FURB provisioning",
                "The source describes buying and delivering a ship to a corp member.",
                0.72m,
                ExtractionBasis.ExplicitText,
                [new KnowledgeEvidence(1, "explicit FURB description", "Buys and delivers a Ship to a Corpy.")]));

        var report = new CorpusComparisonAnalysisService().Analyze([document], [source]);

        var observed = Assert.Single(report.PatternComparisons, item =>
            item.PatternType == "Observed CooperativeProcedure" &&
            item.Pattern == "Cooperative procedure: FURB provisioning");
        Assert.Equal(1, observed.SourceDocumentCount);
        Assert.Equal(["Pink"], observed.Families.Select(family => family.Family).ToArray());
    }

    private static PackageSourceSummary Summary(string rootId, CorpusSourceKind kind) =>
        new(
            rootId,
            kind.ToString(),
            1,
            1,
            0,
            0,
            0,
            0,
            1,
            1,
            1,
            new Dictionary<string, int>(),
            new Dictionary<string, int>(),
            false);

    private static KnowledgeDocument Document(
        string id,
        string rootId,
        string fileName,
        params KnowledgeNode[] nodes) =>
        new(
            id,
            rootId,
            fileName,
            fileName,
            $"hash-{id}",
            "script",
            DateTimeOffset.UtcNow,
            nodes,
            []);

    private static KnowledgeNode Trigger(
        string id,
        string registration,
        string? documentId = null,
        params KnowledgeEvidence[] extraEvidence) =>
        new(
            id,
            documentId ?? id.Replace("trigger-", "doc-", StringComparison.Ordinal),
            KnowledgeCategory.ReactiveTriggerPattern,
            "Trigger: Text line",
            "Observed trigger registration.",
            0.66m,
            ExtractionBasis.StaticStructure,
            [new KnowledgeEvidence(1, "trigger registration", registration), .. extraEvidence]);

    private static KnowledgeNode Pattern(string id, string title) =>
        new(
            id,
            "doc-zed",
            KnowledgeCategory.MacroConstructionPattern,
            title,
            "Observed construction pattern.",
            0.5m,
            ExtractionBasis.StaticStructure,
            [new KnowledgeEvidence(1, "pattern", title)]);

    private static KnowledgeNode Response(string id, string documentId, string text) =>
        new(
            id,
            documentId,
            KnowledgeCategory.ReactiveTriggerPattern,
            "Trigger: Static response evidence",
            "Observed handler response candidate.",
            0.5m,
            ExtractionBasis.StaticStructure,
            [new KnowledgeEvidence(1, "handler response candidate", text)]);

    private static KnowledgeNode Decision(
        string id,
        string documentId,
        string signal,
        string text) =>
        new(
            id,
            documentId,
            KnowledgeCategory.DecisionContext,
            $"Decision context: {signal}",
            "Documentation context evidence.",
            0.52m,
            ExtractionBasis.ExplicitText,
            [new KnowledgeEvidence(1, signal, text)]);

    private static KnowledgeDocument GuideDocument(
        string id,
        string rootId,
        string fileName,
        params KnowledgeNode[] nodes) =>
        new(
            id,
            rootId,
            fileName,
            fileName,
            $"hash-{id}",
            "Markdown",
            DateTimeOffset.UtcNow,
            nodes,
            []);

    private static KnowledgeNode Script(string id, params string[] evidence) =>
        new(
            id,
            id.Replace("script-", "doc-", StringComparison.Ordinal),
            KnowledgeCategory.MacroDefinition,
            "Script: SST",
            "Code artifact.",
            0.4m,
            ExtractionBasis.FilenameHeuristic,
            evidence.Select((text, index) => new KnowledgeEvidence(
                    index + 1,
                    GetContextSignal(text),
                    text))
                .ToArray());

    private static KnowledgeNode NamedScript(string id, string documentId, string macroName) =>
        new(
            id,
            documentId,
            KnowledgeCategory.MacroDefinition,
            $"Script: {macroName}",
            "Code artifact.",
            0.4m,
            ExtractionBasis.FilenameHeuristic,
            []);

    private static string GetContextSignal(string text) =>
        text[..text.IndexOf(':')].Trim() switch
        {
            "Purpose" => "purpose",
            "Goal" => "observed goals",
            "Preconditions" => "preconditions",
            "Validation" => "validation checks",
            "Verification loop" => "verification loops",
            "Retry pattern" => "retry patterns",
            "Resume pattern" => "resume patterns",
            "Abort condition" => "abort conditions",
            "Escalation condition" => "escalation conditions",
            "Recovery checkpoint" => "recovery checkpoints",
            "Stopping condition" => "stopping conditions",
            _ => "failure conditions and risks"
        };
}
