using LLMSurvivingProxyAgent.Agent;
using LLMSurvivingProxyAgent.Core;

namespace LLMSurvivingProxyAgent.TradeWars.Tests;

public sealed class SqliteAgentStoreTests
{
    [Fact]
    public async Task InitializingAnotherProcessDoesNotMarkEveryAgentOffline()
    {
        string directory = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        try
        {
            string connectionString = new Microsoft.Data.Sqlite.SqliteConnectionStringBuilder
            {
                DataSource = Path.Combine(directory, "agents.db"),
                ForeignKeys = true,
                Pooling = false
            }.ToString();
            var firstProcessStore = new SqliteAgentStore(connectionString);
            await firstProcessStore.InitializeAsync();
            await firstProcessStore.SaveAgentAsync(new AgentContainer
            {
                AgentId = "game-d",
                DisplayName = "Game D",
                Status = AgentStatus.Running
            });

            var secondProcessStore = new SqliteAgentStore(connectionString);
            await secondProcessStore.InitializeAsync();

            AgentContainer agent = Assert.IsType<AgentContainer>(
                await secondProcessStore.GetAgentAsync("game-d"));
            Assert.Equal(AgentStatus.Running, agent.Status);
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }
}
