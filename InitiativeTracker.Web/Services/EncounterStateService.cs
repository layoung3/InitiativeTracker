using InitiativeTracker.Core.Enums;
using InitiativeTracker.Core.Models;
using InitiativeTracker.Web.Data;
using InitiativeTracker.Web.Models;
using InitiativeTracker.Web.Rules.Dnd5e;
using Microsoft.EntityFrameworkCore;
using System.Text.Json;
using static InitiativeTracker.Web.Models.CombatantDisplayState;

namespace InitiativeTracker.Web.Services;

public class EncounterStateService
{
    private readonly object _lock = new();

    private const int MaxUndoHistory = 50;

    private static readonly HashSet<string> AllowedDisplayThemes =
        new(StringComparer.OrdinalIgnoreCase)
        {
            "dark",
            "light",
            "parchment",
            "arcane",
            "infernal",
            "frost",
            "forest",
            "blood-moon"
        };

    private string _currentDisplayTheme = "dark";

    private readonly List<EncounterSnapshot> _undoHistory = [];

    private readonly IDbContextFactory<InitiativeTrackerDbContext> _dbContextFactory;

    private readonly CharacterService _characterService;

    private static readonly JsonSerializerOptions JsonOptions =
        new(JsonSerializerDefaults.Web);

    public EncounterStateService(
    IDbContextFactory<InitiativeTrackerDbContext> dbContextFactory,
    CharacterService characterService)
    {
        _dbContextFactory =
            dbContextFactory;

        _characterService =
            characterService;

        LoadPersistedState();
        LoadApplicationSettings();
    }

    public Encounter CurrentEncounter { get; private set; } = new()
    {
        Name = "Current Encounter"
    };

    public string CurrentDisplayTheme
    {
        get
        {
            lock (_lock)
            {
                return _currentDisplayTheme;
            }
        }
    }

    public string SetDisplayTheme(
        string theme)
    {
        var normalized =
            theme?
                .Trim()
                .ToLowerInvariant();

        if (string.IsNullOrWhiteSpace(normalized) ||
            !AllowedDisplayThemes.Contains(normalized))
        {
            throw new ArgumentException(
                "Unknown display theme.",
                nameof(theme));
        }

        lock (_lock)
        {
            if (string.Equals(
                    _currentDisplayTheme,
                    normalized,
                    StringComparison.Ordinal))
            {
                return _currentDisplayTheme;
            }

            var previousTheme =
                _currentDisplayTheme;

            _currentDisplayTheme =
                normalized;

            try
            {
                PersistApplicationSettingsLocked();
            }
            catch
            {
                _currentDisplayTheme =
                    previousTheme;

                throw;
            }

            return _currentDisplayTheme;
        }
    }

    public bool CanUndo
    {
        get
        {
            lock (_lock)
            {
                return _undoHistory.Count > 0;
            }
        }
    }

    private void LoadApplicationSettings()
    {
        using var db =
            _dbContextFactory.CreateDbContext();

        var settings =
            db.ApplicationSettings
                .AsNoTracking()
                .SingleOrDefault(
                    x => x.Id == 1);

        if (settings is null)
        {
            return;
        }

        var normalized =
            settings.DisplayTheme?
                .Trim()
                .ToLowerInvariant();

        if (!string.IsNullOrWhiteSpace(normalized) &&
            AllowedDisplayThemes.Contains(normalized))
        {
            _currentDisplayTheme =
                normalized;
        }
    }

    private void PersistApplicationSettingsLocked()
    {
        using var db =
            _dbContextFactory.CreateDbContext();

        var settings =
            db.ApplicationSettings
                .SingleOrDefault(
                    x => x.Id == 1);

        if (settings is null)
        {
            settings =
                new ApplicationSettingsRecord
                {
                    Id = 1
                };

            db.ApplicationSettings.Add(
                settings);
        }

        settings.DisplayTheme =
            _currentDisplayTheme;

        settings.UpdatedAtUtc =
            DateTime.UtcNow;

        db.SaveChanges();
    }

    private void HydrateLegacyExhaustion(EncounterSnapshot snapshot)
    {
        foreach (var combatant in snapshot.Combatants.Where(c => !c.ExhaustionLevel.HasValue))
            combatant.ExhaustionLevel = combatant.PlayerCharacterId is Guid characterId
                ? _characterService.GetCharacter(characterId)?.ExhaustionLevel ?? 0
                : 0;
    }

    private void LoadPersistedState()
    {
        using var db =
            _dbContextFactory.CreateDbContext();

        var persisted =
            db.EncounterStates
                .AsNoTracking()
                .SingleOrDefault(
                    x => x.Id == 1);

        if (persisted is null)
        {
            return;
        }

        try
        {
            var encounterSnapshot =
                JsonSerializer.Deserialize<EncounterSnapshot>(
                    persisted.EncounterJson,
                    JsonOptions);

            if (encounterSnapshot is not null)
            {
                HydrateLegacyExhaustion(encounterSnapshot);
                CurrentEncounter =
                    Encounter.FromSnapshot(
                        encounterSnapshot);
            }

            var undoHistory =
                JsonSerializer.Deserialize<
                    List<EncounterSnapshot>>(
                        persisted.UndoHistoryJson,
                        JsonOptions);

            if (undoHistory is not null)
            {
                foreach (var snapshot in undoHistory) HydrateLegacyExhaustion(snapshot);
                _undoHistory.Clear();

                _undoHistory.AddRange(
                    undoHistory.TakeLast(
                        MaxUndoHistory));
            }
        }
        catch (JsonException ex)
        {
            throw new InvalidOperationException(
                "The saved encounter state could not be loaded.",
                ex);
        }
    }

    private void PersistStateLocked()
    {
        var encounterJson =
            JsonSerializer.Serialize(
                CurrentEncounter.CreateSnapshot(),
                JsonOptions);

        var undoHistoryJson =
            JsonSerializer.Serialize(
                _undoHistory,
                JsonOptions);

        using var db =
            _dbContextFactory.CreateDbContext();

        var persisted =
            db.EncounterStates
                .SingleOrDefault(
                    x => x.Id == 1);

        if (persisted is null)
        {
            persisted =
                new PersistedEncounterState
                {
                    Id = 1
                };

            db.EncounterStates.Add(
                persisted);
        }

        persisted.EncounterJson =
            encounterJson;

        persisted.UndoHistoryJson =
            undoHistoryJson;

        persisted.UpdatedAtUtc =
            DateTime.UtcNow;

        db.SaveChanges();
    }

    private void ExecuteMutation(
        Action mutation)
    {
        lock (_lock)
        {
            var encounterBefore =
                CurrentEncounter.CreateSnapshot();

            var historyBefore =
                _undoHistory.ToList();

            mutation();

            _undoHistory.Add(
                encounterBefore);

            if (_undoHistory.Count >
                MaxUndoHistory)
            {
                _undoHistory.RemoveAt(0);
            }

            try
            {
                PersistStateLocked();
            }
            catch
            {
                CurrentEncounter =
                    Encounter.FromSnapshot(
                        encounterBefore);

                _undoHistory.Clear();

                _undoHistory.AddRange(
                    historyBefore);

                throw;
            }

            SyncAllLinkedCharacters();
        }
    }

    public void AddPlayerCharacter(
        Guid characterId,
        bool isHiddenFromPublic = false,
        bool isHpHiddenFromPublic = false,
        Guid? submittingPlayerProfileId = null)
    {
        lock (_lock)
        {
            var character =
                _characterService.GetCharacter(
                    characterId)
                ?? throw new ArgumentException(
                    "Character was not found.",
                    nameof(characterId));

            var characterConditions =
                _characterService
                    .GetConditions(
                        character.Id);

            ExecuteMutation(() =>
            {
                if (submittingPlayerProfileId.HasValue &&
                    (CurrentEncounter.IsStarted || character.PlayerProfileId != submittingPlayerProfileId.Value))
                    throw new InvalidOperationException("Only your own characters can be added during encounter setup.");

                if (CurrentEncounter.Combatants.Any(
                        x =>
                            x.PlayerCharacterId ==
                            character.Id))
                {
                    throw new InvalidOperationException(
                        "This character is already in the encounter.");
                }

                CurrentEncounter.AddCombatant(
                    new Combatant
                    {
                        IsHiddenFromPublic = isHiddenFromPublic,
                        IsHpHiddenFromPublic = isHpHiddenFromPublic,
                        ExhaustionLevel = character.ExhaustionLevel,
                        Name =
                        character.Name,

                        Initiative =
                        0,

                        CurrentHp =
                        character.CurrentHp,

                        MaxHp =
                        character.MaxHp,

                        TemporaryHp =
                        character.TemporaryHp,

                        PlayerCharacterId =
                        character.Id,

                        AssignedPlayerProfileId =
                        character.PlayerProfileId,

                        Conditions = characterConditions
                            .Select(
                                condition =>
                                    new CombatantCondition
                                    {
                                        Id = condition.Id,
                                        Name = condition.Name,
                                        IsCustom = condition.IsCustom,
                                        Notes = condition.Notes
                                    }).ToList()
                    });
            });
        }
    }

    public void AddCombatants(
        string name,
        int initiative,
        int? currentHp,
        int? maxHp,
        int quantity,
        bool isHiddenFromPublic = false,
        bool isHpHiddenFromPublic = false)
    {
        if (quantity < 1 ||
            quantity > 50)
        {
            throw new ArgumentOutOfRangeException(
                nameof(quantity),
                "Quantity must be between 1 and 50.");
        }

        ExecuteMutation(() =>
        {
            var baseName =
                name.Trim();

            for (var i = 0; i < quantity; i++)
            {
                var combatantName =
                    quantity == 1
                        ? baseName
                        : $"{baseName} {i + 1}";

                CurrentEncounter.AddCombatant(
                    new Combatant
                    {
                        IsHiddenFromPublic = isHiddenFromPublic,
                        IsHpHiddenFromPublic = isHpHiddenFromPublic,
                        Name = combatantName,
                        Initiative = initiative,
                        CurrentHp = currentHp,
                        MaxHp = maxHp
                    });
            }
        });
    }

    public void AssignCombatantController(
        Guid combatantId,
        Guid? playerProfileId)
    {
        ExecuteMutation(() =>
            CurrentEncounter
                .AssignCombatantController(
                    combatantId,
                    playerProfileId));
    }

    public void ClearControllerAssignments(
        Guid playerProfileId)
    {
        ExecuteMutation(() =>
            CurrentEncounter
                .ClearControllerAssignments(
                    playerProfileId));
    }

    public void UpdateCombatant(
        Guid combatantId,
        string name,
        int initiative,
        int? currentHp,
        int? maxHp)
    {
        ExecuteMutation(() =>
            CurrentEncounter.UpdateCombatant(
                combatantId,
                name,
                initiative,
                currentHp,
                maxHp));
    }

    public void DeleteCombatant(
        Guid combatantId)
    {
        ExecuteMutation(() =>
            CurrentEncounter.DeleteCombatant(
                combatantId));
    }

    public void ReorderCombatants(
        IReadOnlyList<Guid> orderedIds)
    {
        ExecuteMutation(() =>
            CurrentEncounter.ReorderCombatants(
                orderedIds));
    }

    public void MoveCombatant(
        Guid combatantId,
        int direction)
    {
        ExecuteMutation(() =>
            CurrentEncounter.MoveCombatant(
                combatantId,
                direction));
    }

    public void StartCombat()
    {
        ExecuteMutation(
            CurrentEncounter.StartCombat);
    }

    public void AdvanceTurn()
    {
        ExecuteMutation(
            CurrentEncounter.AdvanceTurn);
    }

    public void PreviousTurn()
    {
        ExecuteMutation(
            CurrentEncounter.PreviousTurn);
    }

    public void ResetEncounter()
    {
        ExecuteMutation(() =>
        {
            CurrentEncounter = new Encounter
            {
                Name = "Current Encounter"
            };
        });
    }

    public EncounterDisplayState GetDisplayState()
    {
        lock (_lock)
        {
            var publicCombatants =
                CurrentEncounter.Combatants
                    .Where(c => c.IsActive)
                    .Where(c =>
                        !c.IsHiddenFromPublic ||
                        c.AssignedPlayerProfileId.HasValue);

            var orderedCombatants =
                CurrentEncounter.IsStarted
                    ? publicCombatants
                        .OrderBy(c => c.SortOrder)
                        .ToList()
                    : publicCombatants
                        .OrderByDescending(c => c.Initiative)
                        .ThenBy(c => c.SortOrder)
                        .ToList();

            var currentTurnNumber = 0;

            if (CurrentEncounter.CurrentCombatantId.HasValue)
            {
                currentTurnNumber =
                    orderedCombatants.FindIndex(
                        c =>
                            c.Id ==
                            CurrentEncounter
                                .CurrentCombatantId
                                .Value)
                    + 1;
            }

            return new EncounterDisplayState
            {
                IsStarted = CurrentEncounter.IsStarted,

                Round = CurrentEncounter.Round,

                CurrentTurnNumber = currentTurnNumber,

                ActiveCombatantCount = orderedCombatants.Count,

                Combatants =
                    orderedCombatants
                        .Select(c =>
                        {
                            var isPlayerControlled = c.AssignedPlayerProfileId.HasValue;

                            var hideHp = c.IsHpHiddenFromPublic && !isPlayerControlled;

                            return new CombatantDisplayState
                            {
                                PortraitUrl = MediaPaths.CharacterPortrait(c.PlayerCharacterId),
                                Id = c.Id,
                                Name = c.Name,
                                ExhaustionLevel = c.ExhaustionLevel,
                                Initiative = c.Initiative,
                                CurrentHp = hideHp ? null : c.CurrentHp,
                                MaxHp = hideHp ? null : c.MaxHp,
                                TemporaryHp = hideHp ? null : c.TemporaryHp,
                                IsActive = c.IsActive,
                                IsCurrent = CurrentEncounter.CurrentCombatantId == c.Id,
                                Conditions = c.Conditions
                                    .OrderBy(x => x.Name)
                                    .Select(
                                        condition =>
                                            new ConditionDisplayState
                                            {
                                                Name =
                                                    condition.Name,

                                                StyleKey =
                                                    Dnd5eConditionCatalog
                                                        .GetStyleKey(
                                                            condition.Name)
                                            })
                                    .ToList(),
                            };
                        })
                        .ToList()
            };
        }
    }

    public void AdjustHp(
        Guid combatantId,
        int amount)
    {
        ExecuteMutation(() =>
            CurrentEncounter.AdjustHp(
                combatantId,
                amount));
    }

    public void SetCurrentHp(
        Guid combatantId,
        int currentHp)
    {
        ExecuteMutation(() =>
            CurrentEncounter
                .SetCurrentHp(
                    combatantId,
                    currentHp));
    }

    public void SetTemporaryHp(
        Guid combatantId,
        int temporaryHp)
    {
        ExecuteMutation(() =>
            CurrentEncounter
                .SetTemporaryHp(
                    combatantId,
                    temporaryHp));
    }

    public void SetCombatantStatus(
        Guid combatantId,
        CombatantStatus status)
    {
        ExecuteMutation(() =>
            CurrentEncounter.SetCombatantStatus(
                combatantId,
                status));
    }

    public bool Undo()
    {
        lock (_lock)
        {
            if (_undoHistory.Count == 0)
            {
                return false;
            }

            var encounterBefore =
                CurrentEncounter.CreateSnapshot();

            var historyBefore =
                _undoHistory.ToList();

            var lastIndex =
                _undoHistory.Count - 1;

            var snapshot =
                _undoHistory[lastIndex];

            _undoHistory.RemoveAt(
                lastIndex);

            CurrentEncounter =
                Encounter.FromSnapshot(
                    snapshot);

            try
            {
                PersistStateLocked();
            }
            catch
            {
                CurrentEncounter =
                    Encounter.FromSnapshot(
                        encounterBefore);

                _undoHistory.Clear();

                _undoHistory.AddRange(
                    historyBefore);

                throw;
            }

            SyncAllLinkedCharacters();

            return true;
        }
    }

    public void AdjustHpForController(
        Guid playerProfileId,
        Guid combatantId,
        int amount)
    {
        ExecuteMutation(() =>
        {
            var combatant =
                CurrentEncounter.GetCombatant(
                    combatantId);

            EnsureControlledBy(
                combatant,
                playerProfileId);

            CurrentEncounter.AdjustHp(
                combatantId,
                amount);
        });
    }

    public void SetCurrentHpForController(
        Guid playerProfileId,
        Guid combatantId,
        int currentHp)
    {
        ExecuteMutation(() =>
        {
            var combatant =
                CurrentEncounter.GetCombatant(
                    combatantId);

            EnsureControlledBy(
                combatant,
                playerProfileId);

            CurrentEncounter.SetCurrentHp(
                combatantId,
                currentHp);
        });
    }

    public void SetTemporaryHpForController(
        Guid playerProfileId,
        Guid combatantId,
        int temporaryHp)
    {
        ExecuteMutation(() =>
        {
            var combatant =
                CurrentEncounter.GetCombatant(
                    combatantId);

            EnsureControlledBy(
                combatant,
                playerProfileId);

            CurrentEncounter.SetTemporaryHp(
                combatantId,
                temporaryHp);
        });
    }

    public void AdvanceTurnForController(
        Guid playerProfileId)
    {
        ExecuteMutation(() =>
        {
            if (!CurrentEncounter.IsStarted)
            {
                throw new InvalidOperationException(
                    "Combat has not started.");
            }

            var current =
                CurrentEncounter.CurrentCombatant
                ?? throw new InvalidOperationException(
                    "There is no current combatant.");

            EnsureControlledBy(
                current,
                playerProfileId);

            CurrentEncounter.AdvanceTurn();
        });
    }

    public void SetPlayerCharacterInitiative(
        Guid characterId,
        int initiative)
    {
        ExecuteMutation(() =>
        {
            var combatant =
                CurrentEncounter.Combatants
                    .SingleOrDefault(
                        x =>
                            x.PlayerCharacterId ==
                            characterId)
                ?? throw new InvalidOperationException(
                    "Character is not part of the current encounter.");

            CurrentEncounter
                .SetCombatantInitiative(
                    combatant.Id,
                    initiative);
        });
    }

    public void SetCharacterExhaustion(Guid characterId, int level)
    {
        lock (_lock)
        {
            var linked = CurrentEncounter.Combatants.Where(c => c.PlayerCharacterId == characterId).ToList();
            if (linked.Count == 0)
            {
                _characterService.SetCharacterExhaustion(characterId, level);
                return;
            }
            ExecuteMutation(() =>
            {
                foreach (var combatant in linked)
                    combatant.ExhaustionLevel = Math.Clamp(level, 0, 6);
            });
        }
    }

    public void SetCombatantVisibility(
        Guid combatantId,
        bool isHiddenFromPublic,
        bool isHpHiddenFromPublic)
    {
        ExecuteMutation(() =>
            CurrentEncounter
                .SetCombatantVisibility(
                    combatantId,
                    isHiddenFromPublic,
                    isHpHiddenFromPublic));
    }

    private static void EnsureControlledBy(
        Combatant combatant,
        Guid playerProfileId)
    {
        if (combatant.AssignedPlayerProfileId !=
            playerProfileId)
        {
            throw new InvalidOperationException(
                "You do not control this combatant.");
        }
    }

    private void SyncLinkedCharacterState(
        Combatant combatant)
    {
        if (!combatant.PlayerCharacterId.HasValue ||
            !combatant.CurrentHp.HasValue ||
            !combatant.MaxHp.HasValue)
        {
            return;
        }


        var conditions =
            combatant.Conditions
                .Select(
                    condition =>
                        new PlayerCharacterCondition
                        {
                            Id = condition.Id,
                            PlayerCharacterId = combatant.PlayerCharacterId.Value,
                            Name = condition.Name,
                            IsCustom = condition.IsCustom,
                            Notes = condition.Notes
                        }).ToList();


        _characterService
            .UpdateEncounterState(
                combatant
                    .PlayerCharacterId
                    .Value,
                combatant.CurrentHp.Value,
                combatant.MaxHp.Value,
                combatant.TemporaryHp,
                conditions, combatant.ExhaustionLevel);
    }

    private void SyncAllLinkedCharacters()
    {
        foreach (var combatant
                 in CurrentEncounter.Combatants)
        {
            SyncLinkedCharacterState(combatant);
        }
    }

    public void AddCombatantCondition(
        Guid combatantId,
        string name,
        bool isCustom,
        string? notes = null)
    {
        ExecuteMutation(() =>
            CurrentEncounter
                .AddCombatantCondition(
                    combatantId,
                    new CombatantCondition
                    {
                        Name = name,
                        IsCustom = isCustom,
                        Notes = notes
                    }));
    }

    public void RemoveCombatantCondition(
        Guid combatantId,
        Guid conditionId)
    {
        ExecuteMutation(() =>
            CurrentEncounter
                .RemoveCombatantCondition(
                    combatantId,
                    conditionId));
    }

    public void AddConditionForController(
        Guid playerProfileId,
        Guid combatantId,
        string name,
        bool isCustom,
        string? notes = null)
    {
        ExecuteMutation(() =>
        {
            var combatant =
                CurrentEncounter
                    .GetCombatant(
                        combatantId);

            EnsureControlledBy(
                combatant,
                playerProfileId);

            CurrentEncounter
                .AddCombatantCondition(
                    combatantId,
                    new CombatantCondition
                    {
                        Name = name,
                        IsCustom = isCustom,
                        Notes = notes
                    });
        });
    }

    public void RemoveConditionForController(
        Guid playerProfileId,
        Guid combatantId,
        Guid conditionId)
    {
        ExecuteMutation(() =>
        {
            var combatant =
                CurrentEncounter
                    .GetCombatant(
                        combatantId);

            EnsureControlledBy(
                combatant,
                playerProfileId);

            CurrentEncounter
                .RemoveCombatantCondition(
                    combatantId,
                    conditionId);
        });
    }
}
