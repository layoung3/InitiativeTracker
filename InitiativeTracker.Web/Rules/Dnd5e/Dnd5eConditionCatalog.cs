namespace InitiativeTracker.Web.Rules.Dnd5e;

public static class Dnd5eConditionCatalog
{
    public static IReadOnlyList<string> StandardConditions
    {
        get;
    } =
    [
        "Blinded",
        "Charmed",
        "Deafened",
        "Frightened",
        "Grappled",
        "Incapacitated",
        "Invisible",
        "Paralyzed",
        "Petrified",
        "Poisoned",
        "Prone",
        "Restrained",
        "Stunned",
        "Unconscious"
    ];

    private static readonly IReadOnlyDictionary<string, string> StyleKeys =
        new Dictionary<string, string>(
            StringComparer.OrdinalIgnoreCase)
        {
            ["Blinded"] = "blinded",
            ["Charmed"] = "charmed",
            ["Deafened"] = "deafened",
            ["Frightened"] = "frightened",
            ["Grappled"] = "grappled",
            ["Incapacitated"] = "incapacitated",
            ["Invisible"] = "invisible",
            ["Paralyzed"] = "paralyzed",
            ["Petrified"] = "petrified",
            ["Poisoned"] = "poisoned",
            ["Prone"] = "prone",
            ["Restrained"] = "restrained",
            ["Stunned"] = "stunned",
            ["Unconscious"] = "unconscious"
        };


    public static string GetStyleKey(
        string? conditionName)
    {
        if (string.IsNullOrWhiteSpace(
                conditionName))
        {
            return "custom";
        }

        return StyleKeys.TryGetValue(
            conditionName.Trim(),
            out var styleKey)
                ? styleKey
                : "custom";
    }
}