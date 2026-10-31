using System.Text.RegularExpressions;

namespace LLMSurvivingProxyAgent.Knowledge;

public enum DocumentationSourceRole
{
    TacticalGuide,
    HelpReference,
    DesignNotes,
    ForumMaterial,
    PersonalNotes,
    RuntimeHistory,
    GeneralDocumentation
}

public static partial class DocumentationSourceClassifier
{
    private static readonly HashSet<string> RuntimeHistoryMarkers = new(StringComparer.OrdinalIgnoreCase)
    {
        "history", "journal", "log", "logs", "review", "reviews", "telemetry"
    };

    private static readonly HashSet<string> ForumMarkers = new(StringComparer.OrdinalIgnoreCase)
    {
        "bulletin", "forum", "forums", "post", "posts", "thread", "threads"
    };

    private static readonly HashSet<string> DesignMarkers = new(StringComparer.OrdinalIgnoreCase)
    {
        "architecture", "decision", "design", "planning", "proposal", "roadmap"
    };

    private static readonly HashSet<string> PersonalNoteMarkers = new(StringComparer.OrdinalIgnoreCase)
    {
        "diary", "notebook", "notes", "personal"
    };

    private static readonly HashSet<string> HelpMarkers = new(StringComparer.OrdinalIgnoreCase)
    {
        "api", "command", "commands", "computer", "faq", "help", "manual", "menu", "reference", "whatsnew"
    };

    private static readonly HashSet<string> TacticalGuideMarkers = new(StringComparer.OrdinalIgnoreCase)
    {
        "doctrine", "guide", "guides", "playbook", "slice", "strategy", "strategies", "tactic", "tactics"
    };

    private static readonly Regex ReferenceHistoryMarker = new(
        @"what['’]s[\s_-]+new|tlxtw|sysop|compnavigation|corporate.?menu.?map",
        RegexOptions.IgnoreCase | RegexOptions.Compiled | RegexOptions.CultureInvariant);

    public static DocumentationSourceRole Classify(KnowledgeDocument document)
    {
        ArgumentNullException.ThrowIfNull(document);
        return ClassifyPath(document.RelativePath);
    }

    public static DocumentationSourceRole ClassifyPath(string relativePath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(relativePath);
        var path = relativePath.Replace('\\', '/');
        var tokens = TokenPattern().Matches(path)
            .Select(match => match.Value)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        if (path.Contains("/generated-mombot/", StringComparison.OrdinalIgnoreCase))
        {
            return DocumentationSourceRole.HelpReference;
        }
        if (tokens.Overlaps(RuntimeHistoryMarkers))
        {
            return DocumentationSourceRole.RuntimeHistory;
        }
        if (tokens.Overlaps(ForumMarkers))
        {
            return DocumentationSourceRole.ForumMaterial;
        }
        if (tokens.Overlaps(DesignMarkers))
        {
            return DocumentationSourceRole.DesignNotes;
        }
        if (tokens.Overlaps(PersonalNoteMarkers))
        {
            return DocumentationSourceRole.PersonalNotes;
        }
        if (tokens.Overlaps(HelpMarkers) || ReferenceHistoryMarker.IsMatch(path))
        {
            return DocumentationSourceRole.HelpReference;
        }
        if (tokens.Overlaps(TacticalGuideMarkers))
        {
            return DocumentationSourceRole.TacticalGuide;
        }
        return DocumentationSourceRole.GeneralDocumentation;
    }

    public static bool IsMinedDocumentation(KnowledgeDocument document) =>
        document.Language is "Markdown" or "Text" or "HTML" or "WordDocumentText" or "TiddlyWiki";

    [GeneratedRegex(@"[a-z0-9]+", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex TokenPattern();
}
