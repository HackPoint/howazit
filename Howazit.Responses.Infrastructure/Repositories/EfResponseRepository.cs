using Howazit.Responses.Application.Abstractions;
using Howazit.Responses.Domain.Entities;
using Howazit.Responses.Infrastructure.Persistence;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Polly;
using Polly.Retry;

namespace Howazit.Responses.Infrastructure.Repositories;

public partial class EfResponseRepository(ResponsesDbContext db, ILogger<EfResponseRepository> logger)
    : IResponseRepository {
    // Process-wide retry pipeline (exponential backoff + jitter) for *transient* DB failures only.
    private static readonly ResiliencePipeline DbWritePipeline = new ResiliencePipelineBuilder()
        .AddRetry(new RetryStrategyOptions {
            MaxRetryAttempts = 3,
            Delay = TimeSpan.FromMilliseconds(80),
            BackoffType = DelayBackoffType.Exponential,
            UseJitter = true,
            // Only retry transients; never retry unique-constraint duplicates
            ShouldHandle = args => {
                if (args.Outcome.Exception is null) return ValueTask.FromResult(false);

                var ex = args.Outcome.Exception;

                // Do not retry on idempotent duplicates
                if (IsUniqueConstraintViolation(ex)) return ValueTask.FromResult(false);

                // Retry on common EF/SQLite transient conditions
                if (ex is TimeoutException) return ValueTask.FromResult(true);
                if (IsSqliteBusyOrLocked(ex)) return ValueTask.FromResult(true);
                if (IsEfTransient(ex)) return ValueTask.FromResult(true);

                return ValueTask.FromResult(false);
            }
        })
        .Build();

    public Task<bool> ExistsAsync(string clientId, string responseId, CancellationToken ct = default) =>
        db.SurveyResponses
            .AsNoTracking()
            .AnyAsync(r => r.ClientId == clientId && r.ResponseId == responseId, ct);

    public async Task<bool> TryAddAsync(SurveyResponse entity, CancellationToken ct = default) {
        // Optimistic short-circuit (still race-safe due to unique index + catch below)
        if (await ExistsAsync(entity.ClientId, entity.ResponseId, ct).ConfigureAwait(false))
            return false;

        await db.SurveyResponses.AddAsync(entity, ct).ConfigureAwait(false);

        try {
            // Retry SaveChangesAsync on *transient* issues only
            await DbWritePipeline
                .ExecuteAsync(async token => { await db.SaveChangesAsync(token).ConfigureAwait(false); }, ct)
                .ConfigureAwait(false);

            return true;
        }
        catch (DbUpdateException ex) when (IsUniqueConstraintViolation(ex)) {
            // Idempotent duplicate — clean tracking and report "not added"
            LogDuplicateResponseClientidResponseid(ex, entity.ClientId, entity.ResponseId);
            db.ChangeTracker.Clear();
            return false;
        }
    }

    public Task<int> CountForClientAsync(string clientId, CancellationToken ct = default) =>
        db.SurveyResponses
            .AsNoTracking()
            .CountAsync(r => r.ClientId == clientId, ct);

    // ---------- Helpers ----------

    private static bool IsEfTransient(Exception ex) {
        // Retry EF concurrency/update timeouts etc. (but *not* unique violations).
        return ex is DbUpdateConcurrencyException
               || ex is DbUpdateException &&
               !IsUniqueConstraintViolation(ex);
    }

    private static bool IsSqliteBusyOrLocked(Exception ex) {
        // Follow InnerException chain to find SqliteException(BUSY/LOCKED)
        for (var e = ex; e is not null; e = e.InnerException!) {
            if (e is SqliteException s &&
                (s.SqliteErrorCode == 5 /*SQLITE_BUSY*/ || s.SqliteErrorCode == 6 /*SQLITE_LOCKED*/))
                return true;
        }

        return false;
    }

    private static bool IsUniqueConstraintViolation(Exception ex) {
        // SQLite: SQLITE_CONSTRAINT = 19
        for (var e = ex; e is not null; e = e.InnerException!) {
            if (e is SqliteException s && s.SqliteErrorCode == 19)
                return true;

            // SQL Server (without hard dependency): detect by reflection (2601, 2627)
            var typeName = e.GetType().Name;
            if (typeName is "SqlException") {
                var numberProp = e.GetType().GetProperty("Number");
                if (numberProp?.GetValue(e) is int number && (number == 2601 || number == 2627))
                    return true;
            }

            // PostgreSQL (without hard dependency): SqlState 23505
            var sqlStateProp = e.GetType().GetProperty("SqlState");
            if (sqlStateProp?.GetValue(e) is string state && state == "23505")
                return true;
        }

        return false;
    }

    // Source-generated logger method (as you had it)
    [LoggerMessage(LogLevel.Debug, "Duplicate response {clientId}/{responseId}")]
    partial void LogDuplicateResponseClientidResponseid(DbUpdateException dbUpdateException, string clientId,
        string responseId);
}