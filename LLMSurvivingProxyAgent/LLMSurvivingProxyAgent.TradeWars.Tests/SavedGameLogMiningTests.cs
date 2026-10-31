using System.IO.Compression;
using System.Text.Json;
using System.Text.Json.Serialization;
using LLMSurvivingProxyAgent.CorpusBuilder;
using LLMSurvivingProxyAgent.Knowledge;
using LLMSurvivingProxyAgent.Packaging;
using Microsoft.Data.Sqlite;

namespace LLMSurvivingProxyAgent.TradeWars.Tests;

public sealed class SavedGameLogMiningTests
{
    [Fact]
    public async Task BuildHarvestsRepeatedIncomingMenuOptionsWithoutImportingTranscriptText()
    {
        using var directory = new TemporaryDirectory();
        var nodeRoot = directory.Combine("node-bot");
        var logRoot = Path.Combine(nodeRoot, "logs");
        Directory.CreateDirectory(logRoot);
        await File.WriteAllTextAsync(Path.Combine(nodeRoot, "bot.js"), "console.log('bot source');");
        var logPath = Path.Combine(logRoot, "server-session.log");
        var firstDisplay = new[]
        {
            "[RX-LINE 2026-10-23T08:00:00.000Z] º <L> Land on a Planet  <T> Corporate Menu   º",
            "[RX-LINE 2026-10-23T08:00:00.001Z] º <N> Move to NavPoint <!> Main Menu Help   º"
        };
        var computerDisplay = new[]
        {
            "[RX-LINE 2026-10-23T08:00:00.002Z] º <F> Course Plotter     <A> Make Announcement º",
            "[RX-LINE 2026-10-23T08:00:00.003Z] º <!> Computer Help     <Q> Exit Computer      º"
        };
        var lines = new List<string>(firstDisplay);
        lines.AddRange(Enumerable.Range(0, 10).Select(index =>
            $"[RX-LINE 2026-10-23T08:00:01.{index:000}Z] Private conversation and secret value must be ignored."));
        lines.AddRange(firstDisplay);
        lines.AddRange(Enumerable.Range(0, 10).Select(index =>
            $"[RX-LINE 2026-10-23T08:00:02.{index:000}Z] Other game output."));
        lines.AddRange(computerDisplay);
        lines.AddRange(Enumerable.Range(0, 10).Select(index =>
            $"[RX-LINE 2026-10-23T08:00:03.{index:000}Z] Other game output."));
        lines.AddRange(computerDisplay);
        lines.Add("[TX       2026-10-23T08:00:02.000Z] private command input must be ignored.");
        await File.WriteAllLinesAsync(logPath, lines);

        var result = await new CorpusBuildService().BuildAsync(new CorpusBuildOptions(
            "Saved-game menu capture",
            "1.2.0",
            directory.Combine("out", "menus.seedpkg"),
            directory.Combine("out", "menus.inventory.json"),
            [
                new CorpusRoot("node", CorpusSourceKind.NodeBot, nodeRoot),
                new CorpusRoot("saved-logs", CorpusSourceKind.SavedGameLogs, logRoot)
            ]));

        var nodeSummary = Assert.Single(result.Inventory.Sources, source => source.RootId == "node");
        Assert.Equal(1, nodeSummary.TotalFiles);
        Assert.Equal(1, nodeSummary.ExcludedDirectories);
        var summary = Assert.Single(result.Inventory.Sources, source => source.RootId == "saved-logs");
        Assert.Equal(1, summary.DocumentsMined);
        Assert.Equal(2, summary.Coverage.MenuStructuresObserved);
        Assert.Equal(8, summary.Coverage.MenuOptionsObserved);
        Assert.Equal(4, summary.Coverage.MenuDisplayObservations);
        Assert.Equal(CorpusCoverageStatus.Measured, summary.Coverage.MenuCoverage);
        Assert.Equal(CorpusCoverageStatus.Partial, summary.Coverage.LogContentCoverage);

        using var packageStream = File.OpenRead(result.PackagePath);
        using var archive = new ZipArchive(packageStream, ZipArchiveMode.Read);
        var manifest = await DeserializeEntryAsync<PackageManifest>(archive, "manifest.json");
        var payload = await DeserializeEntryAsync<SeedKnowledgePackagePayload>(archive, "payload.json");
        Assert.Equal(3, manifest.SchemaVersion);
        var document = Assert.Single(payload.Documents, item => item.RootId == "saved-logs");
        Assert.Equal("saved-game-log.log", document.FileName);
        Assert.StartsWith("captures/", document.RelativePath, StringComparison.Ordinal);
        var menu = Assert.Single(document.Nodes, node => node.Title == "Menu: Main Command");
        Assert.Equal(KnowledgeCategory.MenuStructure, menu.Category);
        Assert.Contains(menu.Evidence, evidence => evidence.Text == "<L> Land on a Planet");
        Assert.Contains(menu.Evidence, evidence => evidence.Text == "<T> Corporate Menu");
        Assert.Contains(menu.Evidence, evidence => evidence.Text == "<N> Move to NavPoint");
        Assert.Contains(menu.Evidence, evidence => evidence.Text == "<!> Main Menu Help");
        var computerMenu = Assert.Single(document.Nodes, node => node.Title == "Menu: Onboard Computer");
        Assert.Contains(computerMenu.Evidence, evidence => evidence.Text == "<!> Computer Help");
        Assert.Contains(computerMenu.Evidence, evidence => evidence.Text == "<Q> Exit Computer");
        Assert.DoesNotContain(
            "Private conversation",
            JsonSerializer.Serialize(payload),
            StringComparison.OrdinalIgnoreCase);

        var seedStore = new SqliteKnowledgeStore(new SqliteConnectionStringBuilder
        {
            DataSource = directory.Combine("imported-seed.db"),
            ForeignKeys = true,
            Pooling = false
        }.ToString());
        var runtimeStore = new SqliteKnowledgeStore(new SqliteConnectionStringBuilder
        {
            DataSource = directory.Combine("imported-runtime.db"),
            ForeignKeys = true,
            Pooling = false
        }.ToString());
        await seedStore.InitializeAsync();
        await runtimeStore.InitializeAsync();
        await new SeedKnowledgePackageService(seedStore, runtimeStore)
            .ImportAsync(new MemoryStream(await File.ReadAllBytesAsync(result.PackagePath)));
        Assert.Contains(
            (await seedStore.GetAllDocumentsAsync()).SelectMany(item => item.Nodes),
            node => node.Category == KnowledgeCategory.MenuStructure);
    }

    private static async Task<T> DeserializeEntryAsync<T>(ZipArchive archive, string name)
    {
        var entry = archive.GetEntry(name)
            ?? throw new InvalidDataException($"Package entry '{name}' was not found.");
        await using var stream = entry.Open();
        return await JsonSerializer.DeserializeAsync<T>(
                stream,
                new JsonSerializerOptions(JsonSerializerDefaults.Web)
                {
                    Converters = { new JsonStringEnumConverter(JsonNamingPolicy.CamelCase) }
                })
            ?? throw new InvalidDataException($"Package entry '{name}' is empty.");
    }

    private sealed class TemporaryDirectory : IDisposable
    {
        private readonly string _path = Path.Combine(
            Path.GetTempPath(),
            $"twbot-saved-log-{Guid.NewGuid():N}");

        public TemporaryDirectory() => Directory.CreateDirectory(_path);

        public string Combine(params string[] paths) => Path.Combine([_path, .. paths]);

        public void Dispose() => Directory.Delete(_path, recursive: true);
    }
}
