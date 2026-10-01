using InitiativeTracker.Web.Data;

namespace InitiativeTracker.Web.Rules.Dnd5e;

public static class Dnd5eWeaponFactory
{
    public static PlayerCharacterWeapon Create(
        Dnd5eWeaponDefinition definition)
    {
        return new PlayerCharacterWeapon
        {
            AttackAbility =
                definition.DefaultAttackAbility,

            UsesProficiency =
                true,

            AttackBonus =
                0,

            PrimaryDamageBonus =
                0,

            NormalRange =
                definition.NormalRange,

            LongRange =
                definition.LongRange,

            DamageComponents =
                definition
                    .DamageComponents
                    .Select(
                        x =>
                            new PlayerCharacterWeaponDamageComponent
                            {
                                DamageDice =
                                    x.DamageDice,

                                DamageType =
                                    x.DamageType,

                                IsPrimary =
                                    x.IsPrimary,

                                AddsAbilityModifier =
                                    x.AddsAbilityModifier,

                                VersatileDamageDice =
                                    x.VersatileDamageDice,

                                SortOrder =
                                    x.SortOrder
                            })
                    .ToList()
        };
    }
}