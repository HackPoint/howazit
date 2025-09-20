using System.Data.Common;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Polly;
using Polly.CircuitBreaker;
using Polly.Retry;

namespace Howazit.Responses.Infrastructure.Resilience;

public interface IResiliencePolicies
{
    ResiliencePipeline DbWrite { get; }
    ResiliencePipeline Redis { get; }
}

public sealed class ResiliencePolicies : IResiliencePolicies
{
    public ResiliencePipeline DbWrite { get; }
    public ResiliencePipeline Redis { get; }

    public ResiliencePolicies()
    {
        DbWrite = CreateDefaultDbWrite();
        Redis   = CreateDefaultRedis();
    }

    // Return the interface type
    public static IResiliencePolicies CreateDefault() => new ResiliencePolicies();

    public static ResiliencePipeline CreateDefaultRedis()
    {
        var builder = new ResiliencePipelineBuilder();

        builder.AddRetry(new RetryStrategyOptions
        {
            MaxRetryAttempts = 3,
            Delay = TimeSpan.FromMilliseconds(80),
            BackoffType = DelayBackoffType.Exponential,
            UseJitter = true,
            ShouldHandle = new PredicateBuilder()
                .Handle<TimeoutException>()
                .Handle<Exception>() // covers Redis transient exceptions broadly
        });

        builder.AddCircuitBreaker(new CircuitBreakerStrategyOptions
        {
            FailureRatio = 0.5,
            SamplingDuration = TimeSpan.FromSeconds(10),
            MinimumThroughput = 4,
            BreakDuration = TimeSpan.FromSeconds(5)
        });

        return builder.Build();
    }

    private static ResiliencePipeline CreateDefaultDbWrite()
    {
        var builder = new ResiliencePipelineBuilder();

        builder.AddRetry(new RetryStrategyOptions
        {
            MaxRetryAttempts = 3,
            Delay = TimeSpan.FromMilliseconds(100),
            BackoffType = DelayBackoffType.Exponential,
            UseJitter = true,
            ShouldHandle = static args =>
            {
                var ex = args.Outcome.Exception;
                if (ex is null) return ValueTask.FromResult(false);

                if (IsUniqueConstraintViolation(ex)) return ValueTask.FromResult(false); // never retry duplicates

                if (ex is TimeoutException) return ValueTask.FromResult(true);
                if (ex is DbUpdateConcurrencyException) return ValueTask.FromResult(true);
                if (ex is DbUpdateException) return ValueTask.FromResult(true);
                if (ex is DbException) return ValueTask.FromResult(true);
                if (IsSqliteBusyOrLocked(ex)) return ValueTask.FromResult(true);

                return ValueTask.FromResult(false);
            }
        });

        return builder.Build();
    }

    private static bool IsSqliteBusyOrLocked(Exception ex)
    {
        for (var e = ex; e is not null; e = e.InnerException!)
        {
            if (e is SqliteException se && (se.SqliteErrorCode is 5 or 6)) // BUSY/LOCKED
                return true;
        }
        return false;
    }

    private static bool IsUniqueConstraintViolation(Exception ex)
    {
        // SQLite: 19 (SQLITE_CONSTRAINT)
        for (var e = ex; e is not null; e = e.InnerException!)
        {
            if (e is SqliteException s && s.SqliteErrorCode == 19)
                return true;

            var typeName = e.GetType().Name;
            if (typeName is "SqlException")
            {
                var numberProp = e.GetType().GetProperty("Number");
                if (numberProp?.GetValue(e) is int number && (number == 2601 || number == 2627))
                    return true;
            }

            // PostgreSQL: 23505
            var sqlStateProp = e.GetType().GetProperty("SqlState");
            if (sqlStateProp?.GetValue(e) is string state && state == "23505")
                return true;
        }
        return false;
    }
}
