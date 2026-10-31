namespace LLMSurvivingProxyAgent.Runtime;

public sealed class GameProxyListenerOptions
{
    public string AgentId { get; set; } = string.Empty;
    public string ListenAddress { get; set; } = "127.0.0.1";
    public int ListenPort { get; set; }
    public string UpstreamHost { get; set; } = string.Empty;
    public int UpstreamPort { get; set; }
    public string EventLogDirectory { get; set; } = string.Empty;
}
