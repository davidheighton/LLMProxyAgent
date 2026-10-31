using LLMSurvivingProxyAgent.Core;
using LLMSurvivingProxyAgent.Runtime;

namespace LLMSurvivingProxyAgent.TradeWars.Tests;

public sealed class LlmInteractionHistoryStoreTests
{
    [Fact]
    public void ReplacesInProgressTraceAndStoresAppliedStrategyOutcome()
    {
        var store = new LlmInteractionHistoryStore();
        DateTimeOffset startedAt = DateTimeOffset.UtcNow;
        var inProgress = new LlmInteractionRecord(
            "interaction-1",
            "meridian",
            "Strategic Review",
            "Ollama",
            "qwen3:8b",
            1,
            startedAt,
            startedAt,
            "[system]\nReview progress.",
            string.Empty,
            false,
            null)
        {
            IsInProgress = true
        };
        store.Record(inProgress);
        store.Record(inProgress with
        {
            CompletedAt = startedAt.AddSeconds(3),
            Response = """{"objectiveDecision":"selectProposal"}""",
            Succeeded = true,
            IsInProgress = false
        });
        store.RecordAppliedOutcome("interaction-1", "Selected the active learning proposal.");

        var result = Assert.Single(store.GetRecent("MERIDIAN"));
        Assert.False(result.IsInProgress);
        Assert.Equal("Selected the active learning proposal.", result.AppliedOutcome);
        Assert.Equal(TimeSpan.FromSeconds(3), result.Duration);
    }

    [Fact]
    public void RetainsOnlyTheNewestInteractionsForEachAgent()
    {
        var store = new LlmInteractionHistoryStore();
        DateTimeOffset now = DateTimeOffset.UtcNow;
        for (int index = 0; index <= LlmInteractionHistoryStore.MaximumInteractionsPerAgent; index++)
        {
            store.Record(new LlmInteractionRecord(
                $"interaction-{index}",
                "meridian",
                "Pilot",
                "Ollama",
                "qwen3:8b",
                1,
                now,
                now,
                $"prompt-{index}",
                $"response-{index}",
                true,
                null));
        }

        var recent = store.GetRecent("meridian", LlmInteractionHistoryStore.MaximumInteractionsPerAgent);

        Assert.Equal(LlmInteractionHistoryStore.MaximumInteractionsPerAgent, recent.Count);
        Assert.Equal("interaction-100", recent[0].InteractionId);
        Assert.Equal("interaction-1", recent[^1].InteractionId);
    }

    [Fact]
    public void RetainsPilotHistoryIndependentlyOfBusyReflexRole()
    {
        var store = new LlmInteractionHistoryStore();
        DateTimeOffset now = DateTimeOffset.UtcNow;

        for (int index = 0; index < 150; index++)
        {
            store.Record(new LlmInteractionRecord(
                $"reflex-{index}",
                "meridian",
                "Reflex",
                "Ollama",
                "qwen3:1.7b",
                1,
                now.AddSeconds(index),
                now.AddSeconds(index + 1),
                $"reflex-prompt-{index}",
                $"reflex-response-{index}",
                true,
                null));
        }

        for (int index = 0; index < 5; index++)
        {
            store.Record(new LlmInteractionRecord(
                $"pilot-{index}",
                "meridian",
                "Pilot",
                "Ollama",
                "qwen3:1.7b",
                1,
                now.AddMinutes(1).AddSeconds(index),
                now.AddMinutes(1).AddSeconds(index + 1),
                $"pilot-prompt-{index}",
                $"pilot-response-{index}",
                true,
                null));
        }

        var pilotHistory = store.GetRecentByRole("meridian", "Pilot", 100);
        var reflexHistory = store.GetRecentByRole("meridian", "Reflex", 100);

        Assert.Equal(5, pilotHistory.Count);
        Assert.Equal("pilot-4", pilotHistory[0].InteractionId);
        Assert.Equal(100, reflexHistory.Count);
        Assert.Equal("reflex-149", reflexHistory[0].InteractionId);
        Assert.Equal("reflex-50", reflexHistory[^1].InteractionId);
    }

    [Fact]
    public async Task EventHubKeepsNewestEventsAndReportsEvictionsWhenFull()
    {
        var hub = new ProxyGameEventHub();

        for (int index = 0; index < ProxyGameEventHub.Capacity; index++)
        {
            Assert.True(hub.TryPublish(new ProxySessionEvent(
                "meridian",
                new ProxyGameEvent(DateTimeOffset.UtcNow, "PresenceHeartbeat"))));
        }

        Assert.Equal(ProxyGameEventHub.Capacity, hub.QueueDepth);
        Assert.True(hub.TryPublish(new ProxySessionEvent(
            "meridian",
            new ProxyGameEvent(DateTimeOffset.UtcNow, "ServerPrompt", PromptSurface: "latest"))));
        Assert.Equal(ProxyGameEventHub.Capacity, hub.QueueDepth);
        Assert.Equal(1, hub.DroppedEventCount);

        await using var reader = hub.ReadAllAsync().GetAsyncEnumerator();
        for (int index = 0; index < ProxyGameEventHub.Capacity; index++)
        {
            Assert.True(await reader.MoveNextAsync());
            if (index == ProxyGameEventHub.Capacity - 1)
            {
                Assert.Equal("ServerPrompt", reader.Current.Event.Kind);
                Assert.Equal("latest", reader.Current.Event.PromptSurface);
            }
        }

        Assert.Equal(0, hub.QueueDepth);
    }
}
