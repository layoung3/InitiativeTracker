using InitiativeTracker.Web.Data;

namespace InitiativeTracker.Web.Rules.Dnd5e;

public static class Dnd5eCharacterAttackCalculator
{
    public static Dnd5eAttackView Calculate(
        PlayerCharacter character,
        PlayerCharacterAttack attack)
    {
        ArgumentNullException.ThrowIfNull(
            character);

        ArgumentNullException.ThrowIfNull(
            attack);


        var abilityScore =
            Dnd5eWeaponAttackCalculator
                .GetAbilityScore(
                    character,
                    attack.AttackAbility);

        var abilityModifier =
            Dnd5eWeaponAttackCalculator
                .GetAbilityModifier(
                    abilityScore);


        var toHitBonus =
            attack.AttackBonus;


        if (attack.AddsAbilityModifierToHit)
        {
            toHitBonus +=
                abilityModifier;
        }


        if (attack.UsesProficiency)
        {
            toHitBonus +=
                character.ProficiencyBonus;
        }


        var damage =
            attack.DamageComponents
                .OrderBy(
                    x => x.SortOrder)
                .Select(
                    x =>
                    {
                        var flatBonus =
                            x.AdditionalDamageBonus;


                        if (x.AddsAbilityModifier)
                        {
                            flatBonus +=
                                abilityModifier;
                        }


                        return new Dnd5eAttackDamageView
                        {
                            DamageExpression =
                                x.DamageExpression,

                            DamageType =
                                x.DamageType,

                            FlatBonus =
                                flatBonus,

                            IsPrimary =
                                false
                        };
                    })
                .ToList();


        return new Dnd5eAttackView
        {
            SourceId =
                attack.Id,

            SourceType =
                Dnd5eAttackSourceType.Custom,

            Name =
                attack.Name,

            AttackAbility =
                attack.AttackAbility,

            ToHitBonus =
                toHitBonus,

            NormalRange =
                attack.NormalRange,

            LongRange =
                attack.LongRange,

            Notes =
                attack.Notes,

            DamageComponents =
                damage
        };
    }
}