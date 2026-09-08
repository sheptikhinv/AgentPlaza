using AgentPlaza.Domain;
using Microsoft.EntityFrameworkCore;

namespace AgentPlaza.Infrastructure;

/// <summary>Provides persistence for people, installations, sessions, and presence.</summary>
public sealed class PlazaDbContext(DbContextOptions<PlazaDbContext> options) : DbContext(options)
{
    /// <summary>Gets people displayed in the plaza.</summary>
    public DbSet<Person> People => Set<Person>();

    /// <summary>Gets configured reporter installations.</summary>
    public DbSet<Installation> Installations => Set<Installation>();

    /// <summary>Gets external agent sessions.</summary>
    public DbSet<AgentSession> Sessions => Set<AgentSession>();

    /// <summary>Gets deduplicated event receipts.</summary>
    public DbSet<AgentEventReceipt> EventReceipts => Set<AgentEventReceipt>();

    /// <summary>Gets materialized person presence snapshots.</summary>
    public DbSet<PresenceSnapshot> PresenceSnapshots => Set<PresenceSnapshot>();

    /// <inheritdoc />
    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<Person>(entity =>
        {
            entity.Property(value => value.Name).HasMaxLength(80);
            entity.Property(value => value.AvatarSeed).HasMaxLength(80);
        });

        modelBuilder.Entity<Installation>(entity =>
        {
            entity.Property(value => value.Name).HasMaxLength(80);
            entity.Property(value => value.TokenHash).HasMaxLength(64);
            entity.HasIndex(value => value.TokenHash).IsUnique();
            entity.HasOne(value => value.Person).WithMany(value => value.Installations).HasForeignKey(value => value.PersonId);
        });

        modelBuilder.Entity<AgentSession>(entity =>
        {
            entity.HasKey(value => new { value.InstallationId, value.ExternalId });
            entity.Property(value => value.ExternalId).HasMaxLength(256);
            entity.Property(value => value.Source).HasMaxLength(32);
            entity.Property(value => value.Status).HasConversion<string>().HasMaxLength(16);
            entity.Property(value => value.ActivityKind).HasMaxLength(32);
            entity.Property(value => value.Summary).HasMaxLength(160);
            entity.Property(value => value.Project).HasMaxLength(80);
            entity.HasIndex(value => value.LastSeenAt);
            entity.HasOne(value => value.Installation).WithMany(value => value.Sessions).HasForeignKey(value => value.InstallationId);
        });

        modelBuilder.Entity<AgentEventReceipt>(entity =>
        {
            entity.HasKey(value => value.EventId);
            entity.Property(value => value.SessionId).HasMaxLength(256);
            entity.HasIndex(value => new { value.InstallationId, value.SessionId, value.Sequence }).IsUnique();
        });

        modelBuilder.Entity<PresenceSnapshot>(entity =>
        {
            entity.HasKey(value => value.PersonId);
            entity.Property(value => value.Status).HasConversion<string>().HasMaxLength(16);
            entity.Property(value => value.ActivityKind).HasMaxLength(32);
            entity.Property(value => value.Summary).HasMaxLength(160);
            entity.Property(value => value.Project).HasMaxLength(80);
            entity.HasOne(value => value.Person).WithOne().HasForeignKey<PresenceSnapshot>(value => value.PersonId);
        });
    }
}
