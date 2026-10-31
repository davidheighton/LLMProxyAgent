using LLMSurvivingProxyAgent.Core;

namespace LLMSurvivingProxyAgent.Macros;

public sealed class MacroRegistry
{
    private readonly IReadOnlyDictionary<string, IMacro> _macros;

    public MacroRegistry(IEnumerable<IMacro> macros)
    {
        ArgumentNullException.ThrowIfNull(macros);
        var registered = new Dictionary<string, IMacro>(StringComparer.OrdinalIgnoreCase);
        foreach (var macro in macros)
        {
            if (!registered.TryAdd(macro.Descriptor.Id, macro))
            {
                throw new InvalidOperationException(
                    $"A macro with ID '{macro.Descriptor.Id}' is already registered.");
            }
        }
        _macros = registered;
    }

    public IReadOnlyList<MacroDescriptor> Descriptors =>
        _macros.Values
            .Select(macro => macro.Descriptor)
            .OrderBy(descriptor => descriptor.Category)
            .ThenBy(descriptor => descriptor.Name, StringComparer.Ordinal)
            .ToArray();

    public IMacro GetRequired(string macroId) =>
        _macros.TryGetValue(macroId, out var macro)
            ? macro
            : throw new KeyNotFoundException($"Macro '{macroId}' is not registered.");
}
