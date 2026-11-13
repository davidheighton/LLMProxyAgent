using System.ComponentModel;
using System.Diagnostics;
using System.Globalization;
using System.Net;
using LLMSurvivingProxyAgent.Core;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace LLMSurvivingProxyAgent.Runtime;

/// <summary>
/// How to start a brain's <c>llama-server</c> when it is not already running. One server per brain is shared by every
/// MTC agent process on the machine (llama-server serves concurrent requests from its <c>--parallel</c> slots), so a
/// model is loaded into GPU memory once, not once per agent.
/// <code>
/// Brains:{brain}:LlamaCpp:ModelPath       .gguf file; required for automatic launch
/// Brains:{brain}:LlamaCpp:ServerPath      llama-server executable (default: llama-server on PATH)
/// Brains:{brain}:LlamaCpp:ContextSize     tokens per request slot (Pilot 8192, Strategic 16384)
/// Brains:{brain}:LlamaCpp:Parallel        concurrent request slots, i.e. agents served at once (default 2)
/// Brains:{brain}:LlamaCpp:GpuLayers       layers offloaded to the GPU (default 999 = all of them)
/// Brains:{brain}:LlamaCpp:KvCacheType     KV-cache precision, e.g. q8_0 (about half of f16's VRAM); default f16
/// Brains:{brain}:LlamaCpp:DraftModelPath  small same-family .gguf for speculative decoding (e.g. Qwen3-0.6B for a
///                                         Qwen3 strategist); speeds up long generations on a GPU
/// Brains:{brain}:LlamaCpp:ExtraArguments  appended verbatim, e.g. "-fa on --cache-type-k q8_0"
/// </code>
/// </summary>
public sealed record LlamaCppServerOptions(
    string Brain,
    string ServerPath,
    string ModelPath,
    string Alias,
    string Host,
    int Port,
    int ContextSize,
    int Parallel,
    int GpuLayers,
    string? ExtraArguments)
{
    public string? KvCacheType { get; init; }
    public string? DraftModelPath { get; init; }

    public static LlamaCppServerOptions? Read(IConfiguration configuration, string brain, ModelConfiguration model)
    {
        ArgumentNullException.ThrowIfNull(configuration);
        ArgumentNullException.ThrowIfNull(model);
        if (!LlmBackends.IsLlamaCpp(model))
        {
            return null;
        }

        string section = $"Brains:{brain}:LlamaCpp";
        string? modelPath = configuration[$"{section}:ModelPath"];
        if (string.IsNullOrWhiteSpace(modelPath))
        {
            return null;
        }
        if (!model.Endpoint.IsLoopback)
        {
            throw new InvalidOperationException($"{brain} llama-server can only be launched on a loopback endpoint.");
        }

        int Read(string name, int fallback, int minimum)
        {
            string? text = configuration[$"{section}:{name}"];
            if (string.IsNullOrWhiteSpace(text))
            {
                return fallback;
            }
            return int.TryParse(text, NumberStyles.Integer, CultureInfo.InvariantCulture, out int value) && value >= minimum
                ? value
                : throw new InvalidOperationException($"{section}:{name} must be an integer of at least {minimum}.");
        }

        return new LlamaCppServerOptions(
            brain,
            configuration[$"{section}:ServerPath"] is { Length: > 0 } serverPath ? serverPath : "llama-server",
            Path.GetFullPath(modelPath),
            model.Model,
            model.Endpoint.Host,
            model.Endpoint.Port,
            Read("ContextSize", brain == BrainConfiguration.Strategic ? 16_384 : 8_192, 512),
            Read("Parallel", 2, 1),
            Read("GpuLayers", 999, 0),
            configuration[$"{section}:ExtraArguments"])
        {
            KvCacheType = configuration[$"{section}:KvCacheType"] is { Length: > 0 } kvCache ? kvCache.Trim() : null,
            DraftModelPath = configuration[$"{section}:DraftModelPath"] is { Length: > 0 } draft ? Path.GetFullPath(draft) : null
        };
    }

    /// <summary>The llama-server command line. Context is per slot, so the total is ContextSize × Parallel.</summary>
    public IReadOnlyList<string> BuildArguments()
    {
        var arguments = new List<string>
        {
            "--model", ModelPath,
            "--alias", Alias,
            "--host", Host,
            "--port", Port.ToString(CultureInfo.InvariantCulture),
            "--ctx-size", ((long)ContextSize * Parallel).ToString(CultureInfo.InvariantCulture),
            "--parallel", Parallel.ToString(CultureInfo.InvariantCulture),
            "--n-gpu-layers", GpuLayers.ToString(CultureInfo.InvariantCulture),
            // Chat templates (and their enable_thinking switch) need the Jinja template engine.
            "--jinja"
        };
        if (KvCacheType is { } kvCacheType)
        {
            arguments.AddRange(["--cache-type-k", kvCacheType, "--cache-type-v", kvCacheType]);
        }
        if (DraftModelPath is { } draftModel)
        {
            arguments.AddRange(["--model-draft", draftModel, "--gpu-layers-draft", GpuLayers.ToString(CultureInfo.InvariantCulture)]);
        }
        if (!string.IsNullOrWhiteSpace(ExtraArguments))
        {
            arguments.AddRange(SplitArguments(ExtraArguments));
        }
        return arguments;
    }

    internal static IEnumerable<string> SplitArguments(string text)
    {
        var current = new System.Text.StringBuilder();
        bool quoted = false;
        foreach (char character in text)
        {
            if (character == '"')
            {
                quoted = !quoted;
            }
            else if (char.IsWhiteSpace(character) && !quoted)
            {
                if (current.Length > 0)
                {
                    yield return current.ToString();
                    current.Clear();
                }
            }
            else
            {
                current.Append(character);
            }
        }
        if (current.Length > 0)
        {
            yield return current.ToString();
        }
    }
}

public enum LlamaCppServerState
{
    Unreachable,
    Loading,
    Ready
}

/// <summary>Checks and, when configured, starts a brain's llama-server.</summary>
public static class LlamaCppServer
{
    /// <summary><c>GET /health</c>: 200 when the model is loaded, 503 while it is loading.</summary>
    public static async Task<LlamaCppServerState> GetStateAsync(
        HttpClient client,
        Uri endpoint,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(client);
        ArgumentNullException.ThrowIfNull(endpoint);
        try
        {
            using HttpResponseMessage response = await client
                .GetAsync(new Uri(endpoint.ToString().TrimEnd('/') + "/health"), cancellationToken)
                .ConfigureAwait(false);
            return response.IsSuccessStatusCode ? LlamaCppServerState.Ready
                : response.StatusCode == HttpStatusCode.ServiceUnavailable ? LlamaCppServerState.Loading
                : LlamaCppServerState.Unreachable;
        }
        catch (HttpRequestException)
        {
            return LlamaCppServerState.Unreachable;
        }
        catch (TaskCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            return LlamaCppServerState.Unreachable;
        }
    }

    /// <summary>Starts llama-server detached; it keeps serving other agents after this process exits.</summary>
    public static string? TryLaunch(LlamaCppServerOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);
        if (!File.Exists(options.ModelPath))
        {
            return $"{options.Brain} model file not found: {options.ModelPath}";
        }
        if (options.DraftModelPath is { } draft && !File.Exists(draft))
        {
            return $"{options.Brain} draft model file not found: {draft}";
        }

        var startInfo = new ProcessStartInfo
        {
            FileName = options.ServerPath,
            UseShellExecute = false,
            CreateNoWindow = true
        };
        foreach (string argument in options.BuildArguments())
        {
            startInfo.ArgumentList.Add(argument);
        }

        try
        {
            using Process? process = Process.Start(startInfo);
            return process is null ? $"{options.Brain} llama-server did not start." : null;
        }
        catch (Win32Exception exception)
        {
            return $"{options.Brain} llama-server could not be started from '{options.ServerPath}': {exception.Message}";
        }
    }
}

/// <summary>
/// At host startup, makes sure every llama.cpp brain has a server: brains that share an endpoint share a server; an
/// unreachable one is launched when <c>Brains:{brain}:LlamaCpp:ModelPath</c> is configured. Never blocks startup —
/// agents simply retry their model calls until the server is ready.
/// </summary>
public sealed class LlamaCppServerStartupService(
    BrainModels brains,
    IConfiguration configuration,
    ILogger<LlamaCppServerStartupService> logger) : BackgroundService
{
    private static readonly TimeSpan ReadyTimeout = TimeSpan.FromMinutes(5);

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var servers = new[]
            {
                (Brain: BrainConfiguration.Pilot, Model: brains.Pilot),
                (Brain: BrainConfiguration.Strategic, Model: brains.Strategic),
                (Brain: BrainConfiguration.Reactive, Model: brains.Reactive)
            }
            .Where(item => LlmBackends.IsLlamaCpp(item.Model))
            .GroupBy(item => item.Model.Endpoint.ToString().TrimEnd('/'), StringComparer.OrdinalIgnoreCase)
            .Select(group => group.First())
            .ToArray();
        if (servers.Length == 0)
        {
            return;
        }

        using var client = new HttpClient { Timeout = TimeSpan.FromSeconds(3) };
        await Task.WhenAll(servers.Select(server => EnsureAsync(client, server.Brain, server.Model, stoppingToken)))
            .ConfigureAwait(false);
    }

    private async Task EnsureAsync(
        HttpClient client,
        string brain,
        ModelConfiguration model,
        CancellationToken stoppingToken)
    {
        try
        {
            LlamaCppServerState state = await LlamaCppServer.GetStateAsync(client, model.Endpoint, stoppingToken)
                .ConfigureAwait(false);
            if (state == LlamaCppServerState.Unreachable)
            {
                LlamaCppServerOptions? options = LlamaCppServerOptions.Read(configuration, brain, model);
                if (options is null)
                {
                    logger.LogWarning(
                        "{Brain} brain uses llama.cpp at {Endpoint}, which is not running. Start llama-server there or set Brains:{Brain}:LlamaCpp:ModelPath to launch it automatically.",
                        brain,
                        model.Endpoint,
                        brain);
                    return;
                }

                string? failure = LlamaCppServer.TryLaunch(options);
                if (failure is not null)
                {
                    logger.LogError("{Failure}", failure);
                    return;
                }
                logger.LogInformation(
                    "Launched llama-server for the {Brain} brain: {Arguments}",
                    brain,
                    string.Join(' ', options.BuildArguments()));
            }

            DateTimeOffset deadline = DateTimeOffset.UtcNow + ReadyTimeout;
            while (state != LlamaCppServerState.Ready && DateTimeOffset.UtcNow < deadline)
            {
                await Task.Delay(TimeSpan.FromSeconds(2), stoppingToken).ConfigureAwait(false);
                state = await LlamaCppServer.GetStateAsync(client, model.Endpoint, stoppingToken).ConfigureAwait(false);
            }
            if (state == LlamaCppServerState.Ready)
            {
                logger.LogInformation("{Brain} brain ready: {Model}", brain, BrainConfiguration.Describe(model));
            }
            else
            {
                logger.LogWarning("{Brain} llama-server at {Endpoint} is still not ready.", brain, model.Endpoint);
            }
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
        {
        }
    }
}
