using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using PrintServer.Core.Printing;
using PrintServer.Core.Queue;

namespace PrintServer.Queue;

public sealed class SqliteJobStore(IDbContextFactory<QueueDbContext> factory, TimeProvider time) : IJobStore
{
    private const int SqliteConstraintError = 19;
    private const int ClaimAttempts = 5;
    private const int ClaimCandidates = 100;
    private const int MaxPageSize = 200;
    private const string LikeEscape = "\\";

    public async Task<bool> TryEnqueueAsync(NewJob job, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(job);

        using var db = factory.CreateDbContext();
        db.Jobs.Add(JobMapping.ToEntity(job, time.GetUtcNow()));
        try
        {
            await db.SaveChangesAsync(ct).ConfigureAwait(false);
            return true;
        }
        catch (DbUpdateException ex) when (ex.InnerException is SqliteException { SqliteErrorCode: SqliteConstraintError })
        {
            return false;
        }
    }

    public async Task<Job?> GetAsync(Guid id, CancellationToken ct)
    {
        using var db = factory.CreateDbContext();
        var entity = await db.Jobs.AsNoTracking().FirstOrDefaultAsync(j => j.Id == id, ct).ConfigureAwait(false);
        return entity is null ? null : JobMapping.ToJob(entity);
    }

    public async Task<IReadOnlyList<Job>> ListAsync(JobState state, CancellationToken ct)
    {
        using var db = factory.CreateDbContext();
        var entities = await db.Jobs.AsNoTracking()
            .Where(j => j.State == state)
            .OrderBy(j => j.CreatedAt)
            .ToListAsync(ct)
            .ConfigureAwait(false);
        return [.. entities.Select(JobMapping.ToJob)];
    }

    public async Task<JobPage> QueryAsync(JobQuery query, int skip, int take, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(query);
        ArgumentOutOfRangeException.ThrowIfNegative(skip);
        ArgumentOutOfRangeException.ThrowIfLessThan(take, 1);

        using var db = factory.CreateDbContext();
        var jobs = db.Jobs.AsNoTracking().AsQueryable();
        if (query.State is { } state)
        {
            jobs = jobs.Where(j => j.State == state);
        }

        if (!string.IsNullOrWhiteSpace(query.Search))
        {
            var pattern = "%" + EscapeLike(query.Search.Trim()) + "%";
            jobs = jobs.Where(j => EF.Functions.Like(j.FileName, pattern, LikeEscape));
        }

        var total = await jobs.CountAsync(ct).ConfigureAwait(false);
        var entities = await jobs
            .OrderByDescending(j => j.CreatedAt)
            .ThenBy(j => j.Id)
            .Skip(skip)
            .Take(Math.Min(take, MaxPageSize))
            .ToListAsync(ct)
            .ConfigureAwait(false);
        return new JobPage([.. entities.Select(JobMapping.ToJob)], total);
    }

    public async Task<IReadOnlyDictionary<JobState, int>> CountByStateAsync(CancellationToken ct)
    {
        using var db = factory.CreateDbContext();
        var counts = await db.Jobs.AsNoTracking()
            .GroupBy(j => j.State)
            .Select(group => new { State = group.Key, Count = group.Count() })
            .ToListAsync(ct)
            .ConfigureAwait(false);
        return counts.ToDictionary(item => item.State, item => item.Count);
    }

    public async Task<bool> CancelAsync(Guid id, string reason, CancellationToken ct)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(reason);

        var now = time.GetUtcNow();
        using var db = factory.CreateDbContext();
        var updated = await db.Jobs
            .Where(j => j.Id == id
                && (j.State == JobState.Pending || j.State == JobState.Converting || j.State == JobState.Printing))
            .ExecuteUpdateAsync(
                set => set
                    .SetProperty(j => j.State, JobState.Cancelled)
                    .SetProperty(j => j.LastError, reason)
                    .SetProperty(j => j.FinishedAt, now)
                    .SetProperty(j => j.NextAttemptAt, (DateTimeOffset?)null),
                ct)
            .ConfigureAwait(false);
        return updated == 1;
    }

    public async Task<RestartOutcome> RestartAsync(Guid id, CancellationToken ct)
    {
        var now = time.GetUtcNow();
        using var db = factory.CreateDbContext();
        try
        {
            var updated = await db.Jobs
                .Where(j => j.Id == id
                    && (j.State == JobState.Failed || j.State == JobState.Stalled || j.State == JobState.Cancelled))
                .ExecuteUpdateAsync(
                    set => set
                        .SetProperty(j => j.State, JobState.Pending)
                        .SetProperty(j => j.Attempts, 0)
                        .SetProperty(j => j.LastError, (string?)null)
                        .SetProperty(j => j.CreatedAt, now)
                        .SetProperty(j => j.StartedAt, (DateTimeOffset?)null)
                        .SetProperty(j => j.FinishedAt, (DateTimeOffset?)null)
                        .SetProperty(j => j.NextAttemptAt, (DateTimeOffset?)null)
                        .SetProperty(j => j.LastBlockedAt, (DateTimeOffset?)null)
                        .SetProperty(j => j.BackendJobId, (string?)null)
                        .SetProperty(j => j.DocumentFormat, j => j.SourceFormat),
                    ct)
                .ConfigureAwait(false);
            return updated == 1 ? RestartOutcome.Restarted : RestartOutcome.NotRestartable;
        }
        catch (SqliteException ex) when (ex.SqliteErrorCode == SqliteConstraintError)
        {
            return RestartOutcome.Conflict;
        }
        catch (DbUpdateException ex) when (ex.InnerException is SqliteException { SqliteErrorCode: SqliteConstraintError })
        {
            return RestartOutcome.Conflict;
        }
    }

    public async Task<IReadOnlyList<Job>> FindActiveAsync(string originDirectory, string fileName, CancellationToken ct)
    {
        using var db = factory.CreateDbContext();
        var entities = await db.Jobs.AsNoTracking()
            .Where(j => j.OriginDirectory == originDirectory
                && j.FileName == fileName
                && (j.State == JobState.Pending || j.State == JobState.Converting || j.State == JobState.Printing))
            .OrderBy(j => j.CreatedAt)
            .ToListAsync(ct)
            .ConfigureAwait(false);
        return [.. entities.Select(JobMapping.ToJob)];
    }

    public async Task<Job?> ClaimNextAsync(CancellationToken ct)
    {
        var printable = DocumentFormats.Printable.ToArray();
        using var db = factory.CreateDbContext();
        for (var attempt = 0; attempt < ClaimAttempts; attempt++)
        {
            var now = time.GetUtcNow();
            var busyPrinters = await db.Jobs.AsNoTracking()
                .Where(j => j.State == JobState.Printing)
                .Select(j => j.PrinterName)
                .ToListAsync(ct)
                .ConfigureAwait(false);
            var eligible = await db.Jobs.AsNoTracking()
                .Where(j => j.State == JobState.Pending
                    && printable.Contains(j.DocumentFormat)
                    && (j.NextAttemptAt == null || j.NextAttemptAt <= now))
                .OrderBy(j => j.CreatedAt)
                .Take(ClaimCandidates)
                .ToListAsync(ct)
                .ConfigureAwait(false);

            var candidate = eligible.FirstOrDefault(j => !busyPrinters.Contains(j.PrinterName));
            if (candidate is null)
            {
                return null;
            }

            var claimed = await db.Jobs
                .Where(j => j.Id == candidate.Id && j.State == JobState.Pending)
                .ExecuteUpdateAsync(
                    set => set
                        .SetProperty(j => j.State, JobState.Printing)
                        .SetProperty(j => j.StartedAt, now)
                        .SetProperty(j => j.Attempts, j => j.Attempts + 1)
                        .SetProperty(j => j.NextAttemptAt, (DateTimeOffset?)null),
                    ct)
                .ConfigureAwait(false);
            if (claimed == 1)
            {
                return await GetAsync(candidate.Id, ct).ConfigureAwait(false);
            }
        }

        return null;
    }

    public async Task<Job?> ClaimNextConversionAsync(CancellationToken ct)
    {
        var printable = DocumentFormats.Printable.ToArray();
        using var db = factory.CreateDbContext();
        for (var attempt = 0; attempt < ClaimAttempts; attempt++)
        {
            var now = time.GetUtcNow();
            var candidate = await db.Jobs.AsNoTracking()
                .Where(j => j.State == JobState.Pending && !printable.Contains(j.DocumentFormat))
                .OrderBy(j => j.CreatedAt)
                .FirstOrDefaultAsync(ct)
                .ConfigureAwait(false);
            if (candidate is null)
            {
                return null;
            }

            var claimed = await db.Jobs
                .Where(j => j.Id == candidate.Id && j.State == JobState.Pending)
                .ExecuteUpdateAsync(
                    set => set
                        .SetProperty(j => j.State, JobState.Converting)
                        .SetProperty(j => j.StartedAt, now),
                    ct)
                .ConfigureAwait(false);
            if (claimed == 1)
            {
                return await GetAsync(candidate.Id, ct).ConfigureAwait(false);
            }
        }

        return null;
    }

    public async Task<bool> FinishConversionAsync(Guid id, string documentFormat, CancellationToken ct)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(documentFormat);

        using var db = factory.CreateDbContext();
        var updated = await db.Jobs
            .Where(j => j.Id == id && j.State == JobState.Converting)
            .ExecuteUpdateAsync(
                set => set
                    .SetProperty(j => j.State, JobState.Pending)
                    .SetProperty(j => j.DocumentFormat, documentFormat)
                    .SetProperty(j => j.StartedAt, (DateTimeOffset?)null),
                ct)
            .ConfigureAwait(false);
        return updated == 1;
    }

    public async Task<bool> SetBackendJobIdAsync(Guid id, string backendJobId, CancellationToken ct)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(backendJobId);

        using var db = factory.CreateDbContext();
        var updated = await db.Jobs
            .Where(j => j.Id == id && j.State == JobState.Printing)
            .ExecuteUpdateAsync(set => set.SetProperty(j => j.BackendJobId, backendJobId), ct)
            .ConfigureAwait(false);
        return updated == 1;
    }

    public async Task<bool> MarkBlockedAsync(Guid id, CancellationToken ct)
    {
        var now = time.GetUtcNow();
        using var db = factory.CreateDbContext();
        var updated = await db.Jobs
            .Where(j => j.Id == id && j.State == JobState.Printing)
            .ExecuteUpdateAsync(set => set.SetProperty(j => j.LastBlockedAt, now), ct)
            .ConfigureAwait(false);
        return updated == 1;
    }

    public async Task<bool> CompleteAsync(Guid id, CancellationToken ct)
    {
        var now = time.GetUtcNow();
        using var db = factory.CreateDbContext();
        var updated = await db.Jobs
            .Where(j => j.Id == id && j.State == JobState.Printing)
            .ExecuteUpdateAsync(
                set => set
                    .SetProperty(j => j.State, JobState.Completed)
                    .SetProperty(j => j.FinishedAt, now)
                    .SetProperty(j => j.LastError, (string?)null),
                ct)
            .ConfigureAwait(false);
        return updated == 1;
    }

    public async Task<bool> FailAsync(Guid id, string reason, CancellationToken ct)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(reason);

        var now = time.GetUtcNow();
        using var db = factory.CreateDbContext();
        var updated = await db.Jobs
            .Where(j => j.Id == id
                && (j.State == JobState.Pending || j.State == JobState.Converting || j.State == JobState.Printing))
            .ExecuteUpdateAsync(
                set => set
                    .SetProperty(j => j.State, JobState.Failed)
                    .SetProperty(j => j.LastError, reason)
                    .SetProperty(j => j.FinishedAt, now)
                    .SetProperty(j => j.NextAttemptAt, (DateTimeOffset?)null),
                ct)
            .ConfigureAwait(false);
        return updated == 1;
    }

    public async Task<bool> StallAsync(Guid id, string reason, CancellationToken ct)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(reason);

        var now = time.GetUtcNow();
        using var db = factory.CreateDbContext();
        var updated = await db.Jobs
            .Where(j => j.Id == id && j.State == JobState.Printing)
            .ExecuteUpdateAsync(
                set => set
                    .SetProperty(j => j.State, JobState.Stalled)
                    .SetProperty(j => j.LastError, reason)
                    .SetProperty(j => j.FinishedAt, now),
                ct)
            .ConfigureAwait(false);
        return updated == 1;
    }

    public async Task<bool> RetryLaterAsync(Guid id, string reason, DateTimeOffset nextAttemptAt, CancellationToken ct)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(reason);

        using var db = factory.CreateDbContext();
        var updated = await db.Jobs
            .Where(j => j.Id == id && j.State == JobState.Printing)
            .ExecuteUpdateAsync(
                set => set
                    .SetProperty(j => j.State, JobState.Pending)
                    .SetProperty(j => j.LastError, reason)
                    .SetProperty(j => j.NextAttemptAt, (DateTimeOffset?)nextAttemptAt)
                    .SetProperty(j => j.StartedAt, (DateTimeOffset?)null)
                    .SetProperty(j => j.LastBlockedAt, (DateTimeOffset?)null)
                    .SetProperty(j => j.BackendJobId, (string?)null),
                ct)
            .ConfigureAwait(false);
        return updated == 1;
    }

    public async Task<bool> RequeueAsync(Guid id, CancellationToken ct)
    {
        using var db = factory.CreateDbContext();
        var updated = await db.Jobs
            .Where(j => j.Id == id && (j.State == JobState.Printing || j.State == JobState.Converting))
            .ExecuteUpdateAsync(
                set => set
                    .SetProperty(j => j.State, JobState.Pending)
                    .SetProperty(j => j.StartedAt, (DateTimeOffset?)null)
                    .SetProperty(j => j.NextAttemptAt, (DateTimeOffset?)null)
                    .SetProperty(j => j.LastBlockedAt, (DateTimeOffset?)null)
                    .SetProperty(j => j.BackendJobId, (string?)null),
                ct)
            .ConfigureAwait(false);
        return updated == 1;
    }

    public async Task<bool> UpdateDocumentPathAsync(Guid id, string documentPath, CancellationToken ct)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(documentPath);

        using var db = factory.CreateDbContext();
        var updated = await db.Jobs
            .Where(j => j.Id == id)
            .ExecuteUpdateAsync(set => set.SetProperty(j => j.DocumentPath, documentPath), ct)
            .ConfigureAwait(false);
        return updated == 1;
    }

    private static string EscapeLike(string text) =>
        text.Replace(LikeEscape, LikeEscape + LikeEscape, StringComparison.Ordinal)
            .Replace("%", LikeEscape + "%", StringComparison.Ordinal)
            .Replace("_", LikeEscape + "_", StringComparison.Ordinal);
}
