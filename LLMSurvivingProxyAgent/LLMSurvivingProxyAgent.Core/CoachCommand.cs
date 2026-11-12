namespace LLMSurvivingProxyAgent.Core;

public enum CoachCommandKind
{
    AddEntry,
    Help,
    Error
}

public sealed record CoachCommand(
    CoachCommandKind Kind,
    string Message,
    PlaybookTier Tier = PlaybookTier.Tip,
    PlaybookScope Scope = PlaybookScope.Universal,
    AutonomousTestAction? ForbiddenAction = null,
    IReadOnlyList<string>? AppliesWhen = null,
    bool UseCurrentSituation = false,
    string Guidance = "");

/// <summary>
/// Parses coaching typed in the game terminal. Grammar (modifiers before ':' are optional, in any order):
/// <code>
/// !coach &lt;guidance&gt;
/// !coach [tip|rule] [game] [here] [never &lt;action&gt;] [when &lt;tag&gt;,&lt;tag&gt;] : &lt;guidance&gt;
/// !coach help
/// </code>
/// <c>here</c> scopes the entry to the current situation's credits, holds, scanner, and location tags.
/// <c>game</c> limits it to the current game universe. A rule needs <c>never &lt;action&gt;</c>.
/// </summary>
public static class CoachCommandParser
{
    public const string Prefix = "!coach";

    public static readonly IReadOnlyDictionary<string, AutonomousTestAction> ActionAliases =
        new Dictionary<string, AutonomousTestAction>(StringComparer.OrdinalIgnoreCase)
        {
            ["move"] = AutonomousTestAction.MoveToAdjacentSector,
            ["trade"] = AutonomousTestAction.TradeAtCurrentPort,
            ["worldtrade"] = AutonomousTestAction.StartWorldTradeMode,
            ["shopper"] = AutonomousTestAction.RunStarDockShopper,
            ["buyholds"] = AutonomousTestAction.BuyCargoHolds,
            ["buyfighters"] = AutonomousTestAction.BuyFighters,
            ["buyshields"] = AutonomousTestAction.BuyShields,
            ["refurb"] = AutonomousTestAction.InspectRefurbMenu
        };

    public const string HelpText =
        "!coach <tip>  |  !coach [tip|rule] [game] [here] [never <action>] [when <tag>,<tag>] : <guidance>. " +
        "Actions: move, trade, worldtrade, shopper, buyholds, buyfighters, buyshields, refurb (or full action names). " +
        "Tags: credits:<1k, holds:<30, scanner:none, location:fedspace, ... prefix ! to negate.";

    public static bool IsCoachCommand(string? line) =>
        line is not null &&
        line.TrimStart().StartsWith(Prefix, StringComparison.OrdinalIgnoreCase) &&
        (line.TrimStart().Length == Prefix.Length || char.IsWhiteSpace(line.TrimStart()[Prefix.Length]));

    public static CoachCommand Parse(string line)
    {
        ArgumentNullException.ThrowIfNull(line);
        if (!IsCoachCommand(line))
        {
            return Error("Not a coach command.");
        }

        string body = line.TrimStart()[Prefix.Length..].Trim();
        if (body.Length == 0 || body.Equals("help", StringComparison.OrdinalIgnoreCase))
        {
            return new CoachCommand(CoachCommandKind.Help, HelpText);
        }

        int separator = FindSeparator(body);
        string modifiers;
        string guidance;
        if (separator < 0)
        {
            modifiers = string.Empty;
            guidance = body;
        }
        else
        {
            modifiers = body[..separator];
            guidance = body[(separator + 1)..].Trim();
            if (!LooksLikeModifiers(modifiers))
            {
                modifiers = string.Empty;
                guidance = body;
            }
        }

        if (guidance.Length == 0)
        {
            return Error("Coaching needs guidance text after ':'.");
        }
        if (guidance.Length > 1_000)
        {
            return Error("Coaching guidance is limited to 1,000 characters.");
        }

        var tier = PlaybookTier.Tip;
        var scope = PlaybookScope.Universal;
        bool here = false;
        AutonomousTestAction? forbidden = null;
        var conditions = new List<string>();
        string[] tokens = modifiers.Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        for (int index = 0; index < tokens.Length; index++)
        {
            string token = tokens[index].ToLowerInvariant();
            switch (token)
            {
                case "tip":
                    tier = PlaybookTier.Tip;
                    break;
                case "rule":
                    tier = PlaybookTier.Rule;
                    break;
                case "game":
                    scope = PlaybookScope.Game;
                    break;
                case "here":
                    here = true;
                    break;
                case "never" when index + 1 < tokens.Length:
                    if (!TryParseAction(tokens[++index], out AutonomousTestAction action))
                    {
                        return Error($"Unknown action '{tokens[index]}'. {HelpText}");
                    }
                    forbidden = action;
                    break;
                case "when" when index + 1 < tokens.Length:
                    conditions.AddRange(string.Join(' ', tokens[(index + 1)..])
                        .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries));
                    index = tokens.Length;
                    break;
                default:
                    return Error($"Unknown coach modifier '{tokens[index]}'. {HelpText}");
            }
        }

        IReadOnlyList<string> normalized = PlaybookSituation.Normalize(conditions, out IReadOnlyList<string> invalid);
        if (invalid.Count > 0)
        {
            return Error($"Unknown situation tag(s): {string.Join(", ", invalid)}. Known: {string.Join(", ", PlaybookSituation.Vocabulary)}");
        }
        if (tier == PlaybookTier.Rule && forbidden is null)
        {
            return Error("A rule needs 'never <action>', e.g. '!coach rule never move when holds:<30 : ...'.");
        }
        if (tier == PlaybookTier.Tip && forbidden is not null)
        {
            tier = PlaybookTier.Rule;
        }

        return new CoachCommand(
            CoachCommandKind.AddEntry,
            tier == PlaybookTier.Rule ? "Rule recorded." : "Tip recorded.",
            tier,
            scope,
            forbidden,
            normalized,
            here,
            guidance);
    }

    /// <summary>The subset of current tags used for <c>here</c>: the ones that define the game stage and place.</summary>
    public static IReadOnlyList<string> SelectSituationTags(IReadOnlySet<string> situation) =>
        situation
            .Where(tag =>
                tag.StartsWith("credits:", StringComparison.Ordinal) ||
                tag.StartsWith("holds:", StringComparison.Ordinal) ||
                tag.StartsWith("scanner:", StringComparison.Ordinal) ||
                tag.StartsWith("location:", StringComparison.Ordinal) ||
                tag == "ship:escape-pod")
            .ToArray();

    public static bool TryParseAction(string value, out AutonomousTestAction action)
    {
        if (ActionAliases.TryGetValue(value, out action))
        {
            return true;
        }
        return Enum.TryParse(value, ignoreCase: true, out action) &&
               Enum.IsDefined(action) &&
               action != AutonomousTestAction.None;
    }

    public static string ToTitle(string guidance)
    {
        string singleLine = string.Join(' ', guidance.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries));
        return singleLine.Length <= 80 ? singleLine : singleLine[..77].TrimEnd() + "...";
    }

    /// <summary>
    /// The guidance separator is a ':' preceded or followed by whitespace (or ending the text), so the ':' inside
    /// tags such as <c>credits:&lt;1k</c> is not mistaken for it.
    /// </summary>
    private static int FindSeparator(string body)
    {
        for (int index = 0; index < body.Length; index++)
        {
            if (body[index] == ':' &&
                (index == body.Length - 1 ||
                 char.IsWhiteSpace(body[index + 1]) ||
                 index > 0 && char.IsWhiteSpace(body[index - 1])))
            {
                return index;
            }
        }
        return -1;
    }

    private static bool LooksLikeModifiers(string value)
    {
        string[] tokens = value.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        return tokens.Length > 0 && tokens[0].ToLowerInvariant() is "tip" or "rule" or "game" or "here" or "never" or "when";
    }

    private static CoachCommand Error(string message) => new(CoachCommandKind.Error, message);
}
