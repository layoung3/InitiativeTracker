using Microsoft.EntityFrameworkCore;

namespace InitiativeTracker.Web.Data;

public class InitiativeTrackerDbContext : DbContext
{
    public InitiativeTrackerDbContext(
        DbContextOptions<InitiativeTrackerDbContext> options)
        : base(options)
    {
    }

    public DbSet<PersistedEncounterState> EncounterStates =>
        Set<PersistedEncounterState>();

    public DbSet<GameSessionRecord> GameSessions =>
        Set<GameSessionRecord>();

    public DbSet<RegisteredDevice> RegisteredDevices =>
        Set<RegisteredDevice>();

    public DbSet<DevicePairingRequest> DevicePairingRequests =>
        Set<DevicePairingRequest>();

    public DbSet<PlayerConnection> PlayerConnections =>
        Set<PlayerConnection>();

    public DbSet<PlayerProfile> PlayerProfiles =>
        Set<PlayerProfile>();

    public DbSet<PlayerCharacter> PlayerCharacters =>
        Set<PlayerCharacter>();

    public DbSet<PlayerCharacterSkill> PlayerCharacterSkills =>
        Set<PlayerCharacterSkill>();

    public DbSet<PlayerCharacterCondition> PlayerCharacterConditions =>
        Set<PlayerCharacterCondition>();

    public DbSet<PlayerCharacterItem> PlayerCharacterItems =>
        Set<PlayerCharacterItem>();

    public DbSet<PlayerCharacterWeapon> PlayerCharacterWeapons =>
        Set<PlayerCharacterWeapon>();

    public DbSet<PlayerCharacterWeaponDamageComponent> PlayerCharacterWeaponDamageComponents =>
        Set<PlayerCharacterWeaponDamageComponent>();

    protected override void OnModelCreating(
        ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);

        modelBuilder.Entity<PersistedEncounterState>(
            entity =>
            {
                entity.ToTable("EncounterState");

                entity.HasKey(x => x.Id);

                entity.Property(x => x.EncounterJson)
                    .IsRequired();

                entity.Property(x => x.UndoHistoryJson)
                    .IsRequired();
            });

        modelBuilder.Entity<GameSessionRecord>(
            entity =>
            {
                entity.ToTable("GameSession");

                entity.HasKey(x => x.Id);

                entity.Property(x => x.JoinCode)
                    .IsRequired();

                entity.Property(x => x.DmPin)
                    .IsRequired();

                entity.Property(x => x.PlayerJoinToken)
                    .IsRequired();

                entity.Property(x => x.DisplayToken)
                    .IsRequired();
            });

        modelBuilder.Entity<RegisteredDevice>(
            entity =>
            {
                entity.ToTable("RegisteredDevice");

                entity.HasKey(x => x.Id);

                entity.Property(x => x.Name)
                    .IsRequired();

                entity.Property(x => x.DeviceType)
                    .IsRequired();
            });

        modelBuilder.Entity<DevicePairingRequest>(
            entity =>
            {
                entity.ToTable("DevicePairingRequest");

                entity.HasKey(x => x.Id);

                entity.Property(x => x.PairingCode)
                    .IsRequired();

                entity.Property(x => x.DeviceName)
                    .IsRequired();

                entity.Property(x => x.Status)
                    .IsRequired();
            });

        modelBuilder.Entity<PlayerConnection>(
            entity =>
            {
                entity.ToTable("PlayerConnection");

                entity.HasKey(x => x.Id);

                entity.Property(x => x.Name)
                    .IsRequired();
            });

        modelBuilder.Entity<PlayerProfile>(
            entity =>
            {
                entity.ToTable("PlayerProfile");

                entity.HasKey(x => x.Id);

                entity.Property(x => x.Name)
                    .IsRequired();

                entity.Property(x => x.AccessToken)
                    .IsRequired();

                entity.HasIndex(x => x.AccessToken)
                    .IsUnique();
            });


        modelBuilder.Entity<PlayerCharacter>(
            entity =>
            {
                entity.ToTable("PlayerCharacter");

                entity.HasKey(x => x.Id);

                entity.Property(x => x.Name)
                    .IsRequired();

                entity.HasIndex(x =>
                    x.PlayerProfileId);
            });

        modelBuilder.Entity<PlayerCharacterSkill>(
            entity =>
            {
                entity.HasKey(
                    x => x.Id);

                entity.HasIndex(
                        x => new
                        {
                            x.PlayerCharacterId,
                            x.Skill
                        })
                    .IsUnique();

                entity.HasOne(
                        x => x.PlayerCharacter)
                    .WithMany(
                        x => x.Skills)
                    .HasForeignKey(
                        x => x.PlayerCharacterId)
                    .OnDelete(
                        DeleteBehavior.Cascade);
            });

        modelBuilder.Entity<PlayerCharacterCondition>(
            entity =>
            {
                entity.HasKey(
                    x => x.Id);

                entity.Property(
                        x => x.Name)
                    .IsRequired()
                    .HasMaxLength(100);

                entity.Property(
                        x => x.Notes)
                    .HasMaxLength(1000);

                entity.HasIndex(
                    x => new
                    {
                        x.PlayerCharacterId,
                        x.Name
                    })
                    .IsUnique();

                entity.HasOne(
                        x => x.PlayerCharacter)
                    .WithMany(
                        x => x.Conditions)
                    .HasForeignKey(
                        x => x.PlayerCharacterId)
                    .OnDelete(
                        DeleteBehavior.Cascade);
            });

        modelBuilder.Entity<PlayerCharacterItem>(
            entity =>
            {
                entity.ToTable(
                    "PlayerCharacterItem");

                entity.HasKey(
                    x => x.Id);

                entity.Property(
                        x => x.Name)
                    .IsRequired()
                    .HasMaxLength(150);

                entity.Property(
                        x => x.ItemType)
                    .IsRequired();

                entity.Property(
                        x => x.Notes)
                    .HasMaxLength(2000);

                entity.HasIndex(
                    x => x.PlayerCharacterId);

                entity.HasOne(
                        x => x.PlayerCharacter)
                    .WithMany(
                        x => x.InventoryItems)
                    .HasForeignKey(
                        x => x.PlayerCharacterId)
                    .OnDelete(
                        DeleteBehavior.Cascade);
            });

        modelBuilder.Entity<PlayerCharacterWeapon>(
            entity =>
            {
                entity.ToTable(
                    "PlayerCharacterWeapon");

                entity.HasKey(
                    x => x.PlayerCharacterItemId);

                entity.Property(
                        x => x.AttackAbility)
                    .IsRequired();

                entity.HasOne(
                        x => x.Item)
                    .WithOne(
                        x => x.Weapon)
                    .HasForeignKey<PlayerCharacterWeapon>(
                        x => x.PlayerCharacterItemId)
                    .OnDelete(
                        DeleteBehavior.Cascade);
            });

        modelBuilder.Entity<PlayerCharacterWeaponDamageComponent>(
            entity =>
            {
                entity.ToTable(
                    "PlayerCharacterWeaponDamageComponent");

                entity.HasKey(
                    x => x.Id);

                entity.Property(
                        x => x.DamageDice)
                    .IsRequired()
                    .HasMaxLength(50);

                entity.Property(
                        x => x.DamageType)
                    .IsRequired()
                    .HasMaxLength(100);

                entity.Property(
                        x => x.VersatileDamageDice)
                    .HasMaxLength(50);

                entity.HasIndex(
                    x => new
                    {
                        x.PlayerCharacterItemId,
                        x.SortOrder
                    });

                entity.HasOne(
                        x => x.Weapon)
                    .WithMany(
                        x => x.DamageComponents)
                    .HasForeignKey(
                        x => x.PlayerCharacterItemId)
                    .OnDelete(
                        DeleteBehavior.Cascade);
            });
    }
}