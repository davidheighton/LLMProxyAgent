using System.Globalization;
using LLMSurvivingProxyAgent.Core;
using Microsoft.Extensions.Configuration;

namespace LLMSurvivingProxyAgent.Runtime;

public static class EpisodeOptionsConfiguration
{
    public const string SectionName = "Learning:Episode";
    public static readonly TimeSpan DefaultWallClockCap = TimeSpan.FromHours(8);

    /// <summary>Reads harness episode settings; returns null when no episode ID is configured.</summary>
    public static EpisodeOptions? Read(IConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(configuration);
        IConfigurationSection section = configuration.GetSection(SectionName);
        string? episodeId = section["Id"];
        if (string.IsNullOrWhiteSpace(episodeId))
        {
            return null;
        }
        if (episodeId.Length > 64 || !episodeId.All(character => char.IsAsciiLetterOrDigit(character) || character is '-' or '_' or '.'))
        {
            throw new InvalidOperationException(
                "Learning:Episode:Id must be 1-64 characters using letters, digits, '.', '_' or '-'.");
        }

        EpisodeGoal goal = EpisodeGoal.FullyUpgradedShip;
        if (section["Goal"] is { Length: > 0 } goalText &&
            !Enum.TryParse(goalText.Replace("-", string.Empty, StringComparison.Ordinal), ignoreCase: true, out goal))
        {
            throw new InvalidOperationException($"Learning:Episode:Goal '{goalText}' is not a known episode goal.");
        }

        TimeSpan cap = DefaultWallClockCap;
        if (section["WallClockMinutes"] is { Length: > 0 } capText)
        {
            if (!double.TryParse(capText, NumberStyles.Float, CultureInfo.InvariantCulture, out double minutes) ||
                minutes <= 0)
            {
                throw new InvalidOperationException("Learning:Episode:WallClockMinutes must be a positive number.");
            }
            cap = TimeSpan.FromMinutes(minutes);
        }

        int minimumTurns = EpisodeOptions.DefaultMinimumTurns;
        if (section["MinimumTurns"] is { Length: > 0 } turnsText &&
            (!int.TryParse(turnsText, NumberStyles.Integer, CultureInfo.InvariantCulture, out minimumTurns) ||
             minimumTurns < 0))
        {
            throw new InvalidOperationException("Learning:Episode:MinimumTurns must be zero or positive.");
        }

        string? pilotModel = section["PilotModel"];
        if (!string.IsNullOrWhiteSpace(pilotModel) &&
            (pilotModel.Length > 100 ||
             !pilotModel.All(character => char.IsAsciiLetterOrDigit(character) || character is ':' or '.' or '-' or '_' or '/')))
        {
            throw new InvalidOperationException(
                "Learning:Episode:PilotModel must be an Ollama model name such as qwen3:1.7b.");
        }

        return new EpisodeOptions(episodeId, goal, cap)
        {
            MinimumTurns = minimumTurns,
            ResultPath = string.IsNullOrWhiteSpace(section["ResultPath"]) ? null : Path.GetFullPath(section["ResultPath"]!),
            ExitOnEnd = bool.TryParse(section["ExitOnEnd"], out bool exitOnEnd) && exitOnEnd,
            PilotModel = string.IsNullOrWhiteSpace(pilotModel) ? null : pilotModel
        };
    }
}
