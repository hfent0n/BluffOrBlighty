using Microsoft.AspNetCore.DataProtection.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace BluffOrBlighty.Api.Data;

/// <summary>
/// A player account. Deliberately minimal: we keep Google's stable account id (to recognise
/// you next time) and the name you play under. No email, no profile picture.
/// </summary>
public sealed class User
{
    public Guid Id { get; set; } = Guid.NewGuid();

    /// <summary>Google's "sub" claim: a stable, opaque id for the Google account.</summary>
    public required string GoogleSubject { get; set; }

    public required string DisplayName { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset LastSeenAt { get; set; }

    public List<CampaignProgress> Progress { get; set; } = [];
    public List<GameRecord> Games { get; set; } = [];
}

/// <summary>Best result per campaign level. Same shape as the browser's saved progress.</summary>
public sealed class CampaignProgress
{
    public Guid UserId { get; set; }
    public required string LevelId { get; set; }
    public int Stars { get; set; }
    public int Wins { get; set; }
    public int Plays { get; set; }
    public int BestMargin { get; set; }
    public DateTimeOffset LastPlayedAt { get; set; }
}

/// <summary>One finished game, from one signed-in player's point of view.</summary>
public sealed class GameRecord
{
    public long Id { get; set; }
    public Guid UserId { get; set; }
    public required string GameCode { get; set; }
    public DateTimeOffset FinishedAt { get; set; }
    public required string MapId { get; set; }
    public string? CampaignLevelId { get; set; }
    public int Players { get; set; }
    public int Score { get; set; }

    /// <summary>1 = first (ties share a rank).</summary>
    public int Rank { get; set; }

    public bool Won { get; set; }
}

public sealed class AppDbContext(DbContextOptions<AppDbContext> options) : DbContext(options), IDataProtectionKeyContext
{
    public DbSet<User> Users => Set<User>();
    public DbSet<CampaignProgress> CampaignProgress => Set<CampaignProgress>();
    public DbSet<GameRecord> GameRecords => Set<GameRecord>();

    /// <summary>
    /// Keys that encrypt sign-in cookies. Kept here so every server instance, and every deploy,
    /// can read the same cookies, rather than signing everyone out.
    /// </summary>
    public DbSet<DataProtectionKey> DataProtectionKeys => Set<DataProtectionKey>();

    protected override void OnModelCreating(ModelBuilder b)
    {
        b.Entity<User>(e =>
        {
            e.ToTable("users");
            e.HasIndex(u => u.GoogleSubject).IsUnique();
            e.Property(u => u.GoogleSubject).HasMaxLength(255);
            e.Property(u => u.DisplayName).HasMaxLength(60);
            e.HasMany(u => u.Progress).WithOne().HasForeignKey(p => p.UserId).OnDelete(DeleteBehavior.Cascade);
            e.HasMany(u => u.Games).WithOne().HasForeignKey(g => g.UserId).OnDelete(DeleteBehavior.Cascade);
        });

        b.Entity<CampaignProgress>(e =>
        {
            e.ToTable("campaign_progress");
            e.HasKey(p => new { p.UserId, p.LevelId });
            e.Property(p => p.LevelId).HasMaxLength(64);
        });

        b.Entity<GameRecord>(e =>
        {
            e.ToTable("game_records");
            e.HasIndex(g => new { g.UserId, g.FinishedAt });
            e.Property(g => g.GameCode).HasMaxLength(8);
            e.Property(g => g.MapId).HasMaxLength(64);
            e.Property(g => g.CampaignLevelId).HasMaxLength(64);
        });

        b.Entity<DataProtectionKey>().ToTable("data_protection_keys");
    }
}

/// <summary>Lets <c>dotnet ef migrations add</c> build the model without a running app or database.</summary>
public sealed class DesignTimeDbContextFactory : IDesignTimeDbContextFactory<AppDbContext>
{
    public AppDbContext CreateDbContext(string[] args) =>
        new(new DbContextOptionsBuilder<AppDbContext>()
            .UseNpgsql("Host=localhost;Database=bluff;Username=bluff")
            .Options);
}
