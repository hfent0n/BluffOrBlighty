using BluffOrBlighty.Api.Data;
using BluffOrBlighty.Api.Game;
using Microsoft.EntityFrameworkCore;

namespace BluffOrBlighty.Api.Accounts;

// ---- What the client sees

public sealed record LevelProgressDto(int Stars, int Wins, int Plays, int BestMargin, DateTimeOffset LastPlayedAt);

public sealed record AccountStats(int Games, int Wins);

public sealed record MeDto(
    Guid Id,
    string DisplayName,
    Dictionary<string, LevelProgressDto> Progress,
    AccountStats Stats);

/// <summary>Campaign progress uploaded from the browser, in the same shape it's stored there.</summary>
public sealed record ProgressImport(Dictionary<string, LevelProgressDto> Levels);

/// <summary>How saved progress combines: keep the best of each, so importing twice changes nothing.</summary>
public static class ProgressMerge
{
    public static void Into(CampaignProgress target, LevelProgressDto incoming)
    {
        target.Stars = Math.Clamp(Math.Max(target.Stars, incoming.Stars), 0, 3);
        target.Wins = Math.Max(target.Wins, Math.Max(0, incoming.Wins));
        target.Plays = Math.Max(target.Plays, Math.Max(0, incoming.Plays));
        target.BestMargin = Math.Max(target.BestMargin, incoming.BestMargin);
        if (incoming.LastPlayedAt > target.LastPlayedAt) target.LastPlayedAt = incoming.LastPlayedAt;
    }

    /// <summary>A finished campaign match. Unlike an import, this counts as a new play.</summary>
    public static void Record(CampaignProgress target, CampaignResult result, DateTimeOffset at)
    {
        target.Plays += 1;
        if (result.Won)
        {
            target.Wins += 1;
            target.BestMargin = Math.Max(target.BestMargin, result.YourScore - result.OpponentScore);
        }

        target.Stars = Math.Max(target.Stars, result.Stars);
        target.LastPlayedAt = at;
    }
}

public sealed class AccountService(AppDbContext db, TimeProvider time)
{
    public const int MaxNameLength = 60;

    /// <summary>Finds the account for a Google sign-in, creating it the first time.</summary>
    public async Task<User> SignInAsync(string googleSubject, string? suggestedName)
    {
        var now = time.GetUtcNow();
        var user = await db.Users.SingleOrDefaultAsync(u => u.GoogleSubject == googleSubject);
        if (user is null)
        {
            user = new User
            {
                GoogleSubject = googleSubject,
                DisplayName = CleanName(suggestedName) ?? "Player",
                CreatedAt = now,
            };
            db.Users.Add(user);
        }

        user.LastSeenAt = now;
        await db.SaveChangesAsync();
        return user;
    }

    public async Task<MeDto?> GetMeAsync(Guid userId)
    {
        var user = await db.Users.AsNoTracking()
            .Include(u => u.Progress)
            .SingleOrDefaultAsync(u => u.Id == userId);
        if (user is null) return null;

        var games = await db.GameRecords.CountAsync(g => g.UserId == userId);
        var wins = await db.GameRecords.CountAsync(g => g.UserId == userId && g.Won);
        return new MeDto(
            user.Id,
            user.DisplayName,
            user.Progress.ToDictionary(p => p.LevelId,
                p => new LevelProgressDto(p.Stars, p.Wins, p.Plays, p.BestMargin, p.LastPlayedAt)),
            new AccountStats(games, wins));
    }

    public async Task<bool> RenameAsync(Guid userId, string name)
    {
        var clean = CleanName(name) ?? throw new GameException("Your name can't be empty.");
        var user = await db.Users.FindAsync(userId);
        if (user is null) return false;
        user.DisplayName = clean;
        await db.SaveChangesAsync();
        return true;
    }

    /// <summary>Merges progress saved in the browser into the account (only known levels; best of each).</summary>
    public async Task ImportProgressAsync(Guid userId, ProgressImport import)
    {
        var known = CampaignLevels.All.Select(l => l.Id).ToHashSet();
        var existing = await db.CampaignProgress.Where(p => p.UserId == userId).ToDictionaryAsync(p => p.LevelId);
        foreach (var (levelId, incoming) in import.Levels)
        {
            if (!known.Contains(levelId)) continue;
            if (!existing.TryGetValue(levelId, out var row))
            {
                row = new CampaignProgress { UserId = userId, LevelId = levelId };
                db.CampaignProgress.Add(row);
                existing[levelId] = row;
            }

            ProgressMerge.Into(row, incoming);
        }

        await db.SaveChangesAsync();
    }

    /// <summary>Saves finished games, and campaign progress for campaign matches.</summary>
    public async Task RecordResultsAsync(IReadOnlyList<PlayerResult> results)
    {
        var now = time.GetUtcNow();
        foreach (var r in results)
        {
            if (!Guid.TryParse(r.UserId, out var userId) || !await db.Users.AnyAsync(u => u.Id == userId)) continue;

            db.GameRecords.Add(new GameRecord
            {
                UserId = userId,
                GameCode = r.GameCode,
                FinishedAt = now,
                MapId = r.MapId,
                CampaignLevelId = r.CampaignLevelId,
                Players = r.Players,
                Score = r.Score,
                Rank = r.Rank,
                Won = r.Won,
            });

            if (r.CampaignLevelId is { } levelId && r.Campaign is { } campaign)
            {
                var row = await db.CampaignProgress.FindAsync(userId, levelId);
                if (row is null)
                {
                    row = new CampaignProgress { UserId = userId, LevelId = levelId };
                    db.CampaignProgress.Add(row);
                }

                ProgressMerge.Record(row, campaign, now);
            }
        }

        await db.SaveChangesAsync();
    }

    /// <summary>Deletes the account and everything saved for it.</summary>
    public async Task DeleteAsync(Guid userId) =>
        await db.Users.Where(u => u.Id == userId).ExecuteDeleteAsync();

    private static string? CleanName(string? name)
    {
        var clean = string.Join(' ', (name ?? "").Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries));
        if (clean.Length == 0) return null;
        return clean.Length > MaxNameLength ? clean[..MaxNameLength] : clean;
    }
}

/// <summary>Saves finished games to players' accounts. A no-op when accounts aren't set up.</summary>
public interface IResultRecorder
{
    bool Enabled { get; }
    Task RecordAsync(IReadOnlyList<PlayerResult> results);
}

public sealed class NoResultRecorder : IResultRecorder
{
    public bool Enabled => false;
    public Task RecordAsync(IReadOnlyList<PlayerResult> results) => Task.CompletedTask;
}

/// <summary>Records through a fresh scope, so it works from the hub or anywhere else.</summary>
public sealed class DbResultRecorder(IServiceScopeFactory scopes, ILogger<DbResultRecorder> logger) : IResultRecorder
{
    public bool Enabled => true;

    public async Task RecordAsync(IReadOnlyList<PlayerResult> results)
    {
        if (results.Count == 0) return;
        try
        {
            await using var scope = scopes.CreateAsyncScope();
            await scope.ServiceProvider.GetRequiredService<AccountService>().RecordResultsAsync(results);
        }
        catch (Exception e)
        {
            // Losing a stats row is better than breaking the end of a game.
            logger.LogError(e, "Couldn't save results for game {Code}", results[0].GameCode);
        }
    }
}
