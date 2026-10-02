using InitiativeTracker.Web.Data;
using InitiativeTracker.Web.Data.Enums;

namespace InitiativeTracker.Web.Rules.Dnd5e;

public static class Dnd5eSpellcastingCalculator
{
    public static Dnd5eSpellcastingSummaryView
        Calculate(
            PlayerCharacter character,
            PlayerCharacterSpellcasting spellcasting)
    {
        ArgumentNullException.ThrowIfNull(
            character);

        ArgumentNullException.ThrowIfNull(
            spellcasting);


        var abilityScore =
            GetAbilityScore(
                character,
                spellcasting.SpellcastingAbility);

        var abilityModifier =
            GetAbilityModifier(
                abilityScore);


        return new Dnd5eSpellcastingSummaryView
        {
            Ability =
                spellcasting.SpellcastingAbility,

            SpellAttackBonus =
                abilityModifier +
                character.ProficiencyBonus +
                spellcasting.AdditionalSpellAttackBonus,

            SpellSaveDc =
                8 +
                abilityModifier +
                character.ProficiencyBonus +
                spellcasting.AdditionalSpellSaveDcBonus
        };
    }


    private static int GetAbilityScore(
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


    private static int GetAbilityModifier(
        int score)
    {
        return (int)Math.Floor(
            (score - 10) / 2.0);
    }
}