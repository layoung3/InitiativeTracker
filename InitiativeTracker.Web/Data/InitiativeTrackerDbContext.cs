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
    }
}