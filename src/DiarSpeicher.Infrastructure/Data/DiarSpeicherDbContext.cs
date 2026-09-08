using DiarSpeicher.Core.Domain.Entities;
using DiarSpeicher.Infrastructure.Data.Conversions;
using Microsoft.EntityFrameworkCore;

namespace DiarSpeicher.Infrastructure.Data;

public class DiarSpeicherDbContext : DbContext
{
    public DiarSpeicherDbContext(DbContextOptions<DiarSpeicherDbContext> options)
        : base(options)
    {
    }

    public DbSet<Library> Libraries => Set<Library>();
    public DbSet<LibraryConfig> LibraryConfigs => Set<LibraryConfig>();
    public DbSet<LibraryExclusion> LibraryExclusions => Set<LibraryExclusion>();
    public DbSet<Series> Series => Set<Series>();
    public DbSet<SeriesMetadata> SeriesMetadata => Set<SeriesMetadata>();
    public DbSet<Media> Media => Set<Media>();
    public DbSet<MediaMetadata> MediaMetadata => Set<MediaMetadata>();
    public DbSet<Tag> Tags => Set<Tag>();
    public DbSet<MediaTag> MediaTags => Set<MediaTag>();
    public DbSet<SeriesTag> SeriesTags => Set<SeriesTag>();
    public DbSet<ScannedDirectory> ScannedDirectories => Set<ScannedDirectory>();
    public DbSet<ReadingSession> ReadingSessions => Set<ReadingSession>();
    public DbSet<User> Users => Set<User>();
    public DbSet<UserPreferences> UserPreferences => Set<UserPreferences>();
    public DbSet<AgeRestriction> AgeRestrictions => Set<AgeRestriction>();
    public DbSet<ApiKey> ApiKeys => Set<ApiKey>();
    public DbSet<Session> Sessions => Set<Session>();

    /// <summary>
    /// Applied model-wide rather than per property so that no timestamp added later silently
    /// reverts to the unsortable representation.
    /// </summary>
    protected override void ConfigureConventions(ModelConfigurationBuilder configurationBuilder)
    {
        base.ConfigureConventions(configurationBuilder);

        configurationBuilder.Properties<DateTimeOffset>().HaveConversion<DateTimeOffsetToTicksConverter>();
        configurationBuilder.Properties<DateTimeOffset?>().HaveConversion<NullableDateTimeOffsetToTicksConverter>();
    }

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);

        // Library & Config (1-to-1)
        modelBuilder.Entity<Library>(entity =>
        {
            entity.HasKey(e => e.Id);
            entity.Property(e => e.Id).HasMaxLength(32);
            entity.Property(e => e.Name).HasMaxLength(255).IsRequired();
            entity.Property(e => e.Path).IsRequired();
            entity.Property(e => e.Status).HasConversion<string>();

            entity.HasOne(e => e.Config)
                .WithOne(c => c.Library)
                .HasForeignKey<Library>(e => e.ConfigId)
                .OnDelete(DeleteBehavior.Cascade);

            entity.HasMany(e => e.Series)
                .WithOne(s => s.Library)
                .HasForeignKey(s => s.LibraryId)
                .OnDelete(DeleteBehavior.Cascade);

            entity.HasMany(e => e.ExcludedUsers)
                .WithOne(x => x.Library)
                .HasForeignKey(x => x.LibraryId)
                .OnDelete(DeleteBehavior.Cascade);
        });

        modelBuilder.Entity<LibraryConfig>(entity =>
        {
            entity.HasKey(e => e.Id);
            entity.Property(e => e.LibraryPattern).HasConversion<string>();
            entity.Property(e => e.LibraryType).HasConversion<string>();
            entity.Property(e => e.DefaultReadingDir).HasConversion<string>();
            entity.Property(e => e.DefaultReadingMode).HasConversion<string>();
            entity.Property(e => e.DefaultLibraryViewMode).HasConversion<string>();
        });

        // Series & SeriesMetadata (1-to-1, series_id as PK)
        modelBuilder.Entity<Series>(entity =>
        {
            entity.HasKey(e => e.Id);
            entity.Property(e => e.Id).HasMaxLength(32);
            entity.Property(e => e.Name).HasMaxLength(255).IsRequired();
            entity.Property(e => e.Path).IsRequired();
            entity.Property(e => e.Status).HasConversion<string>();
            entity.HasIndex(e => e.LibraryId);
            entity.HasIndex(e => e.Path);

            entity.HasOne(e => e.Metadata)
                .WithOne(m => m.Series)
                .HasForeignKey<SeriesMetadata>(m => m.SeriesId)
                .OnDelete(DeleteBehavior.Cascade);

            entity.HasMany(e => e.Media)
                .WithOne(m => m.Series)
                .HasForeignKey(m => m.SeriesId)
                .OnDelete(DeleteBehavior.SetNull);
        });

        modelBuilder.Entity<SeriesMetadata>(entity =>
        {
            entity.HasKey(e => e.SeriesId);
            entity.Property(e => e.SeriesId).HasMaxLength(32);
        });

        // Media & MediaMetadata (1-to-1)
        modelBuilder.Entity<Media>(entity =>
        {
            entity.HasKey(e => e.Id);
            entity.Property(e => e.Id).HasMaxLength(32);
            entity.Property(e => e.Name).HasMaxLength(255).IsRequired();
            entity.Property(e => e.Extension).HasMaxLength(32).IsRequired();
            entity.Property(e => e.Path).IsRequired();
            entity.Property(e => e.Status).HasConversion<string>();

            entity.HasIndex(e => e.SeriesId);
            entity.HasIndex(e => e.Path);
            entity.HasIndex(e => e.Hash);
            entity.HasIndex(e => e.KoreaderHash);
            entity.HasIndex(e => e.DeletedAt);

            // "Latest books" orders by creation date on every call; without this the query
            // scans the table and sorts it into a temporary B-tree.
            entity.HasIndex(e => e.CreatedAt);

            entity.HasOne(e => e.Metadata)
                .WithOne(m => m.Media)
                .HasForeignKey<MediaMetadata>(m => m.MediaId)
                .OnDelete(DeleteBehavior.Cascade);
        });

        modelBuilder.Entity<MediaMetadata>(entity =>
        {
            entity.HasKey(e => e.Id);
            entity.Property(e => e.MediaId).HasMaxLength(32);
            entity.HasIndex(e => e.MediaId).IsUnique();
        });

        // Tags & Relations
        modelBuilder.Entity<Tag>(entity =>
        {
            entity.HasKey(e => e.Id);
            entity.Property(e => e.Name).HasMaxLength(100).IsRequired();
            entity.HasIndex(e => e.Name).IsUnique();
        });

        modelBuilder.Entity<MediaTag>(entity =>
        {
            entity.HasKey(e => new { e.MediaId, e.TagId });
            entity.HasOne(e => e.Media)
                .WithMany(m => m.Tags)
                .HasForeignKey(e => e.MediaId)
                .OnDelete(DeleteBehavior.Cascade);
            entity.HasOne(e => e.Tag)
                .WithMany(t => t.MediaTags)
                .HasForeignKey(e => e.TagId)
                .OnDelete(DeleteBehavior.Cascade);
        });

        modelBuilder.Entity<SeriesTag>(entity =>
        {
            entity.HasKey(e => new { e.SeriesId, e.TagId });
            entity.HasOne(e => e.Series)
                .WithMany(s => s.Tags)
                .HasForeignKey(e => e.SeriesId)
                .OnDelete(DeleteBehavior.Cascade);
            entity.HasOne(e => e.Tag)
                .WithMany(t => t.SeriesTags)
                .HasForeignKey(e => e.TagId)
                .OnDelete(DeleteBehavior.Cascade);
        });

        // ScannedDirectory (Cache mtime)
        modelBuilder.Entity<ScannedDirectory>(entity =>
        {
            entity.HasKey(e => e.Path);
            entity.Property(e => e.Path).IsRequired();
        });

        // ReadingSession
        modelBuilder.Entity<ReadingSession>(entity =>
        {
            entity.HasKey(e => e.Id);
            entity.Property(e => e.Status).HasConversion<string>();
            entity.HasIndex(e => new { e.UserId, e.MediaId });

            entity.HasOne(e => e.Media)
                .WithMany(m => m.ReadingSessions)
                .HasForeignKey(e => e.MediaId)
                .OnDelete(DeleteBehavior.Cascade);

            entity.HasOne(e => e.User)
                .WithMany(u => u.ReadingSessions)
                .HasForeignKey(e => e.UserId)
                .OnDelete(DeleteBehavior.Cascade);
        });

        // User & Preferences
        modelBuilder.Entity<User>(entity =>
        {
            entity.HasKey(e => e.Id);
            entity.Property(e => e.Id).HasMaxLength(32);
            entity.Property(e => e.Username).HasMaxLength(100).IsRequired();
            entity.HasIndex(e => e.Username).IsUnique();

            entity.HasOne(e => e.Preferences)
                .WithOne(p => p.User)
                .HasForeignKey<UserPreferences>(p => p.UserId)
                .OnDelete(DeleteBehavior.Cascade);

            entity.HasOne(e => e.AgeRestriction)
                .WithOne(a => a.User)
                .HasForeignKey<AgeRestriction>(a => a.UserId)
                .OnDelete(DeleteBehavior.Cascade);

            entity.HasMany(e => e.ApiKeys)
                .WithOne(k => k.User)
                .HasForeignKey(k => k.UserId)
                .OnDelete(DeleteBehavior.Cascade);

            entity.HasMany(e => e.Sessions)
                .WithOne(s => s.User)
                .HasForeignKey(s => s.UserId)
                .OnDelete(DeleteBehavior.Cascade);
        });

        modelBuilder.Entity<ApiKey>(entity =>
        {
            entity.HasKey(e => e.Id);
            entity.Property(e => e.Id).HasMaxLength(32);
            entity.Property(e => e.UserId).HasMaxLength(32);
            entity.Property(e => e.KeyHash).IsRequired();
            entity.Property(e => e.Name).HasMaxLength(100).IsRequired();
            entity.HasIndex(e => e.KeyHash).IsUnique();
            entity.HasIndex(e => e.UserId);
        });

        modelBuilder.Entity<Session>(entity =>
        {
            entity.HasKey(e => e.Id);
            entity.Property(e => e.Id).HasMaxLength(32);
            entity.Property(e => e.UserId).HasMaxLength(32);
            entity.HasIndex(e => e.UserId);
            entity.HasIndex(e => e.ExpiresAt);
        });
    }

    public async Task InitializeSqliteWalAsync(CancellationToken cancellationToken = default)
    {
        if (Database.IsSqlite())
        {
            await Database.ExecuteSqlRawAsync("PRAGMA journal_mode=WAL;", cancellationToken);
            await Database.ExecuteSqlRawAsync("PRAGMA synchronous=NORMAL;", cancellationToken);
            await Database.ExecuteSqlRawAsync("PRAGMA temp_store=MEMORY;", cancellationToken);
        }
    }
}
