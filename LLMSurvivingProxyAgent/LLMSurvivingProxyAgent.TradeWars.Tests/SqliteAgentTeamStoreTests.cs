using LLMSurvivingProxyAgent.Agent;
using LLMSurvivingProxyAgent.Core;

namespace LLMSurvivingProxyAgent.TradeWars.Tests;

public sealed class SqliteAgentTeamStoreTests
{
    [Fact]
    public async Task PersistsRecentStatusesByTeamAndUpsertsAgentIdsCaseInsensitively()
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
            var store = new SqliteAgentTeamStore(connectionString);
            await store.InitializeAsync();

            DateTimeOffset now = DateTimeOffset.UtcNow;
            await store.UpsertStatusAsync(new(
                "trade-team",
                "Scout",
                "Game E",
                now.AddMinutes(-2),
                true,
                551,
                100,
                35927,
                "Acquire a Density Scanner"));
            await store.UpsertStatusAsync(new(
                "TRADE-TEAM",
                "scout",
                "Game E",
                now,
                true,
                552,
                99,
                40000,
                "Start World Trade"));
            await store.UpsertStatusAsync(new(
                "trade-team",
                "Hauler",
                "Game D",
                now.AddMinutes(-30),
                true,
                100,
                50,
                12000,
                "Trade"));
            await store.UpsertStatusAsync(new(
                "other-team",
                "Other",
                "Game E",
                now,
                true,
                1,
                1,
                1,
                null));

            IReadOnlyList<AgentTeamStatus> members =
                await store.GetRecentTeamMembersAsync(
                    "trade-team",
                    "current-agent",
                    now.AddMinutes(-15));

            var member = Assert.Single(members);
            Assert.Equal("Scout", member.AgentId);
            Assert.Equal("Game E", member.SessionName);
            Assert.True(member.HasFreshSnapshot);
            Assert.Equal(552, member.CurrentSector);
            Assert.Equal("Start World Trade", member.ActiveGoal);
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }
}
