using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using PrintServer.Core.Queue;

namespace PrintServer.Queue;

public sealed class SqliteJobStore(IDbContextFactory<QueueDbContext> factory, TimeProvider time) : IJobStore
{
    private const int SqliteConstraintError = 19;
    private const int ClaimAttempts = 5;
    private const int ClaimCandidates = 100;

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
                .Where(j => j.State == JobState.Pending && (j.NextAttemptAt == null || j.NextAttemptAt <= now))
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
}
