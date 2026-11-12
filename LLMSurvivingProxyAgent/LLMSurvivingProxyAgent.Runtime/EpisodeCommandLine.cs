using LLMSurvivingProxyAgent.Core;
using Microsoft.Extensions.Configuration;

namespace LLMSurvivingProxyAgent.Runtime;

/// <summary>
/// Command-line options that make an MTC process run one harness-managed episode. MTC forwards the parsed
/// values through environment variables into the <c>Learning:Episode</c> configuration section.
/// </summary>
public static class EpisodeCommandLine
{
    public const string EnvironmentPrefix = "LLMCONTROL_EPISODE_";

    private static readonly (string Option, string Name, bool TakesValue)[] Options =
    [
        ("--episode-id", "Id", true),
        ("--episode-goal", "Goal", true),
        ("--episode-wall-clock-minutes", "WallClockMinutes", true),
        ("--episode-minimum-turns", "MinimumTurns", true),
        ("--episode-result-path", "ResultPath", true),
        ("--episode-pilot-model", "PilotModel", true),
        ("--exit-on-episode-end", "ExitOnEnd", false)
    ];

    public static IEnumerable<string> ConfigurationNames => Options.Select(option => option.Name);

    /// <summary>
    /// Removes episode options from <paramref name="args"/> and validates them. Returns configuration values
    /// keyed by name inside <see cref="EpisodeOptionsConfiguration.SectionName"/>.
    /// </summary>
    public static bool TryExtract(
        IReadOnlyList<string> args,
        out string[] remainingArgs,
        out IReadOnlyDictionary<string, string> values,
        out string? error)
    {
        ArgumentNullException.ThrowIfNull(args);
        var remaining = new List<string>(args.Count);
        var extracted = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        remainingArgs = [];
        values = extracted;
        error = null;

        for (int index = 0; index < args.Count; index++)
        {
            string argument = args[index];
            (string Option, string Name, bool TakesValue)? match = null;
            string? value = null;
            foreach (var option in Options)
            {
                if (string.Equals(argument, option.Option, StringComparison.OrdinalIgnoreCase))
                {
                    match = option;
                    if (option.TakesValue)
                    {
                        if (index + 1 >= args.Count || args[index + 1].StartsWith("--", StringComparison.Ordinal))
                        {
                            error = $"{option.Option} requires a value.";
                            return false;
                        }
                        value = args[++index];
                    }
                    else
                    {
                        value = "true";
                    }
                    break;
                }
                if (option.TakesValue && argument.StartsWith(option.Option + "=", StringComparison.OrdinalIgnoreCase))
                {
                    match = option;
                    value = argument[(option.Option.Length + 1)..];
                    break;
                }
            }

            if (match is not { } found)
            {
                remaining.Add(argument);
                continue;
            }
            if (!extracted.TryAdd(found.Name, value!))
            {
                error = $"{found.Option} may only be specified once.";
                return false;
            }
        }

        if (extracted.Count > 0)
        {
            if (!extracted.ContainsKey("Id"))
            {
                error = "--episode-id is required when other episode options are used.";
                return false;
            }

            try
            {
                EpisodeOptionsConfiguration.Read(ToConfiguration(extracted));
            }
            catch (InvalidOperationException exception)
            {
                error = exception.Message;
                return false;
            }
        }

        remainingArgs = remaining.ToArray();
        return true;
    }

    public static IReadOnlyList<string> ToArguments(EpisodeOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);
        var arguments = new List<string>
        {
            "--episode-id", options.EpisodeId,
            "--episode-goal", options.Goal.ToString(),
            "--episode-wall-clock-minutes",
            options.WallClockCap.TotalMinutes.ToString(System.Globalization.CultureInfo.InvariantCulture),
            "--episode-minimum-turns",
            options.MinimumTurns.ToString(System.Globalization.CultureInfo.InvariantCulture)
        };
        if (options.ResultPath is { } resultPath)
        {
            arguments.Add("--episode-result-path");
            arguments.Add(resultPath);
        }
        if (options.PilotModel is { } pilotModel)
        {
            arguments.Add("--episode-pilot-model");
            arguments.Add(pilotModel);
        }
        if (options.ExitOnEnd)
        {
            arguments.Add("--exit-on-episode-end");
        }
        return arguments;
    }

    public static void ApplyEnvironment(IConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(configuration);
        foreach (string name in ConfigurationNames)
        {
            if (Environment.GetEnvironmentVariable(EnvironmentPrefix + name.ToUpperInvariant()) is { Length: > 0 } value)
            {
                configuration[$"{EpisodeOptionsConfiguration.SectionName}:{name}"] = value;
            }
        }
    }

    /// <summary>Reads episode options set by <see cref="SetEnvironment"/>; null when this is not an episode run.</summary>
    public static EpisodeOptions? ReadEnvironment()
    {
        IConfiguration configuration = new ConfigurationBuilder().AddInMemoryCollection().Build();
        ApplyEnvironment(configuration);
        return EpisodeOptionsConfiguration.Read(configuration);
    }

    public static void SetEnvironment(IReadOnlyDictionary<string, string> values)
    {
        ArgumentNullException.ThrowIfNull(values);
        foreach (string name in ConfigurationNames)
        {
            Environment.SetEnvironmentVariable(
                EnvironmentPrefix + name.ToUpperInvariant(),
                values.TryGetValue(name, out string? value) ? value : null);
        }
    }

    private static IConfiguration ToConfiguration(IReadOnlyDictionary<string, string> values) =>
        new ConfigurationBuilder()
            .AddInMemoryCollection(values.Select(pair =>
                new KeyValuePair<string, string?>($"{EpisodeOptionsConfiguration.SectionName}:{pair.Key}", pair.Value)))
            .Build();
}
