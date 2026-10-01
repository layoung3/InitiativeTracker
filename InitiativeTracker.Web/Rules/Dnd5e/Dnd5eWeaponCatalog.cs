using InitiativeTracker.Web.Data.Enums;

namespace InitiativeTracker.Web.Rules.Dnd5e;

public static class Dnd5eWeaponCatalog
{
    public static IReadOnlyList<Dnd5eWeaponDefinition>
        StandardWeapons
    { get; } =
        [
            new()
            {
                Key = "club",
                Name = "Club",

                DefaultAttackAbility =
                    CharacterAbilityType.Strength,

                AllowedAttackAbilities =
                [
                    CharacterAbilityType.Strength
                ],

                DamageComponents =
                [
                    PrimaryDamage(
                        "1d4",
                        "Bludgeoning")
                ]
            },

            new()
            {
                Key = "dagger",
                Name = "Dagger",

                DefaultAttackAbility =
                    CharacterAbilityType.Strength,

                AllowedAttackAbilities =
                [
                    CharacterAbilityType.Strength,
                    CharacterAbilityType.Dexterity
                ],

                NormalRange = 20,
                LongRange = 60,

                DamageComponents =
                [
                    PrimaryDamage(
                        "1d4",
                        "Piercing")
                ]
            },

            new()
            {
                Key = "spear",
                Name = "Spear",

                DefaultAttackAbility =
                    CharacterAbilityType.Strength,

                AllowedAttackAbilities =
                [
                    CharacterAbilityType.Strength
                ],

                NormalRange = 20,
                LongRange = 60,

                DamageComponents =
                [
                    PrimaryDamage(
                        "1d6",
                        "Piercing",
                        versatileDamageDice: "1d8")
                ]
            },

            new()
            {
                Key = "longsword",
                Name = "Longsword",

                DefaultAttackAbility =
                    CharacterAbilityType.Strength,

                AllowedAttackAbilities =
                [
                    CharacterAbilityType.Strength
                ],

                DamageComponents =
                [
                    PrimaryDamage(
                        "1d8",
                        "Slashing",
                        versatileDamageDice: "1d10")
                ]
            },

            new()
            {
                Key = "rapier",
                Name = "Rapier",

                DefaultAttackAbility =
                    CharacterAbilityType.Strength,

                AllowedAttackAbilities =
                [
                    CharacterAbilityType.Strength,
                    CharacterAbilityType.Dexterity
                ],

                DamageComponents =
                [
                    PrimaryDamage(
                        "1d8",
                        "Piercing")
                ]
            },

            new()
            {
                Key = "shortbow",
                Name = "Shortbow",

                DefaultAttackAbility =
                    CharacterAbilityType.Dexterity,

                AllowedAttackAbilities =
                [
                    CharacterAbilityType.Dexterity
                ],

                NormalRange = 80,
                LongRange = 320,

                DamageComponents =
                [
                    PrimaryDamage(
                        "1d6",
                        "Piercing")
                ]
            },

            new()
            {
                Key = "longbow",
                Name = "Longbow",

                DefaultAttackAbility =
                    CharacterAbilityType.Dexterity,

                AllowedAttackAbilities =
                [
                    CharacterAbilityType.Dexterity
                ],

                NormalRange = 150,
                LongRange = 600,

                DamageComponents =
                [
                    PrimaryDamage(
                        "1d8",
                        "Piercing")
                ]
            }
        ];


    public static Dnd5eWeaponDefinition?
        GetByKey(
            string key)
    {
        if (string.IsNullOrWhiteSpace(
                key))
        {
            return null;
        }

        return StandardWeapons
            .FirstOrDefault(
                x =>
                    string.Equals(
                        x.Key,
                        key.Trim(),
                        StringComparison.OrdinalIgnoreCase));
    }


    private static Dnd5eWeaponDamageDefinition
        PrimaryDamage(
            string damageDice,
            string damageType,
            string? versatileDamageDice = null)
    {
        return new Dnd5eWeaponDamageDefinition
        {
            DamageDice =
                damageDice,

            DamageType =
                damageType,

            IsPrimary =
                true,

            AddsAbilityModifier =
                true,

            VersatileDamageDice =
                versatileDamageDice,

            SortOrder =
                0
        };
    }
}