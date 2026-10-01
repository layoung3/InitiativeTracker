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
}