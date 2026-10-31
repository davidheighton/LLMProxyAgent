using LLMSurvivingProxyAgent.Packaging;

namespace LLMSurvivingProxyAgent.CorpusBuilder;

public enum CorpusSourceKind
{
    TwxScripts,
    PinkScripts,
    MomBot,
    Twx30,
    NodeBot,
    Documentation,
    Data,
    RuntimeArtifacts,
    Other,
    SavedGameLogs,
    Zed
}

public sealed record CorpusRoot(string Id, CorpusSourceKind Kind, string Path);

public sealed record CorpusBuildOptions(
    string Source,
    string Version,
    string PackagePath,
    string InventoryPath,
    IReadOnlyList<CorpusRoot> Roots);

public sealed record CorpusBuildResult(
    string PackagePath,
    string InventoryPath,
    CorpusInventoryReport Inventory,
    int DocumentsExported,
    int NodesExported);
