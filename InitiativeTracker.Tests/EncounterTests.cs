using InitiativeTracker.Core.Enums;
using InitiativeTracker.Core.Models;

namespace InitiativeTracker.Tests;

public class EncounterTests
{
    [Fact]
    public void StartCombat_HighestInitiativeGoesFirst()
    {
        var encounter = new Encounter();

        encounter.AddCombatant(new Combatant
        {
            Name = "Penny",
            Initiative = 14
        });

        encounter.AddCombatant(new Combatant
        {
            Name = "Dragon",
            Initiative = 18
        });

        encounter.AddCombatant(new Combatant
        {
            Name = "Fijt",
            Initiative = 21
        });

        encounter.StartCombat();

        Assert.True(encounter.IsStarted);
        Assert.Equal(1, encounter.Round);
        Assert.Equal("Fijt", encounter.CurrentCombatant?.Name);
    }

    [Fact]
    public void StartCombat_TiedInitiativePreservesExistingOrder()
    {
        var encounter = new Encounter();

        encounter.AddCombatant(new Combatant
        {
            Name = "Dragon",
            Initiative = 18
        });

        encounter.AddCombatant(new Combatant
        {
            Name = "Penny",
            Initiative = 18
        });

        encounter.StartCombat();

        Assert.Equal("Dragon", encounter.CurrentCombatant?.Name);
    }

    [Fact]
    public void AdvanceTurn_MovesToNextCombatant()
    {
        var encounter = CreateEncounter();

        encounter.StartCombat();
        encounter.AdvanceTurn();

        Assert.Equal("Dragon", encounter.CurrentCombatant?.Name);
        Assert.Equal(1, encounter.Round);
    }

    [Fact]
    public void AdvanceTurn_FromLastCombatant_StartsNextRound()
    {
        var encounter = CreateEncounter();

        encounter.StartCombat();

        encounter.AdvanceTurn();
        encounter.AdvanceTurn();
        encounter.AdvanceTurn();

        Assert.Equal("Fijt", encounter.CurrentCombatant?.Name);
        Assert.Equal(2, encounter.Round);
    }

    [Fact]
    public void AdvanceTurn_SkipsDefeatedCombatant()
    {
        var encounter = CreateEncounter();

        encounter.StartCombat();

        var dragon = encounter.Combatants
            .Single(c => c.Name == "Dragon");

        dragon.Status = CombatantStatus.Defeated;

        encounter.AdvanceTurn();

        Assert.Equal("Penny", encounter.CurrentCombatant?.Name);
    }

    [Fact]
    public void AdvanceTurn_SkipsRemovedCombatant()
    {
        var encounter = CreateEncounter();

        encounter.StartCombat();

        var dragon = encounter.Combatants
            .Single(c => c.Name == "Dragon");

        dragon.Status = CombatantStatus.Removed;

        encounter.AdvanceTurn();

        Assert.Equal("Penny", encounter.CurrentCombatant?.Name);
    }

    [Fact]
    public void StartCombat_WithNoCombatants_ThrowsException()
    {
        var encounter = new Encounter();

        Assert.Throws<InvalidOperationException>(
            encounter.StartCombat);
    }

    private static Encounter CreateEncounter()
    {
        var encounter = new Encounter();

        encounter.AddCombatant(new Combatant
        {
            Name = "Penny",
            Initiative = 14,
            CurrentHp = 42,
            MaxHp = 42
        });

        encounter.AddCombatant(new Combatant
        {
            Name = "Dragon",
            Initiative = 18,
            CurrentHp = 61,
            MaxHp = 61
        });

        encounter.AddCombatant(new Combatant
        {
            Name = "Fijt",
            Initiative = 21,
            CurrentHp = 31,
            MaxHp = 31
        });

        return encounter;
    }
}