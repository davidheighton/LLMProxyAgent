namespace LLMSurvivingProxyAgent.Knowledge;

public enum KnowledgeLayer
{
    Unknown,
    Facts,
    Procedural,
    Tactical,
    Communication,
    Cooperative,
    Strategic,
    Structural,
    Experience
}

public static class KnowledgeLayers
{
    private static readonly IReadOnlyDictionary<KnowledgeLayer, KnowledgeCategory[]> CategoriesByLayer =
        new Dictionary<KnowledgeLayer, KnowledgeCategory[]>
        {
            [KnowledgeLayer.Facts] =
            [
                KnowledgeCategory.WorldKnowledge,
                KnowledgeCategory.GameplayMechanic
            ],
            [KnowledgeLayer.Procedural] =
            [
                KnowledgeCategory.MacroDefinition,
                KnowledgeCategory.MacroConstructionPattern,
                KnowledgeCategory.ReactiveTriggerPattern
            ],
            [KnowledgeLayer.Tactical] =
            [
                KnowledgeCategory.TacticalGuidance,
                KnowledgeCategory.TacticalPattern
            ],
            [KnowledgeLayer.Communication] =
            [
                KnowledgeCategory.CommunicationPattern
            ],
            [KnowledgeLayer.Cooperative] =
            [
                KnowledgeCategory.CooperativeProcedure,
                KnowledgeCategory.CorpProtocol,
                KnowledgeCategory.EmergencyResponse,
                KnowledgeCategory.ResourceTransfer,
                KnowledgeCategory.RoleDefinition
            ],
            [KnowledgeLayer.Strategic] =
            [
                KnowledgeCategory.StrategicGuidance,
                KnowledgeCategory.DecisionContext
            ],
            [KnowledgeLayer.Structural] =
            [
                KnowledgeCategory.MenuStructure
            ],
            [KnowledgeLayer.Experience] =
            [
                KnowledgeCategory.AgentExperience
            ]
        };

    public static IReadOnlyList<KnowledgeCategory> GetCategories(KnowledgeLayer layer) =>
        CategoriesByLayer.TryGetValue(layer, out var categories)
            ? categories
            : throw new ArgumentOutOfRangeException(nameof(layer), layer, "Unknown knowledge layer.");

    public static KnowledgeLayer GetLayer(KnowledgeCategory category)
    {
        foreach (var (layer, categories) in CategoriesByLayer)
        {
            if (categories.Contains(category))
            {
                return layer;
            }
        }
        throw new ArgumentOutOfRangeException(
            nameof(category),
            category,
            "Knowledge category is not assigned to a layer.");
    }
}
