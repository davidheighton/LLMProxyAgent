using System.Globalization;
using System.IO;
using System.Text.RegularExpressions;

namespace LLMSurvivingProxyAgent.Core;

public static class ScriptInputPromptPolicy
{
    private static readonly Regex BracketedChoice = new(
        @"<\s*([A-Za-z0-9])\s*>",
        RegexOptions.Compiled | RegexOptions.CultureInvariant);
    private static readonly Regex ParenthesizedChoices = new(
        @"\(([A-Za-z0-9](?:\s*/\s*[A-Za-z0-9])+)\)",
        RegexOptions.Compiled | RegexOptions.CultureInvariant);
    private static readonly Regex LineChoices = new(
        @"^\s*([A-Za-z0-9])\s*[-:]\s+\S",
        RegexOptions.Compiled | RegexOptions.Multiline | RegexOptions.CultureInvariant);
    private static readonly Regex NumericPrompt = new(
        @"\b(quantity|amount|number|to buy|holds|fighters|shields|maximum|max)\b",
        RegexOptions.Compiled | RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
    private static readonly Regex SensitivePrompt = new(
        @"\b(password|passcode|secret|credential|security code|login)\b",
        RegexOptions.Compiled | RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);

    public static bool IsApprovedScript(string? scriptName)
    {
        string fileName = Path.GetFileName(scriptName ?? string.Empty);
        return fileName.Equals("ls-dock-shopper-v33.ts", StringComparison.OrdinalIgnoreCase) ||
            fileName.Equals("2-worldtrade.ts", StringComparison.OrdinalIgnoreCase);
    }

    public static bool IsSafeResponse(GameScriptInputPrompt? prompt, string? response)
    {
        if (prompt is null ||
            !IsApprovedScript(prompt.ScriptName) ||
            string.IsNullOrWhiteSpace(prompt.Text) ||
            string.IsNullOrWhiteSpace(response) ||
            response.Length > 32 ||
            response.Any(character => char.IsControl(character) || character > 0x7e) ||
            SensitivePrompt.IsMatch(prompt.Text))
        {
            return false;
        }

        HashSet<char> visibleChoices = GetVisibleChoices(prompt.Text);
        if (prompt.SingleKey)
        {
            char selectedChoice = response.Length == 1
                ? char.ToUpperInvariant(response[0])
                : '\0';
            return selectedChoice != '\0' &&
                !(Path.GetFileName(prompt.ScriptName).Equals(
                    "ls-dock-shopper-v33.ts",
                    StringComparison.OrdinalIgnoreCase) &&
                  selectedChoice == 'Z') &&
                visibleChoices.Contains(selectedChoice);
        }

        if (response.Length == 1 &&
            visibleChoices.Contains(char.ToUpperInvariant(response[0])) &&
            !(Path.GetFileName(prompt.ScriptName).Equals(
                "ls-dock-shopper-v33.ts",
                StringComparison.OrdinalIgnoreCase) &&
              char.ToUpperInvariant(response[0]) == 'Z'))
        {
            return true;
        }

        return NumericPrompt.IsMatch(prompt.Text) &&
            int.TryParse(response, NumberStyles.None, CultureInfo.InvariantCulture, out int quantity) &&
            quantity is >= 0 and <= 1_000;
    }

    private static HashSet<char> GetVisibleChoices(string promptText)
    {
        var choices = new HashSet<char>();
        foreach (Match match in BracketedChoice.Matches(promptText))
            choices.Add(char.ToUpperInvariant(match.Groups[1].Value[0]));

        foreach (Match match in ParenthesizedChoices.Matches(promptText))
        {
            foreach (string choice in match.Groups[1].Value.Split('/'))
            {
                string trimmed = choice.Trim();
                if (trimmed.Length == 1)
                    choices.Add(char.ToUpperInvariant(trimmed[0]));
            }
        }

        foreach (Match match in LineChoices.Matches(promptText))
            choices.Add(char.ToUpperInvariant(match.Groups[1].Value[0]));

        return choices;
    }
}
