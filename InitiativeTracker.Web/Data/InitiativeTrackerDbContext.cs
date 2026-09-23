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
    }
}