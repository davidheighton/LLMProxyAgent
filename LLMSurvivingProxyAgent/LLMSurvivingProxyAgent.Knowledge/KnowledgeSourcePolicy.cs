namespace LLMSurvivingProxyAgent.Knowledge;

public static class KnowledgeSourcePolicy
{
    public const long MaximumSourceBytes = 4 * 1024 * 1024;
    public const long MaximumArchiveBytes = 128 * 1024 * 1024;
    public const long MaximumArchiveExpandedBytes = 64 * 1024 * 1024;
    public const int MaximumArchiveEntries = 2048;

    private static readonly HashSet<string> SupportedExtensions = new(StringComparer.OrdinalIgnoreCase)
    {
        ".cts", ".ts", ".inc", ".ets", ".cs", ".pas", ".js",
        ".md", ".txt", ".html", ".htm", ".doc", ".tid"
    };

    private static readonly HashSet<string> ExcludedMiningDirectories = new(StringComparer.OrdinalIgnoreCase)
    {
        ".git", ".vs", "bin", "obj", "node_modules", "packages", "vendor",
        ".venv", "__pycache__", ".cache", "dist", "build", "out", "coverage",
        "logs", "runtime", "status", "telemetry", "recordings", "secrets",
        "credentials", "private", "tokens"
    };

    private static readonly HashSet<string> ExcludedMiningFileNames = new(StringComparer.OrdinalIgnoreCase)
    {
        ".env", ".env.local", ".env.production", "credentials.json", "secrets.json"
    };

    public static bool IsSupportedSourceFile(string path, string? rootId = null) =>
        IsSupportedTextExtension(path, rootId) &&
        !IsMiningExcludedPath(path);

    public static bool IsSupportedArchiveFile(string path) =>
        Path.GetExtension(path).Equals(".zip", StringComparison.OrdinalIgnoreCase) &&
        !IsMiningExcludedPath(path);

    public static bool IsSupportedArchiveEntry(string path, string? rootId = null) =>
        IsSupportedTextExtension(path, rootId) &&
        IsSafeArchiveEntryPath(path) &&
        !IsMiningExcludedPath(path);

    public static bool IsSafeArchiveEntryPath(string path)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            return false;
        }

        var normalized = path.Replace('\\', '/');
        if (normalized[0] == '/' ||
            normalized.Length >= 2 && char.IsLetter(normalized[0]) && normalized[1] == ':')
        {
            return false;
        }

        var segments = normalized.Split('/');
        return segments.All(segment =>
            segment.Length > 0 &&
            segment is not ("." or "..") &&
            !segment.Contains(':'));
    }

    public static bool IsMiningExcludedPath(string path) =>
        Path.GetFileName(path) is { } fileName &&
        (ExcludedMiningFileNames.Contains(fileName) ||
         path.Split([Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar], StringSplitOptions.RemoveEmptyEntries)
             .Any(ExcludedMiningDirectories.Contains));

    public static bool IsExcludedMiningDirectory(string directoryName) =>
        ExcludedMiningDirectories.Contains(directoryName);

    private static bool IsSupportedTextExtension(string path, string? rootId)
    {
        var extension = Path.GetExtension(path).ToLowerInvariant();
        if (SupportedExtensions.Contains(extension))
        {
            return true;
        }

        if (string.Equals(rootId, "tribal", StringComparison.OrdinalIgnoreCase) &&
            extension is ".slt" or ".key")
        {
            return true;
        }

        return (extension is ".backup" or ".msfucked") &&
            Path.GetFileNameWithoutExtension(path).EndsWith(".ts", StringComparison.OrdinalIgnoreCase);
    }
}
