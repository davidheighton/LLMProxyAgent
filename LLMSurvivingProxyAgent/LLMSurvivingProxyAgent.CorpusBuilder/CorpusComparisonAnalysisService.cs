using System.Text.RegularExpressions;
using LLMSurvivingProxyAgent.Knowledge;
using LLMSurvivingProxyAgent.Packaging;

namespace LLMSurvivingProxyAgent.CorpusBuilder;

public sealed class CorpusComparisonAnalysisService
{
    private static readonly (string Name, Regex Pattern)[] TriggerEventPatterns =
    [
        ("Bust detection", Pattern(@"\b(bust(?:ed)?|steal(?:ing)?|robbery|robbed|robbery)\b")),
        ("Opportunity scan", Pattern(@"\b(opportunit(?:y|ies)|profitable|pair[- ]?trade|trade opportunity)\b")),
        ("Hostile detection", Pattern(@"\b(hostile|enemy|attack(?:ed)?|photon|threat)\b")),
        ("Connection loss", Pattern(@"\b(disconnect(?:ed)?|connection lost|lost connection|offline)\b")),
        ("Trader observation", Pattern(@"\b(trader|player seen|ship seen|ship spotted|trader spotted)\b")),
        ("Communication", Pattern(@"\b(subspace|radio|message|broadcast|hailing|corporate menu)\b")),
        ("Port or trade activity", Pattern(@"\b(port|stock|commodity|trading|trade)\b")),
        ("Navigation activity", Pattern(@"\b(sector|warp|route|course|navpoint)\b")),
        ("Planetary activity", Pattern(@"\b(planet|colonist|citadel|genesis)\b"))
    ];

    private static readonly (string Name, Regex Pattern)[] MacroTopics =
    [
        ("Economic", Pattern(@"\b(trade|trading|credit|credits|profit|income|bankroll|port|merchant|sst|ssm)\b")),
        ("Combat", Pattern(@"\b(fighter|attack|enemy|combat|photon|torpedo|hostile)\b")),
        ("Exploration", Pattern(@"\b(explore|exploration|scout|map|sector|warp|route|navigation|navpoint|unexplored)\b")),
        ("Intelligence", Pattern(@"\b(scan|intel|cim|density|report|search|status|probe)\b")),
        ("Planetary", Pattern(@"\b(planet|colonist|citadel|terra|genesis|coloniz\w*)\b")),
        ("Recovery", Pattern(@"\b(recover\w*|retry\w*|restore\w*|fallback\w*|abort\w*|resume\w*)\b"))
    ];

    private static readonly Dictionary<string, string> ContextSignals = new(StringComparer.Ordinal)
    {
        ["purpose"] = "Purpose",
        ["intent"] = "Intent",
        ["assumptions"] = "Assumptions",
        ["benefits"] = "Benefits",
        ["expected outcomes"] = "Expected outcomes",
        ["avoidance conditions"] = "Avoidance conditions",
        ["counterexamples"] = "Counterexamples",
        ["observed goals"] = "Observed goals",
        ["typical usage"] = "Typical usage",
        ["observed triggers"] = "Observed triggers",
        ["stopping conditions"] = "Stopping conditions",
        ["failure conditions and risks"] = "Failure conditions and risks",
        ["preconditions"] = "Preconditions",
        ["validation checks"] = "Validation checks",
        ["verification loops"] = "Verification loops",
        ["retry patterns"] = "Retry patterns",
        ["resume patterns"] = "Resume patterns",
        ["abort conditions"] = "Abort conditions",
        ["escalation conditions"] = "Escalation conditions",
        ["recovery checkpoints"] = "Recovery checkpoints",
        ["recovery patterns"] = "Recovery patterns",
        ["inputs"] = "Inputs",
        ["outputs"] = "Outputs",
        ["alternatives"] = "Alternatives"
    };

    private static readonly (string Name, Regex Pattern)[] ResponsePatterns =
    [
        ("Notify or report", Pattern(@"\b(alert|broadcast|echo|hail|message|notify|report|send)\w*\b")),
        ("Abort or stop", Pattern(@"\b(abort|cancel|kill|stop|terminate)\w*\b")),
        ("Retry", Pattern(@"\b(again|repeat|retry)\w*\b")),
        ("Restore or resume", Pattern(@"\b(reconnect|recover|restart|restore|resume)\w*\b")),
        ("Return or reroute", Pattern(@"\b(back|home|reroute|return|safe|stardock)\b")),
        ("Validate or check", Pattern(@"\b(check|confirm|scan|validate|verify)\w*\b")),
        ("Wait or delay", Pattern(@"\b(delay|pause|sleep|wait)\w*\b")),
        ("Record or persist", Pattern(@"\b(log|record|save|store|write)\w*\b"))
    ];

    private static readonly (string Name, Regex Pattern)[] RecoveryPatterns =
    [
        ("Abort or cancel", Pattern(@"\b(abort|cancel)\w*\b")),
        ("Retry", Pattern(@"\b(retry|again|repeat)\w*\b")),
        ("Restore or resume", Pattern(@"\b(restore|resume|restart|reconnect)\w*\b")),
        ("Fallback", Pattern(@"\bfallback\w*\b")),
        ("Disconnect or timeout", Pattern(@"\b(disconnect\w*|timeout\w*)\b")),
        ("Cleanup", Pattern(@"\bcleanup\w*\b")),
        ("Bust handling", Pattern(@"\bbust\w*\b")),
        ("Recover or error handling", Pattern(@"\b(recover\w*|fail(?:ure|ed)?|error\w*)\b"))
    ];

    private static readonly (string Name, Regex Pattern)[] UrgencyPatterns =
    [
        ("Emergency", Pattern(@"\bemergency\b")),
        ("Immediate", Pattern(@"\bimmediate(?:ly)?\b")),
        ("Critical", Pattern(@"\bcritical\b")),
        ("Urgent", Pattern(@"\burgent\b")),
        ("Reactive", Pattern(@"\breactive\b"))
    ];

    private static readonly Regex LeadingAcronymPattern = new(
        @"^(?<acronym>[A-Z]{3,})(?=[A-Z][a-z]|$)",
        RegexOptions.Compiled | RegexOptions.CultureInvariant);

    public CorpusComparisonReport Analyze(
        IReadOnlyList<KnowledgeDocument> documents,
        IReadOnlyList<PackageSourceSummary> sources)
    {
        ArgumentNullException.ThrowIfNull(documents);
        ArgumentNullException.ThrowIfNull(sources);

        var sourceByRoot = sources.ToDictionary(source => source.RootId, StringComparer.Ordinal);
        var sourceFamilies = documents
            .Where(document => sourceByRoot.TryGetValue(document.RootId, out var source) &&
                source.DocumentsMined > 0 &&
                IsComparableSource(source.Kind))
            .Select(document => ToFamilyName(sourceByRoot[document.RootId].Kind))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(family => family, StringComparer.OrdinalIgnoreCase)
            .ToArray();
        var documentationFamilies = documents
            .Where(document => sourceByRoot.TryGetValue(document.RootId, out var source) &&
                source.DocumentsMined > 0 &&
                IsDocumentationDocument(document, source.Kind))
            .Select(document => ToFamilyName(sourceByRoot[document.RootId].Kind))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(family => family, StringComparer.OrdinalIgnoreCase)
            .ToArray();
        var triggerComparisons = new Dictionary<string, ComparisonAccumulator>(StringComparer.Ordinal);
        var macroComparisons = new Dictionary<(string Macro, string Signal, string Topic), ComparisonAccumulator>(
            new MacroComparisonKeyComparer());
        var decisionContextComparisons = new Dictionary<(string Signal, string Topic), ComparisonAccumulator>(
            new PatternComparisonKeyComparer());
        var patternComparisons = new Dictionary<(string Type, string Pattern), ComparisonAccumulator>(
            new PatternComparisonKeyComparer());

        foreach (var document in documents)
        {
            if (!sourceByRoot.TryGetValue(document.RootId, out var source) ||
                source.DocumentsMined == 0)
            {
                continue;
            }

            var family = ToFamilyName(source.Kind);
            var sourceReference = $"{document.RootId}:{document.RelativePath}";
            if (IsDocumentationDocument(document, source.Kind))
            {
                foreach (var node in document.Nodes.Where(node =>
                    node.Category == KnowledgeCategory.DecisionContext))
                {
                    foreach (var evidence in node.Evidence)
                    {
                        var topics = ClassifyMacroTopics(evidence.Text);
                        foreach (var topic in topics.Count == 0 ? ["General"] : topics)
                        {
                            AddDecisionContextObservation(
                                decisionContextComparisons,
                                evidence.Signal,
                                topic,
                                family,
                                sourceReference);
                        }
                    }
                }
            }
            if (!IsComparableSource(source.Kind))
            {
                continue;
            }

            foreach (var node in document.Nodes)
            {
                if (node.Category == KnowledgeCategory.ReactiveTriggerPattern)
                {
                    foreach (var evidence in node.Evidence)
                    {
                        if (evidence.Signal == "trigger registration")
                        {
                            var eventType = ClassifyTrigger(evidence.Text);
                            if (eventType is not null)
                            {
                                AddObservation(triggerComparisons, eventType, family, sourceReference);
                            }
                        }
                        else if (evidence.Signal == "handler response candidate")
                        {
                            AddPatternObservation(
                                patternComparisons,
                                "Trigger handler candidate",
                                Classify(evidence.Text, ResponsePatterns),
                                family,
                                sourceReference);
                        }
                        else if (evidence.Signal == "possible failure or recovery handling")
                        {
                            AddPatternObservation(
                                patternComparisons,
                                "Possible recovery handling",
                                Classify(evidence.Text, RecoveryPatterns),
                                family,
                                sourceReference);
                        }
                        else if (evidence.Signal == "explicit urgency annotation")
                        {
                            AddPatternObservation(
                                patternComparisons,
                                "Explicit urgency annotation",
                                Classify(evidence.Text, UrgencyPatterns),
                                family,
                                sourceReference);
                        }
                    }
                }

                if (node.Category == KnowledgeCategory.MacroConstructionPattern)
                {
                    AddPatternObservation(
                        patternComparisons,
                        "Construction pattern",
                        node.Title,
                        family,
                        sourceReference);
                }

                if (node.Category is KnowledgeCategory.CommunicationPattern or
                    KnowledgeCategory.CooperativeProcedure or
                    KnowledgeCategory.CorpProtocol or
                    KnowledgeCategory.EmergencyResponse or
                    KnowledgeCategory.ResourceTransfer or
                    KnowledgeCategory.RoleDefinition or
                    KnowledgeCategory.TacticalPattern)
                {
                    AddPatternObservation(
                        patternComparisons,
                        $"Observed {node.Category}",
                        node.Title,
                        family,
                        sourceReference);
                }

                if (node.Category == KnowledgeCategory.MacroDefinition &&
                    node.Title.StartsWith("Script:", StringComparison.Ordinal))
                {
                    var macroName = Path.GetFileNameWithoutExtension(document.FileName);
                    foreach (var evidence in node.Evidence)
                    {
                        if (!ContextSignals.TryGetValue(evidence.Signal, out var signal))
                        {
                            continue;
                        }

                        if (signal == "Stopping conditions")
                        {
                            AddPatternObservation(
                                patternComparisons,
                                "Explicit stopping condition",
                                "Labeled stopping-condition context",
                                family,
                                sourceReference);
                        }
                        else if (signal == "Failure conditions and risks")
                        {
                            AddPatternObservation(
                                patternComparisons,
                                "Explicit failure or risk condition",
                                "Labeled failure-condition context",
                                family,
                                sourceReference);
                        }

                        foreach (var topic in ClassifyMacroTopics(evidence.Text))
                        {
                            var key = (macroName, signal, topic);
                            if (!macroComparisons.TryGetValue(key, out var accumulator))
                            {
                                accumulator = new ComparisonAccumulator();
                                macroComparisons.Add(key, accumulator);
                            }
                            accumulator.Add(family, sourceReference);
                        }
                    }
                }
            }
        }

        var triggerResults = triggerComparisons
            .OrderBy(pair => pair.Key, StringComparer.Ordinal)
            .Select(pair => ToTriggerComparison(pair.Key, pair.Value, sourceFamilies))
            .ToArray();
        var macroResults = macroComparisons
            .OrderBy(pair => pair.Key.Macro, StringComparer.OrdinalIgnoreCase)
            .ThenBy(pair => pair.Key.Signal, StringComparer.Ordinal)
            .ThenBy(pair => pair.Key.Topic, StringComparer.Ordinal)
            .Select(pair => ToMacroComparison(pair.Key, pair.Value, sourceFamilies))
            .ToArray();
        var patternResults = patternComparisons
            .OrderBy(pair => pair.Key.Type, StringComparer.Ordinal)
            .ThenBy(pair => pair.Key.Pattern, StringComparer.Ordinal)
            .Select(pair => ToPatternComparison(pair.Key, pair.Value, sourceFamilies))
            .ToArray();
        var decisionContextResults = decisionContextComparisons
            .OrderBy(pair => pair.Key.Signal, StringComparer.Ordinal)
            .ThenBy(pair => pair.Key.Topic, StringComparer.Ordinal)
            .Select(pair => ToDecisionContextComparison(
                pair.Key,
                pair.Value,
                documentationFamilies))
            .ToArray();
        var claimNetworks = BuildClaimNetworks(documents, sourceByRoot);

        return new CorpusComparisonReport(
            sourceFamilies,
            documentationFamilies,
            triggerResults,
            macroResults,
            decisionContextResults,
            patternResults,
            [
                "Documentation decision-context tags are heuristic source claims, not verified tactical guidance.",
                "Topic tags do not establish a relationship between a documentation passage and a specific macro or trigger.",
                "Documentation-family non-observation is scoped to mined documentation files and is not evidence of absence from the source family.",
                "Not observed in a scanned family does not mean the behavior is absent; only mined documents and recognized signals are counted.",
                "Trigger tags are first-match keyword classifications of explicit registration lines, not validated semantic event types.",
                "Handler and recovery evidence is not reliably linked to an individual registration, so no trigger-to-response causality is asserted.",
                "Handler, recovery, and urgency tags are first-match keyword classifications of mined evidence lines, not verified behavior or escalation rules.",
                "Stopping and failure patterns require explicit labels in source comments; unmatched or unlabeled conditions are not counted.",
                "Consensus strength is a source-family coverage bucket, not statistical consensus or validated doctrine.",
                "Macro topics are keyword tags over explicit context comments; cross-family repetition is an observation, not verified doctrine.",
                "Macro identity is approximated by source filename; matching or differing names do not prove that scripts implement the same macro.",
                "Claim networks connect documentation evidence to code-artifact names only when the cited passage contains an exact name match; this co-reference candidate does not prove the passage describes that implementation or validates the tactic."
            ])
        {
            ClaimNetworks = claimNetworks
        };
    }

    private static IReadOnlyList<CorpusClaimNetwork> BuildClaimNetworks(
        IReadOnlyList<KnowledgeDocument> documents,
        IReadOnlyDictionary<string, PackageSourceSummary> sourceByRoot)
    {
        var codeArtifacts = documents
            .Where(document =>
                sourceByRoot.TryGetValue(document.RootId, out var source) &&
                source.DocumentsMined > 0 &&
                IsComparableSource(source.Kind))
            .SelectMany(document =>
            {
                var family = ToFamilyName(sourceByRoot[document.RootId].Kind);
                var sourceReference = $"{document.RootId}:{document.RelativePath}";
                return document.Nodes
                    .Where(node =>
                        node.Category == KnowledgeCategory.MacroDefinition &&
                        node.Title.StartsWith("Script:", StringComparison.Ordinal))
                    .Select(node => new MacroArtifact(
                        node.Title["Script:".Length..].Trim(),
                        node.Id,
                        document.Id,
                        document.RootId,
                        document.RelativePath,
                        family,
                        sourceReference,
                        GetLeadingAcronym(document.FileName)));
            })
            .Where(artifact => artifact.MacroName.Length >= 2)
            .ToArray();
        var candidateNames = codeArtifacts
            .SelectMany(artifact => artifact.AcronymAliases)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Where(name => name.Length >= 3)
            .OrderBy(name => name, StringComparer.OrdinalIgnoreCase);
        var documentation = documents
            .Where(document =>
                sourceByRoot.TryGetValue(document.RootId, out var source) &&
                source.DocumentsMined > 0 &&
                IsDocumentationDocument(document, source.Kind))
            .ToArray();
        var networks = new List<CorpusClaimNetwork>();

        foreach (var candidateName in candidateNames)
        {
            var artifacts = codeArtifacts
                .Where(artifact =>
                    artifact.MacroName.Equals(candidateName, StringComparison.OrdinalIgnoreCase) ||
                    artifact.AcronymAliases.Contains(candidateName, StringComparer.OrdinalIgnoreCase))
                .DistinctBy(artifact => artifact.DocumentId, StringComparer.Ordinal)
                .ToArray();
            var exactMention = new Regex(
                $@"(?<![\p{{L}}\p{{N}}_]){Regex.Escape(candidateName)}(?![\p{{L}}\p{{N}}_])",
                RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
            var claims = new List<CorpusClaimObservation>();

            foreach (var document in documentation)
            {
                var family = ToFamilyName(sourceByRoot[document.RootId].Kind);
                foreach (var node in document.Nodes.Where(node =>
                    node.Category == KnowledgeCategory.DecisionContext))
                {
                    foreach (var evidence in node.Evidence)
                    {
                        var evidenceMatch = exactMention.IsMatch(evidence.Text);
                        var fileNameMatch = exactMention.IsMatch(document.FileName);
                        if (!evidenceMatch && !fileNameMatch)
                        {
                            continue;
                        }

                        claims.Add(new CorpusClaimObservation(
                            evidence.Signal,
                            evidence.Text,
                            evidence.LineNumber,
                            node.Id,
                            document.Id,
                            document.RootId,
                            document.RelativePath,
                            family,
                            DocumentationSourceClassifier.Classify(document).ToString(),
                            candidateName,
                            evidenceMatch && fileNameMatch
                                ? "Evidence text and document filename"
                                : evidenceMatch
                                    ? "Evidence text"
                                    : "Document filename"));
                    }
                }
            }

            if (claims.Count == 0)
            {
                continue;
            }

            var codeFamilies = artifacts
                .GroupBy(artifact => artifact.Family, StringComparer.OrdinalIgnoreCase)
                .OrderBy(group => group.Key, StringComparer.OrdinalIgnoreCase)
                .Select(group =>
                {
                    var familyArtifacts = group.ToArray();
                    return new CorpusFamilyObservation(
                        familyArtifacts[0].Family,
                        familyArtifacts.Length,
                        familyArtifacts.Length,
                        familyArtifacts.Select(artifact => artifact.SourceReference)
                            .Distinct(StringComparer.Ordinal)
                            .OrderBy(path => path, StringComparer.Ordinal)
                            .Take(5)
                            .ToArray());
                })
                .ToArray();
            var orderedClaims = claims
                .OrderBy(claim => claim.SourceFamily, StringComparer.OrdinalIgnoreCase)
                .ThenBy(claim => claim.SourcePath, StringComparer.Ordinal)
                .ThenBy(claim => claim.LineNumber)
                .ThenBy(claim => claim.Signal, StringComparer.Ordinal)
                .ToArray();

            networks.Add(new CorpusClaimNetwork(
                candidateName,
                artifacts.Length,
                artifacts.Select(artifact => new CorpusCodeArtifactReference(
                        artifact.NodeId,
                        artifact.DocumentId,
                        artifact.RootId,
                        artifact.SourcePath,
                        artifact.Family))
                    .OrderBy(artifact => artifact.SourceFamily, StringComparer.OrdinalIgnoreCase)
                    .ThenBy(artifact => artifact.SourcePath, StringComparer.Ordinal)
                    .ToArray(),
                codeFamilies,
                orderedClaims.Length,
                orderedClaims.Select(claim => claim.DocumentId).Distinct(StringComparer.Ordinal).Count(),
                orderedClaims.Select(claim => claim.SourceFamily)
                    .Distinct(StringComparer.OrdinalIgnoreCase)
                    .Count(),
                orderedClaims,
                "Acronym-like code filename evidence is matched exactly in cited documentation text or its filename. These are co-reference candidates; filename-only matches suggest document subject, not a verified relationship to every passage. No link establishes intent or doctrine."));
        }

        return networks
            .OrderBy(network => network.MacroName, StringComparer.OrdinalIgnoreCase)
            .ToArray();
    }

    private static IReadOnlyList<string> GetLeadingAcronym(string fileName)
    {
        var stem = Path.GetFileNameWithoutExtension(fileName);
        var match = LeadingAcronymPattern.Match(stem);
        return match.Success ? [match.Groups["acronym"].Value] : [];
    }

    private static void AddPatternObservation(
        IDictionary<(string Type, string Pattern), ComparisonAccumulator> comparisons,
        string type,
        string? pattern,
        string family,
        string sourceReference)
    {
        if (pattern is null)
        {
            return;
        }
        var key = (type, pattern);
        if (!comparisons.TryGetValue(key, out var accumulator))
        {
            accumulator = new ComparisonAccumulator();
            comparisons.Add(key, accumulator);
        }
        accumulator.Add(family, sourceReference);
    }

    private static void AddDecisionContextObservation(
        IDictionary<(string Signal, string Topic), ComparisonAccumulator> comparisons,
        string signal,
        string topic,
        string family,
        string sourceReference)
    {
        var key = (signal, topic);
        if (!comparisons.TryGetValue(key, out var accumulator))
        {
            accumulator = new ComparisonAccumulator();
            comparisons.Add(key, accumulator);
        }
        accumulator.Add(family, sourceReference);
    }

    private static CorpusDecisionContextComparison ToDecisionContextComparison(
        (string Signal, string Topic) key,
        ComparisonAccumulator accumulator,
        IReadOnlyList<string> scannedFamilies)
    {
        var families = accumulator.ToObservations();
        var observedFamilies = families.Select(family => family.Family).ToHashSet(StringComparer.OrdinalIgnoreCase);
        return new CorpusDecisionContextComparison(
            key.Signal,
            key.Topic,
            families.Sum(family => family.EvidenceCount),
            families.Sum(family => family.SourceDocumentCount),
            families.Count,
            families,
            scannedFamilies.Where(family => !observedFamilies.Contains(family)).ToArray())
        {
            ConsensusStrength = GetConsensusStrength(families.Count, scannedFamilies.Count)
        };
    }

    private static CorpusPatternComparison ToPatternComparison(
        (string Type, string Pattern) key,
        ComparisonAccumulator accumulator,
        IReadOnlyList<string> scannedFamilies)
    {
        var families = accumulator.ToObservations();
        var observedFamilies = families.Select(family => family.Family).ToHashSet(StringComparer.OrdinalIgnoreCase);
        return new CorpusPatternComparison(
            key.Type,
            key.Pattern,
            families.Sum(family => family.EvidenceCount),
            families.Sum(family => family.SourceDocumentCount),
            families.Count,
            families,
            scannedFamilies.Where(family => !observedFamilies.Contains(family)).ToArray())
        {
            ConsensusStrength = GetConsensusStrength(families.Count, scannedFamilies.Count)
        };
    }

    private static void AddObservation(
        IDictionary<string, ComparisonAccumulator> comparisons,
        string eventType,
        string family,
        string sourceReference)
    {
        if (!comparisons.TryGetValue(eventType, out var accumulator))
        {
            accumulator = new ComparisonAccumulator();
            comparisons.Add(eventType, accumulator);
        }
        accumulator.Add(family, sourceReference);
    }

    private static CorpusTriggerComparison ToTriggerComparison(
        string eventType,
        ComparisonAccumulator accumulator,
        IReadOnlyList<string> scannedFamilies)
    {
        var families = accumulator.ToObservations();
        var observedFamilies = families.Select(family => family.Family).ToHashSet(StringComparer.OrdinalIgnoreCase);
        return new CorpusTriggerComparison(
            eventType,
            families.Sum(family => family.EvidenceCount),
            families.Sum(family => family.SourceDocumentCount),
            families.Count,
            families,
            scannedFamilies.Where(family => !observedFamilies.Contains(family)).ToArray())
        {
            ConsensusStrength = GetConsensusStrength(families.Count, scannedFamilies.Count)
        };
    }

    private static CorpusMacroContextComparison ToMacroComparison(
        (string Macro, string Signal, string Topic) key,
        ComparisonAccumulator accumulator,
        IReadOnlyList<string> scannedFamilies)
    {
        var families = accumulator.ToObservations();
        var observedFamilies = families.Select(family => family.Family).ToHashSet(StringComparer.OrdinalIgnoreCase);
        return new CorpusMacroContextComparison(
            key.Macro,
            key.Signal,
            key.Topic,
            families.Sum(family => family.EvidenceCount),
            families.Sum(family => family.SourceDocumentCount),
            families.Count,
            families,
            scannedFamilies.Where(family => !observedFamilies.Contains(family)).ToArray())
        {
            ConsensusStrength = GetConsensusStrength(families.Count, scannedFamilies.Count)
        };
    }

    private static string GetConsensusStrength(int observedFamilies, int scannedFamilies)
    {
        if (observedFamilies >= 4 &&
            observedFamilies * 3 >= scannedFamilies * 2)
        {
            return "High";
        }
        if (observedFamilies >= 2 &&
            observedFamilies * 3 >= scannedFamilies)
        {
            return "Moderate";
        }
        return "Low";
    }

    private static string? ClassifyTrigger(string registrationLine)
    {
        foreach (var eventType in TriggerEventPatterns)
        {
            if (eventType.Pattern.IsMatch(registrationLine))
            {
                return eventType.Name;
            }
        }
        return null;
    }

    private static IReadOnlyList<string> ClassifyMacroTopics(string contextText) =>
        MacroTopics
            .Where(topic => topic.Pattern.IsMatch(contextText))
            .Select(topic => topic.Name)
            .ToArray();

    private static string? Classify(string text, IReadOnlyList<(string Name, Regex Pattern)> patterns) =>
        patterns.FirstOrDefault(pattern => pattern.Pattern.IsMatch(text)).Name is { Length: > 0 } name
            ? name
            : null;

    private static Regex Pattern(string expression) =>
        new(expression, RegexOptions.IgnoreCase | RegexOptions.Compiled | RegexOptions.CultureInvariant);

    private static bool IsDocumentationDocument(KnowledgeDocument document, string sourceKind) =>
        sourceKind == nameof(CorpusSourceKind.Documentation) ||
        document.Language is "Markdown" or "Text";

    private static string ToFamilyName(string kind) => kind switch
    {
        nameof(CorpusSourceKind.TwxScripts) => "TWX",
        nameof(CorpusSourceKind.PinkScripts) => "Pink",
        nameof(CorpusSourceKind.MomBot) => "MOMBot",
        nameof(CorpusSourceKind.Twx30) => "TWX30",
        nameof(CorpusSourceKind.Zed) => "Zed",
        nameof(CorpusSourceKind.NodeBot) => "NodeBot",
        nameof(CorpusSourceKind.Documentation) => "Documentation",
        nameof(CorpusSourceKind.Data) => "Data",
        nameof(CorpusSourceKind.Other) => "Other",
        _ => kind
    };

    private static bool IsComparableSource(string kind) =>
        kind is not nameof(CorpusSourceKind.RuntimeArtifacts) and
            not nameof(CorpusSourceKind.SavedGameLogs) and
            not nameof(CorpusSourceKind.Documentation) and
            not nameof(CorpusSourceKind.Data);

    private sealed class ComparisonAccumulator
    {
        private readonly Dictionary<string, FamilyAccumulator> _families =
            new(StringComparer.OrdinalIgnoreCase);

        public void Add(string family, string sourceReference)
        {
            if (!_families.TryGetValue(family, out var accumulator))
            {
                accumulator = new FamilyAccumulator();
                _families.Add(family, accumulator);
            }
            accumulator.Add(sourceReference);
        }

        public IReadOnlyList<CorpusFamilyObservation> ToObservations() =>
            _families
                .OrderBy(pair => pair.Key, StringComparer.OrdinalIgnoreCase)
                .Select(pair => pair.Value.ToObservation(pair.Key))
                .ToArray();
    }

    private sealed record MacroArtifact(
        string MacroName,
        string NodeId,
        string DocumentId,
        string RootId,
        string SourcePath,
        string Family,
        string SourceReference,
        IReadOnlyList<string> AcronymAliases);

    private sealed class FamilyAccumulator
    {
        private readonly HashSet<string> _documents = new(StringComparer.Ordinal);
        private int _evidenceCount;

        public void Add(string sourceReference)
        {
            _evidenceCount++;
            _documents.Add(sourceReference);
        }

        public CorpusFamilyObservation ToObservation(string family)
        {
            var documents = _documents.OrderBy(path => path, StringComparer.Ordinal).ToArray();
            return new CorpusFamilyObservation(
                family,
                _evidenceCount,
                documents.Length,
                documents.Take(5).ToArray());
        }
    }

    private sealed class MacroComparisonKeyComparer : IEqualityComparer<(string Macro, string Signal, string Topic)>
    {
        public bool Equals(
            (string Macro, string Signal, string Topic) left,
            (string Macro, string Signal, string Topic) right) =>
            string.Equals(left.Macro, right.Macro, StringComparison.OrdinalIgnoreCase) &&
            string.Equals(left.Signal, right.Signal, StringComparison.Ordinal) &&
            string.Equals(left.Topic, right.Topic, StringComparison.Ordinal);

        public int GetHashCode((string Macro, string Signal, string Topic) value) =>
            HashCode.Combine(
                StringComparer.OrdinalIgnoreCase.GetHashCode(value.Macro),
                StringComparer.Ordinal.GetHashCode(value.Signal),
                StringComparer.Ordinal.GetHashCode(value.Topic));
    }

    private sealed class PatternComparisonKeyComparer : IEqualityComparer<(string Type, string Pattern)>
    {
        public bool Equals(
            (string Type, string Pattern) left,
            (string Type, string Pattern) right) =>
            string.Equals(left.Type, right.Type, StringComparison.Ordinal) &&
            string.Equals(left.Pattern, right.Pattern, StringComparison.Ordinal);

        public int GetHashCode((string Type, string Pattern) value) =>
            HashCode.Combine(
                StringComparer.Ordinal.GetHashCode(value.Type),
                StringComparer.Ordinal.GetHashCode(value.Pattern));
    }
}
