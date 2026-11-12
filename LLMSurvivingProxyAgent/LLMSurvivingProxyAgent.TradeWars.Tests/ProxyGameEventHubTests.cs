using LLMSurvivingProxyAgent.Core;
using LLMSurvivingProxyAgent.Runtime;

namespace LLMSurvivingProxyAgent.TradeWars.Tests;

public sealed class ProxyGameEventHubTests
{
    [Fact]
    public async Task AgentReadersReceiveOnlyTheirOwnSessionEvents()
    {
        var hub = new ProxyGameEventHub();
        hub.TryPublish(new ProxySessionEvent(
            "game-d",
            new ProxyGameEvent(DateTimeOffset.UtcNow, "Connected")));
        hub.TryPublish(new ProxySessionEvent(
            "game-e",
            new ProxyGameEvent(DateTimeOffset.UtcNow, "Connected")));

        await using var gameD = hub.ReadAllAsync("game-d").GetAsyncEnumerator();
        await using var gameE = hub.ReadAllAsync("game-e").GetAsyncEnumerator();

        Assert.True(await gameD.MoveNextAsync().AsTask().WaitAsync(TimeSpan.FromSeconds(2)));
        Assert.True(await gameE.MoveNextAsync().AsTask().WaitAsync(TimeSpan.FromSeconds(2)));
        Assert.Equal("game-d", gameD.Current.AgentId);
        Assert.Equal("game-e", gameE.Current.AgentId);
        Assert.Equal(0, hub.GetQueueDepth("game-d"));
        Assert.Equal(0, hub.GetQueueDepth("game-e"));
    }

    [Fact]
    public void AgentQueueEvictionsAreTrackedIndependently()
    {
        var hub = new ProxyGameEventHub();

        for (int index = 0; index < ProxyGameEventHub.Capacity; index++)
        {
            Assert.True(hub.TryPublish(new ProxySessionEvent(
                "game-d",
                new ProxyGameEvent(DateTimeOffset.UtcNow, "Prompt", PromptSurface: index.ToString()))));
        }

        Assert.True(hub.TryPublish(new ProxySessionEvent(
            "game-d",
            new ProxyGameEvent(DateTimeOffset.UtcNow, "Prompt", PromptSurface: "newest"))));
        Assert.True(hub.TryPublish(new ProxySessionEvent(
            "game-e",
            new ProxyGameEvent(DateTimeOffset.UtcNow, "Prompt", PromptSurface: "independent"))));

        Assert.Equal(ProxyGameEventHub.Capacity, hub.GetQueueDepth("game-d"));
        Assert.Equal(1, hub.GetDroppedEventCount("game-d"));
        Assert.Equal(1, hub.GetQueueDepth("game-e"));
        Assert.Equal(0, hub.GetDroppedEventCount("game-e"));
    }
}
