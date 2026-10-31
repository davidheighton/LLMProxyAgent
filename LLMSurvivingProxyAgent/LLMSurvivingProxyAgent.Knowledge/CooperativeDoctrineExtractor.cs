using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;

namespace LLMSurvivingProxyAgent.Knowledge;

internal static class CooperativeDoctrineExtractor
{
    private static readonly Regex SensitiveSetting = new(
        @"^\s*(?:ALIAS|SHIPNAME|PLANETNAME|CORPNAME|CORPPW|SSCHANNEL|AUTOLOAD|PASSWORD|PASSWD|SECRET|TOKEN|API[_-]?KEY)\s*[:=]",
        RegexOptions.IgnoreCase | RegexOptions.Compiled | RegexOptions.CultureInvariant);
    private static readonly Regex SensitiveValue = new(
        @"(?i)\b(password|passwd|secret|token|api[_ -]?key|corp[_ -]?pw|sschannel)\b(\s*[:=]\s*|\s+)([""']?)[^\s""'`,;]+",
        RegexOptions.Compiled | RegexOptions.CultureInvariant);
    private static readonly Regex TacticalBranch = new(
        @"^\s*if\s*\((?<trigger>[^)]{1,120})\)\s*(?<action>goto\s+:?(?:retreat|flee|escape|evac\w*|safe\w*|recover\w*|abort\w*|halt\w*|stop\w*)|halt|stop|exit|disconnect)\b.*$",
        RegexOptions.IgnoreCase | RegexOptions.Compiled | RegexOptions.CultureInvariant);
    private static readonly Regex TwxSendCommand = new(
        @"^\s*send\s+(?<payload>.+)$",
        RegexOptions.IgnoreCase | RegexOptions.Compiled | RegexOptions.CultureInvariant);
    private static readonly Regex TwxStringLiteral = new(
        "\"(?<value>(?:\\\\.|[^\"\\\\])*)\"",
        RegexOptions.Compiled | RegexOptions.CultureInvariant);
    private static readonly Regex SubspaceMessageCue = new(
        @"\b(bust(?:ed)?|photoned|photon|saveme|save\s*me|need\s+(?:figs|fighters|fuel|ore|cash|credits|support)|locked|escort|cover|backup|rescue)\b",
        RegexOptions.IgnoreCase | RegexOptions.Compiled | RegexOptions.CultureInvariant);
    private static readonly Regex IfThen = new(
        @"^\s*(?:if|when|unless)\s+(?<trigger>.+?)(?:,|\s+then\s+)(?<action>.+?)(?:\s+because\s+(?<rationale>.+?))?[.;]?\s*$",
        RegexOptions.IgnoreCase | RegexOptions.Compiled | RegexOptions.CultureInvariant);
    private static readonly Regex GameContext = new(
        @"\b(port|trade|sector|warp|route|stardock|credits?|turns?|fighters?|figs|ship|planet|colonists?|cim|ore|combat|photon|bust|shield|scanner|corp|team|ally|fuel|holds?)\b",
        RegexOptions.IgnoreCase | RegexOptions.Compiled | RegexOptions.CultureInvariant);
    private static readonly Regex Channel = new(
        @"\b(sub[- ]?space|radio|fed(?:eration)? comm|hail|message)\b",
        RegexOptions.IgnoreCase | RegexOptions.Compiled | RegexOptions.CultureInvariant);
    private static readonly Regex Participant = new(
        @"\b(corp(?:oration|ies|y)?|teammates?|team|ally|alliance|members?)\b",
        RegexOptions.IgnoreCase | RegexOptions.Compiled | RegexOptions.CultureInvariant);
    private static readonly Regex Resource = new(
        @"\b(fighters?|figs|credits?|cash|ore|fuel|holds?|colonists?|ships?|planets?|support|assistance)\b",
        RegexOptions.IgnoreCase | RegexOptions.Compiled | RegexOptions.CultureInvariant);
    private static readonly Regex AssistanceRequest = new(
        @"\b(?<request>need|request(?:ing)?|require(?:d)?|send me|looking for|can someone (?:send|provide|help))\s+(?<resource>fighters?|figs|credits?|cash|ore|fuel|holds?|colonists?|ships?|support|assistance)\b",
        RegexOptions.IgnoreCase | RegexOptions.Compiled | RegexOptions.CultureInvariant);
    private static readonly Regex CommunicationAction = new(
        @"\b(send|broadcast|report|notify|alert|share|update|keep .{0,30} updated)\b|\bmessage\s+(?:the\s+)?(?:corp|team|teammates?|ally|player)\b",
        RegexOptions.IgnoreCase | RegexOptions.Compiled | RegexOptions.CultureInvariant);
    private static readonly Regex Threat = new(
        @"\b(incoming photon|photoned|photon(?:ed)?|stuck|podded|bust(?:ed)?|hostile|enemy|trapped|trap detected|lane compromised)\b",
        RegexOptions.IgnoreCase | RegexOptions.Compiled | RegexOptions.CultureInvariant);
    private static readonly Regex EmergencyAction = new(
        @"\b(send(?: out)?\s+(?:a\s+)?(?:save me|saveme|hail|warning)|call\s+save me|broadcast\s+(?:bust|threat|hostile|photon)\w*|report(?:ing)?\s+(?:bust|threat|progress|status|them)\w*|notify\s+(?:the\s+)?(?:corp|team|ally|support)\w*|alert\s+(?:the\s+)?(?:corp|team|ally|support)\w*)",
        RegexOptions.IgnoreCase | RegexOptions.Compiled | RegexOptions.CultureInvariant);
    private static readonly Regex ExplicitResponse = new(
        @"\b(?:reply|respond|answer)\s+(?:with|by)\s+(?<response>[^.;]{2,100})",
        RegexOptions.IgnoreCase | RegexOptions.Compiled | RegexOptions.CultureInvariant);
    private static readonly Regex FurbProvisioning = new(
        @"\bfurb\b.{0,100}\b(?:buys?|buy|deliver(?:s|ed|ing)?)\b.{0,80}\bship\b.{0,60}\b(?:corp(?:y|ies|oration)?|attack|holds?)\b",
        RegexOptions.IgnoreCase | RegexOptions.Compiled | RegexOptions.CultureInvariant);
    private static readonly Regex ResourceTransferAction = new(
        @"\b(?<action>transfer|provide|give|deliver|send|share|supply|buy)\b.{0,60}\b(?<resource>fighters?|figs|credits?|cash|ore|fuel|holds?|colonists?|ships?|planets?)\b",
        RegexOptions.IgnoreCase | RegexOptions.Compiled | RegexOptions.CultureInvariant);
    private static readonly Regex CorpProtocol = new(
        @"\b(?:corp(?:oration)?|teammates?|team|alliance)\b.{0,100}\b(?:standard setup|protocol|create|created|made|join|joined|owned|ownership|channel|coordinate|settings?)\b|\b(?:standard setup|protocol)\b.{0,100}\b(?:corp(?:oration)?|teammates?|team|alliance)\b",
        RegexOptions.IgnoreCase | RegexOptions.Compiled | RegexOptions.CultureInvariant);
    private static readonly Regex OrderedStep = new(
        @"^\s*(?:step\s*)?\d+[.)]\s*(?<step>.+)$",
        RegexOptions.IgnoreCase | RegexOptions.Compiled | RegexOptions.CultureInvariant);
    private static readonly Regex CooperativeStepAction = new(
        @"\b(identify|locate|find|notify|alert|assess|provide|transfer|deliver|escort|rescue|report|broadcast|support|secure|verify)\b",
        RegexOptions.IgnoreCase | RegexOptions.Compiled | RegexOptions.CultureInvariant);
    private static readonly Regex RoleStatement = new(
        @"^\s*(?<role>scout|fighter supplier|furb provider|banker|builder|trader|defender|attacker|intel gatherer|planet manager|bot operator)\s*(?::|-|is responsible for|handles)\s*(?<duties>.+)$",
        RegexOptions.IgnoreCase | RegexOptions.Compiled | RegexOptions.CultureInvariant);
    private static readonly Regex Responsibility = new(
        @"\b(responsible for|handles|manages|provides|supplies|gathers|reports|defends|builds|operates)\b",
        RegexOptions.IgnoreCase | RegexOptions.Compiled | RegexOptions.CultureInvariant);

    public static IReadOnlyList<KnowledgeNode> Extract(
        string documentId,
        string fileName,
        string extension,
        string content)
    {
        var candidates = new Dictionary<(KnowledgeCategory Category, string Title), Candidate>();
        var lines = content.Split(["\r\n", "\n"], StringSplitOptions.None);
        var cooperativeStepLines = new List<(int LineNumber, string Text, string Step)>();
        var hasCooperativeContext = false;

        for (var index = 0; index < lines.Length; index++)
        {
            var sourceLine = lines[index];
            var lineNumber = index + 1;
            if (SensitiveSetting.IsMatch(sourceLine))
            {
                continue;
            }

            if (IsCodeFile(extension))
            {
                var branch = TacticalBranch.Match(sourceLine);
                if (branch.Success &&
                    GameContext.IsMatch(branch.Groups["trigger"].Value))
                {
                    Add(
                        KnowledgeCategory.TacticalPattern,
                        $"Tactical pattern: {branch.Groups["action"].Value.Trim()}",
                        "A source branch pairs the cited condition with a safety-oriented action target; runtime behavior and rationale are not inferred.",
                        0.68m,
                        ExtractionBasis.StaticStructure,
                        lineNumber,
                        "explicit safety branch",
                        sourceLine,
                        new KnowledgeEvidenceDetails
                        {
                            Trigger = Limit(branch.Groups["trigger"].Value),
                            Action = Limit(branch.Groups["action"].Value),
                            Intent = null,
                            Rationale = null
                        });
                }
            }

            if (IsTwxScript(extension) &&
                !TryGetTextLine(sourceLine, extension, out _) &&
                TryGetTwxSubspaceMessage(sourceLine, out var messageTemplate))
            {
                var intent = SubspaceMessageIntent(messageTemplate);
                Add(
                    KnowledgeCategory.CommunicationPattern,
                    $"Communication pattern: SubSpace {intent}",
                    "The source sends a literal Sub-Space message template; dynamic values are placeholders. This is implemented behavior, not a captured exchange or response.",
                    0.64m,
                    ExtractionBasis.StaticStructure,
                    lineNumber,
                    "explicit outbound SubSpace message template",
                    sourceLine,
                    new KnowledgeEvidenceDetails
                    {
                        MessageTemplate = Limit(messageTemplate),
                        Channel = "SubSpace",
                        Intent = intent
                    });
            }

            if (!TryGetTextLine(sourceLine, extension, out var text))
            {
                continue;
            }

            var participant = Participant.Match(text);
            var resource = Resource.Match(text);
            var channel = Channel.Match(text);
            hasCooperativeContext |= participant.Success ||
                AssistanceRequest.IsMatch(text) ||
                FurbProvisioning.IsMatch(text) ||
                CorpProtocol.IsMatch(text);

            var ifThen = IfThen.Match(text);
            if (ifThen.Success &&
                GameContext.IsMatch(ifThen.Groups["trigger"].Value + " " + ifThen.Groups["action"].Value))
            {
                var rationale = ifThen.Groups["rationale"].Success
                    ? Limit(ifThen.Groups["rationale"].Value)
                    : null;
                Add(
                    KnowledgeCategory.TacticalPattern,
                    $"Tactical pattern: {Limit(ifThen.Groups["action"].Value)}",
                    "The cited text explicitly pairs a condition with an action; no unstated intent or game outcome is inferred.",
                    0.64m,
                    ExtractionBasis.ExplicitText,
                    lineNumber,
                    "explicit condition and action",
                    text,
                    new KnowledgeEvidenceDetails
                    {
                        Trigger = Limit(ifThen.Groups["trigger"].Value),
                        Action = Limit(ifThen.Groups["action"].Value),
                        Rationale = rationale
                    });
            }

            var request = AssistanceRequest.Match(text);
            var communicationAction = CommunicationAction.Match(text);
            var hasCommunicationSignal = request.Success ||
                channel.Success && (communicationAction.Success || participant.Success) ||
                participant.Success && communicationAction.Success;
            if (hasCommunicationSignal)
            {
                var intent = request.Success
                    ? "explicit resource or assistance request"
                    : Threat.IsMatch(text)
                        ? "threat or emergency report"
                        : Regex.IsMatch(text, @"\b(progress|cycles?|turns?|figs|fighters|status)\b", RegexOptions.IgnoreCase)
                            ? "progress or status update"
                            : "information sharing";
                var requestedResource = request.Success
                    ? NormalizeResource(request.Groups["resource"].Value)
                    : null;
                var explicitResponse = ExplicitResponse.Match(text);
                var requiredAction = request.Success
                    ? $"Respond to the explicit request for {requestedResource}"
                    : communicationAction.Success
                        ? $"{communicationAction.Value} the information stated in the message"
                        : null;
                var communicationTitle = requestedResource is null
                    ? intent
                    : $"{requestedResource} request";
                Add(
                    KnowledgeCategory.CommunicationPattern,
                    $"Communication pattern: {communicationTitle}",
                    "The cited source explicitly describes a message, report, or assistance request; the response is not assumed unless stated.",
                    0.58m,
                    ExtractionBasis.ExplicitText,
                    lineNumber,
                    "communication or request statement",
                    text,
                    new KnowledgeEvidenceDetails
                    {
                        Message = Limit(text),
                        Channel = channel.Success ? Limit(channel.Value) : null,
                        Participants = participant.Success ? Limit(participant.Value) : null,
                        Intent = Limit(intent),
                        RequiredAction = requiredAction is null ? null : Limit(requiredAction),
                        ExpectedResponse = explicitResponse.Success
                            ? Limit(explicitResponse.Groups["response"].Value)
                            : null
                    });
            }

            var threat = Threat.Match(text);
            var emergencyAction = EmergencyAction.Match(text);
            if (threat.Success && emergencyAction.Success)
            {
                Add(
                    KnowledgeCategory.EmergencyResponse,
                    $"Emergency response: {NormalizeThreat(threat.Value)}",
                    "The cited passage pairs an explicit threat or emergency condition with a response action; it does not establish response success.",
                    0.62m,
                    ExtractionBasis.ExplicitText,
                    lineNumber,
                    "explicit trigger and response",
                    text,
                    new KnowledgeEvidenceDetails
                    {
                        Trigger = Limit(threat.Value),
                        Action = Limit(emergencyAction.Value),
                        Intent = Regex.IsMatch(text, @"\b(save me|saveme|rescue|support)\b", RegexOptions.IgnoreCase)
                            ? "request assistance"
                            : "report an emergency"
                    });
            }

            var furb = FurbProvisioning.Match(text);
            if (furb.Success)
            {
                var description = Limit(furb.Value);
                Add(
                    KnowledgeCategory.CooperativeProcedure,
                    "Cooperative procedure: FURB provisioning",
                    "The source describes buying and delivering a ship to a corporation member; no additional rescue steps are inferred.",
                    0.72m,
                    ExtractionBasis.ExplicitText,
                    lineNumber,
                    "explicit FURB description",
                    text,
                    new KnowledgeEvidenceDetails
                    {
                        Action = description,
                        Participants = participant.Success ? Limit(participant.Value) : null
                    });
                Add(
                    KnowledgeCategory.ResourceTransfer,
                    "Resource transfer: ship provisioning",
                    "The cited FURB description explicitly identifies a ship as the provisioned resource.",
                    0.72m,
                    ExtractionBasis.ExplicitText,
                    lineNumber,
                    "explicit ship provisioning",
                    text,
                    new KnowledgeEvidenceDetails
                    {
                        Action = description,
                        Participants = participant.Success ? Limit(participant.Value) : null
                    });
            }

            var transfer = ResourceTransferAction.Match(text);
            if (transfer.Success &&
                (participant.Success || channel.Success || request.Success) &&
                !furb.Success)
            {
                Add(
                    KnowledgeCategory.ResourceTransfer,
                    $"Resource transfer: {NormalizeResource(transfer.Groups["resource"].Value)}",
                    "The cited source names a resource and an explicit provision, transfer, or delivery action in a cooperative context.",
                    0.58m,
                    ExtractionBasis.ExplicitText,
                    lineNumber,
                    "explicit resource action",
                    text,
                    new KnowledgeEvidenceDetails
                    {
                        Action = Limit(transfer.Value),
                        Participants = participant.Success ? Limit(participant.Value) : null
                    });
            }

            if (CorpProtocol.IsMatch(text))
            {
                Add(
                    KnowledgeCategory.CorpProtocol,
                    "Corporation and team operating protocol",
                    "The cited passage explicitly describes corporation, team, membership, asset, or coordination setup.",
                    0.58m,
                    ExtractionBasis.ExplicitText,
                    lineNumber,
                    "explicit corporation or team setup",
                    text,
                    new KnowledgeEvidenceDetails
                    {
                        Action = Limit(text),
                        Participants = participant.Success ? Limit(participant.Value) : null
                    });
            }

            var role = RoleStatement.Match(text);
            if (role.Success && Responsibility.IsMatch(role.Groups["duties"].Value))
            {
                var duties = Limit(role.Groups["duties"].Value);
                Add(
                    KnowledgeCategory.RoleDefinition,
                    $"Role definition: {Humanize(role.Groups["role"].Value)}",
                    "The source explicitly associates a named role with responsibilities; completeness and adoption are not inferred.",
                    0.56m,
                    ExtractionBasis.ExplicitText,
                    lineNumber,
                    "explicit role and responsibilities",
                    text,
                    new KnowledgeEvidenceDetails
                    {
                        Role = Limit(role.Groups["role"].Value),
                        Action = duties,
                        Steps = [duties]
                    });
            }

            var orderedStep = OrderedStep.Match(text);
            if (orderedStep.Success &&
                CooperativeStepAction.IsMatch(orderedStep.Groups["step"].Value) &&
                (participant.Success || Resource.IsMatch(orderedStep.Groups["step"].Value) ||
                 Threat.IsMatch(orderedStep.Groups["step"].Value)))
            {
                cooperativeStepLines.Add((
                    lineNumber,
                    text,
                    Limit(orderedStep.Groups["step"].Value)));
            }
        }

        if (hasCooperativeContext && cooperativeStepLines.Count >= 2)
        {
            var steps = cooperativeStepLines.Select(item => item.Step).ToArray();
            foreach (var step in cooperativeStepLines)
            {
                Add(
                    KnowledgeCategory.CooperativeProcedure,
                    $"Cooperative procedure: {Humanize(Path.GetFileNameWithoutExtension(fileName))}",
                    "An explicitly numbered sequence contains multiple cooperative actions; the source order is retained without filling missing steps.",
                    0.62m,
                    ExtractionBasis.ExplicitText,
                    step.LineNumber,
                    "ordered cooperative step",
                    step.Text,
                    new KnowledgeEvidenceDetails { Steps = steps });
            }
        }

        return candidates.Values
            .OrderBy(candidate => candidate.Category)
            .ThenBy(candidate => candidate.Title, StringComparer.Ordinal)
            .Select(candidate => new KnowledgeNode(
                StableId($"{documentId}\n{candidate.Category}\n{candidate.Title}"),
                documentId,
                candidate.Category,
                candidate.Title,
                candidate.Summary,
                candidate.Confidence,
                candidate.Basis,
                candidate.Evidence))
            .ToArray();

        void Add(
            KnowledgeCategory category,
            string title,
            string summary,
            decimal confidence,
            ExtractionBasis basis,
            int lineNumber,
            string signal,
            string text,
            KnowledgeEvidenceDetails details)
        {
            var key = (category, title);
            if (!candidates.TryGetValue(key, out var candidate))
            {
                candidate = new Candidate(category, title, summary, confidence, basis);
                candidates.Add(key, candidate);
            }

            if (candidate.Evidence.Count < 12 &&
                !candidate.Evidence.Any(item => item.LineNumber == lineNumber && item.Signal == signal))
            {
                candidate.Evidence.Add(new KnowledgeEvidence(
                    lineNumber,
                    signal,
                    Limit(text))
                {
                    Details = details
                });
            }
        }
    }

    private static bool TryGetTextLine(string sourceLine, string extension, out string text)
    {
        var trimmed = sourceLine.Trim();
        if (extension.Equals(".md", StringComparison.OrdinalIgnoreCase) ||
            extension.Equals(".txt", StringComparison.OrdinalIgnoreCase) ||
            extension.Equals(".html", StringComparison.OrdinalIgnoreCase) ||
            extension.Equals(".htm", StringComparison.OrdinalIgnoreCase) ||
            extension.Equals(".doc", StringComparison.OrdinalIgnoreCase) ||
            extension.Equals(".tid", StringComparison.OrdinalIgnoreCase))
        {
            text = trimmed.TrimStart('#', '>', '-', '*', ' ');
            return text.Length > 0;
        }

        foreach (var marker in new[] { "//", "#", ";" })
        {
            if (trimmed.StartsWith(marker, StringComparison.Ordinal))
            {
                text = trimmed[marker.Length..].Trim();
                return text.Length > 0;
            }
        }

        if (trimmed.StartsWith('*') && !trimmed.StartsWith("*/", StringComparison.Ordinal))
        {
            text = trimmed[1..].Trim();
            return text.Length > 0;
        }

        text = string.Empty;
        return false;
    }

    private static bool IsCodeFile(string extension) =>
        extension.ToLowerInvariant() is ".cts" or ".ts" or ".inc" or ".ets" or ".cs" or ".pas" or ".js";

    private static bool IsTwxScript(string extension) =>
        extension.ToLowerInvariant() is ".cts" or ".ts" or ".inc" or ".ets";

    private static bool TryGetTwxSubspaceMessage(string sourceLine, out string messageTemplate)
    {
        var command = TwxSendCommand.Match(sourceLine);
        var payload = command.Success ? command.Groups["payload"].Value : string.Empty;
        var literals = TwxStringLiteral.Matches(payload);
        if (literals.Count == 0 ||
            !literals[0].Groups["value"].Value.StartsWith("'", StringComparison.Ordinal))
        {
            messageTemplate = string.Empty;
            return false;
        }

        var template = new StringBuilder();
        var previousEnd = 0;
        foreach (Match literal in literals)
        {
            if (literal.Index > previousEnd &&
                !string.IsNullOrWhiteSpace(payload[previousEnd..literal.Index]))
            {
                template.Append("{dynamic}");
            }
            template.Append(Regex.Replace(literal.Groups["value"].Value, @"\\(.)", "$1"));
            previousEnd = literal.Index + literal.Length;
        }
        if (previousEnd < payload.Length &&
            !string.IsNullOrWhiteSpace(payload[previousEnd..]))
        {
            template.Append("{dynamic}");
        }

        messageTemplate = template.ToString();
        return SubspaceMessageCue.IsMatch(messageTemplate);
    }

    private static string SubspaceMessageIntent(string messageTemplate)
    {
        if (AssistanceRequest.IsMatch(messageTemplate))
        {
            return "resource request";
        }
        if (Regex.IsMatch(messageTemplate, @"\b(save\s*me|saveme|rescue|help)\b", RegexOptions.IgnoreCase))
        {
            return "assistance request";
        }
        if (Regex.IsMatch(messageTemplate, @"\bbust(?:ed)?\b", RegexOptions.IgnoreCase))
        {
            return "bust report";
        }
        if (Regex.IsMatch(messageTemplate, @"\bphotoned\b", RegexOptions.IgnoreCase))
        {
            return "threat report";
        }
        if (Regex.IsMatch(messageTemplate, @"\bphoton\b", RegexOptions.IgnoreCase))
        {
            return "photon status";
        }
        if (Regex.IsMatch(messageTemplate, @"\blocked\b", RegexOptions.IgnoreCase))
        {
            return "status report";
        }
        return "support coordination";
    }

    private static string NormalizeResource(string value) =>
        value.Trim().ToLowerInvariant() switch
        {
            "fig" or "fighter" or "figs" or "fighters" => "fighters",
            "credit" or "credits" or "cash" => "credits",
            "ship" or "ships" => "ship",
            "hold" or "holds" => "holds",
            "colonist" or "colonists" => "colonists",
            _ => value.Trim().ToLowerInvariant()
        };

    private static string NormalizeThreat(string value) =>
        Regex.IsMatch(value, @"photon", RegexOptions.IgnoreCase)
            ? "photon threat"
            : Regex.IsMatch(value, @"bust", RegexOptions.IgnoreCase)
                ? "bust"
                : value.ToLowerInvariant();

    private static string Humanize(string value)
    {
        var words = Regex.Replace(value.Replace('_', ' ').Replace('-', ' '), @"\s+", " ").Trim();
        return words.Length == 0
            ? "source"
            : char.ToUpperInvariant(words[0]) + words[1..];
    }

    private static string Limit(string value)
    {
        var trimmed = SensitiveValue.Replace(value.Trim(), "$1$2[REDACTED]");
        return trimmed.Length <= 240 ? trimmed : trimmed[..240];
    }

    private static string StableId(string value) =>
        Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(value))).ToLowerInvariant();

    private sealed class Candidate(
        KnowledgeCategory category,
        string title,
        string summary,
        decimal confidence,
        ExtractionBasis basis)
    {
        public KnowledgeCategory Category { get; } = category;
        public string Title { get; } = title;
        public string Summary { get; } = summary;
        public decimal Confidence { get; } = confidence;
        public ExtractionBasis Basis { get; } = basis;
        public List<KnowledgeEvidence> Evidence { get; } = [];
    }
}
