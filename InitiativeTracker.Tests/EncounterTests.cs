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

    [Fact]
    public void AdjustHp_DamageCannotGoBelowZero()
    {
        var encounter = CreateEncounter();

        var penny = encounter.Combatants
            .Single(c => c.Name == "Penny");

        encounter.AdjustHp(
            penny.Id,
            -100);

        Assert.Equal(0, penny.CurrentHp);
    }

    [Fact]
    public void AdjustHp_HealingCannotExceedMaxHp()
    {
        var encounter = CreateEncounter();

        var penny = encounter.Combatants
            .Single(c => c.Name == "Penny");

        penny.CurrentHp = 20;

        encounter.AdjustHp(
            penny.Id,
            100);

        Assert.Equal(42, penny.CurrentHp);
    }

    [Fact]
    public void DefeatingCombatant_RemovesThemFromActiveCount()
    {
        var encounter = CreateEncounter();

        var dragon = encounter.Combatants
            .Single(c => c.Name == "Dragon");

        encounter.SetCombatantStatus(
            dragon.Id,
            CombatantStatus.Defeated);

        Assert.Equal(2, encounter.ActiveCombatantCount);
    }

    [Fact]
    public void DefeatingCurrentCombatant_AdvancesTurn()
    {
        var encounter = CreateEncounter();

        encounter.StartCombat();

        var fijt = encounter.CurrentCombatant!;

        encounter.SetCombatantStatus(
            fijt.Id,
            CombatantStatus.Defeated);

        Assert.Equal(
            "Dragon",
            encounter.CurrentCombatant?.Name);
    }

    [Fact]
    public void RestoreCombatant_ReturnsThemToActiveInitiative()
    {
        var encounter = CreateEncounter();

        var dragon = encounter.Combatants
            .Single(c => c.Name == "Dragon");

        encounter.SetCombatantStatus(
            dragon.Id,
            CombatantStatus.Defeated);

        encounter.SetCombatantStatus(
            dragon.Id,
            CombatantStatus.Active);

        Assert.True(dragon.IsActive);
        Assert.Equal(3, encounter.ActiveCombatantCount);
    }
}