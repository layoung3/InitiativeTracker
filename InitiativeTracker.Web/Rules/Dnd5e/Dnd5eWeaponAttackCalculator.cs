using InitiativeTracker.Web.Data;
using InitiativeTracker.Web.Data.Enums;

namespace InitiativeTracker.Web.Rules.Dnd5e;

public static class Dnd5eWeaponAttackCalculator
{
    public static Dnd5eAttackView Calculate(
        PlayerCharacter character,
        PlayerCharacterWeapon weapon)
    {
        ArgumentNullException.ThrowIfNull(
            character);

        ArgumentNullException.ThrowIfNull(
            weapon);


        var abilityScore =
            GetAbilityScore(
                character,
                weapon.AttackAbility);

        var abilityModifier =
            GetAbilityModifier(
                abilityScore);


        var toHitBonus =
            abilityModifier +
            weapon.AttackBonus;


        if (weapon.UsesProficiency)
        {
            toHitBonus +=
                character.ProficiencyBonus;
        }


        var damageComponents =
            weapon.DamageComponents
                .OrderBy(
                    x => x.SortOrder)
                .Select(
                    component =>
                    {
                        var flatBonus = 0;


                        if (component.AddsAbilityModifier)
                        {
                            flatBonus +=
                                abilityModifier;
                        }


                        if (component.IsPrimary)
                        {
                            flatBonus +=
                                weapon.PrimaryDamageBonus;
                        }


                        return new Dnd5eAttackDamageView
                        {
                            DamageExpression =
                                component.DamageDice,

                            DamageType =
                                component.DamageType,

                            FlatBonus =
                                flatBonus,

                            IsPrimary =
                                component.IsPrimary,

                            VersatileDamageExpression =
                                component.VersatileDamageDice
                        };
                    })
                .ToList();


        return new Dnd5eAttackView
        {
            SourceId =
                weapon.PlayerCharacterItemId,

            SourceType =
                Dnd5eAttackSourceType.Weapon,

            Name =
                weapon.Item.Name,

            AttackAbility =
                weapon.AttackAbility,

            ToHitBonus =
                toHitBonus,

            NormalRange =
                weapon.NormalRange,

            LongRange =
                weapon.LongRange,

            DamageComponents =
                damageComponents
        };
    }


    public static int GetAbilityScore(
        PlayerCharacter character,
        CharacterAbilityType ability)
    {
        return ability switch
        {
            CharacterAbilityType.Strength =>
                character.Strength,

            CharacterAbilityType.Dexterity =>
                character.Dexterity,

            CharacterAbilityType.Constitution =>
                character.Constitution,

            CharacterAbilityType.Intelligence =>
                character.Intelligence,

            CharacterAbilityType.Wisdom =>
                character.Wisdom,

            CharacterAbilityType.Charisma =>
                character.Charisma,

            _ =>
                throw new ArgumentOutOfRangeException(
                    nameof(ability),
                    ability,
                    null)
        };
    }


    public static int GetAbilityModifier(
        int score)
    {
        return (int)Math.Floor(
            (score - 10) / 2.0);
    }
}