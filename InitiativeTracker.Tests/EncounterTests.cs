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

    [Fact]
    public void PreviousTurn_MovesToPreviousCombatant()
    {
        var encounter = CreateEncounter();

        encounter.StartCombat();
        encounter.AdvanceTurn();

        Assert.Equal(
            "Dragon",
            encounter.CurrentCombatant?.Name);

        encounter.PreviousTurn();

        Assert.Equal(
            "Fijt",
            encounter.CurrentCombatant?.Name);

        Assert.Equal(1, encounter.Round);
    }

    [Fact]
    public void PreviousTurn_FromFirstCombatant_MovesToPreviousRound()
    {
        var encounter = CreateEncounter();

        encounter.StartCombat();

        encounter.AdvanceTurn();
        encounter.AdvanceTurn();
        encounter.AdvanceTurn();

        Assert.Equal(2, encounter.Round);
        Assert.Equal(
            "Fijt",
            encounter.CurrentCombatant?.Name);

        encounter.PreviousTurn();

        Assert.Equal(1, encounter.Round);
        Assert.Equal(
            "Penny",
            encounter.CurrentCombatant?.Name);
    }

    [Fact]
    public void PreviousTurn_OnFirstTurnOfRoundOne_DoesNothing()
    {
        var encounter = CreateEncounter();

        encounter.StartCombat();
        encounter.PreviousTurn();

        Assert.Equal(1, encounter.Round);
        Assert.Equal(
            "Fijt",
            encounter.CurrentCombatant?.Name);
    }

    [Fact]
    public void Snapshot_RestoresEncounterState()
    {
        var encounter = CreateEncounter();

        encounter.StartCombat();

        var dragon = encounter.Combatants
            .Single(c => c.Name == "Dragon");

        var snapshot =
            encounter.CreateSnapshot();

        encounter.AdjustHp(
            dragon.Id,
            -20);

        encounter.SetCombatantStatus(
            dragon.Id,
            CombatantStatus.Defeated);

        var restored =
            Encounter.FromSnapshot(snapshot);

        var restoredDragon =
            restored.Combatants
                .Single(c => c.Name == "Dragon");

        Assert.Equal(61, restoredDragon.CurrentHp);
        Assert.Equal(
            CombatantStatus.Active,
            restoredDragon.Status);

        Assert.Equal(
            "Fijt",
            restored.CurrentCombatant?.Name);
    }

    [Fact]
    public void ReorderCombatants_ChangesInitiativeOrder()
    {
        var encounter = CreateEncounter();

        encounter.StartCombat();

        var fijt = encounter.Combatants
            .Single(c => c.Name == "Fijt");

        var dragon = encounter.Combatants
            .Single(c => c.Name == "Dragon");

        var penny = encounter.Combatants
            .Single(c => c.Name == "Penny");

        encounter.ReorderCombatants(
        [
            penny.Id,
            dragon.Id,
            fijt.Id
        ]);

        var ordered = encounter.Combatants
            .OrderBy(c => c.SortOrder)
            .Select(c => c.Name)
            .ToList();

        Assert.Equal(
            ["Penny", "Dragon", "Fijt"],
            ordered);
    }

    [Fact]
    public void ReorderCombatants_PreservesCurrentCombatant()
    {
        var encounter = CreateEncounter();

        encounter.StartCombat();
        encounter.AdvanceTurn();

        Assert.Equal(
            "Dragon",
            encounter.CurrentCombatant?.Name);

        var fijt = encounter.Combatants
            .Single(c => c.Name == "Fijt");

        var dragon = encounter.Combatants
            .Single(c => c.Name == "Dragon");

        var penny = encounter.Combatants
            .Single(c => c.Name == "Penny");

        encounter.ReorderCombatants(
        [
            penny.Id,
            dragon.Id,
            fijt.Id
        ]);

        Assert.Equal(
            "Dragon",
            encounter.CurrentCombatant?.Name);
    }

    [Fact]
    public void AdvanceTurn_AfterReorder_FollowsNewOrder()
    {
        var encounter = CreateEncounter();

        encounter.StartCombat();
        encounter.AdvanceTurn();

        var fijt = encounter.Combatants
            .Single(c => c.Name == "Fijt");

        var dragon = encounter.Combatants
            .Single(c => c.Name == "Dragon");

        var penny = encounter.Combatants
            .Single(c => c.Name == "Penny");

        encounter.ReorderCombatants(
        [
            penny.Id,
            dragon.Id,
            fijt.Id
        ]);

        encounter.AdvanceTurn();

        Assert.Equal(
            "Fijt",
            encounter.CurrentCombatant?.Name);
    }

    [Fact]
    public void ReorderCombatants_MissingCombatant_ThrowsException()
    {
        var encounter = CreateEncounter();

        encounter.StartCombat();

        var ids = encounter.Combatants
            .Take(2)
            .Select(c => c.Id)
            .ToList();

        Assert.Throws<ArgumentException>(
            () => encounter.ReorderCombatants(ids));
    }

    [Fact]
    public void AddCombatant_WithMaxHpAndNoCurrentHp_StartsAtMaxHp()
    {
        var encounter = new Encounter();

        encounter.AddCombatant(
            new Combatant
            {
                Name = "Goblin",
                Initiative = 10,
                MaxHp = 12
            });

        Assert.Equal(
            12,
            encounter.Combatants[0].CurrentHp);
    }

    [Fact]
    public void UpdateCombatant_ChangesSetupValues()
    {
        var encounter = CreateEncounter();

        var dragon = encounter.Combatants
            .Single(c => c.Name == "Dragon");

        encounter.UpdateCombatant(
            dragon.Id,
            "Dragon Updated",
            25,
            50,
            70);

        Assert.Equal(
            "Dragon Updated",
            dragon.Name);

        Assert.Equal(
            25,
            dragon.Initiative);

        Assert.Equal(
            50,
            dragon.CurrentHp);

        Assert.Equal(
            70,
            dragon.MaxHp);
    }

    [Fact]
    public void UpdateCombatant_AfterCombatStarts_ThrowsException()
    {
        var encounter = CreateEncounter();

        encounter.StartCombat();

        var dragon = encounter.Combatants
            .Single(c => c.Name == "Dragon");

        Assert.Throws<InvalidOperationException>(
            () => encounter.UpdateCombatant(
                dragon.Id,
                "Dragon",
                20,
                50,
                60));
    }

    [Fact]
    public void DeleteCombatant_RemovesCombatant()
    {
        var encounter = CreateEncounter();

        var dragon = encounter.Combatants
            .Single(c => c.Name == "Dragon");

        encounter.DeleteCombatant(
            dragon.Id);

        Assert.DoesNotContain(
            encounter.Combatants,
            c => c.Id == dragon.Id);

        Assert.Equal(
            2,
            encounter.Combatants.Count);
    }

    [Fact]
    public void AssignCombatantController_AssignsPlayer()
    {
        var encounter =
            CreateEncounter();

        var combatant =
            encounter.Combatants.First();

        var playerProfileId =
            Guid.NewGuid();

        encounter.AssignCombatantController(
            combatant.Id,
            playerProfileId);

        Assert.Equal(
            playerProfileId,
            combatant.AssignedPlayerProfileId);
    }

    [Fact]
    public void AssignCombatantController_CanReassignPlayer()
    {
        var encounter =
            CreateEncounter();

        var combatant =
            encounter.Combatants.First();

        var firstPlayer =
            Guid.NewGuid();

        var secondPlayer =
            Guid.NewGuid();

        encounter.AssignCombatantController(
            combatant.Id,
            firstPlayer);

        encounter.AssignCombatantController(
            combatant.Id,
            secondPlayer);

        Assert.Equal(
            secondPlayer,
            combatant.AssignedPlayerProfileId);
    }

    [Fact]
    public void AssignCombatantController_NullReturnsControlToDm()
    {
        var encounter =
            CreateEncounter();

        var combatant =
            encounter.Combatants.First();

        encounter.AssignCombatantController(
            combatant.Id,
            Guid.NewGuid());

        encounter.AssignCombatantController(
            combatant.Id,
            null);

        Assert.Null(
            combatant.AssignedPlayerProfileId);
    }

    [Fact]
    public void ClearControllerAssignments_ReturnsPlayersCombatantsToDm()
    {
        var encounter =
            CreateEncounter();

        var player =
            Guid.NewGuid();

        encounter.AssignCombatantController(
            encounter.Combatants[0].Id,
            player);

        encounter.AssignCombatantController(
            encounter.Combatants[1].Id,
            player);

        encounter.ClearControllerAssignments(
            player);

        Assert.All(
            encounter.Combatants,
            combatant =>
                Assert.Null(
                    combatant.AssignedPlayerProfileId));
    }

    [Fact]
    public void SetCombatantVisibility_CanHideCombatant()
    {
        var encounter =
            CreateEncounter();

        var combatant =
            encounter.Combatants.First();

        encounter.SetCombatantVisibility(
            combatant.Id,
            true,
            false);

        Assert.True(
            combatant.IsHiddenFromPublic);
    }

    [Fact]
    public void SetCombatantVisibility_CanHideHp()
    {
        var encounter =
            CreateEncounter();

        var combatant =
            encounter.Combatants.First();

        encounter.SetCombatantVisibility(
            combatant.Id,
            false,
            true);

        Assert.True(
            combatant.IsHpHiddenFromPublic);
    }

    [Fact]
    public void SetCombatantVisibility_CanHideCombatantAndHp()
    {
        var encounter =
            CreateEncounter();

        var combatant =
            encounter.Combatants.First();

        encounter.SetCombatantVisibility(
            combatant.Id,
            true,
            true);

        Assert.True(
            combatant.IsHiddenFromPublic);

        Assert.True(
            combatant.IsHpHiddenFromPublic);
    }
}