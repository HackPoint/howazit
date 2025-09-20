using Howazit.Responses.Application.Abstractions;
using Howazit.Responses.Infrastructure.Resilience;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Polly;
using Polly.CircuitBreaker;
using StackExchange.Redis;

namespace Howazit.Responses.Infrastructure.Realtime;

public class RedisAggregateStore : IRealtimeAggregateStore {
    private readonly IConnectionMultiplexer _redis;
    private readonly ILogger<RedisAggregateStore> _logger;
    private readonly ResiliencePipeline _pipeline;

    // Convenience: default policies + null logger
    public RedisAggregateStore(IConnectionMultiplexer redis)
        : this(redis, ResiliencePolicies.CreateDefault(), NullLogger<RedisAggregateStore>.Instance) { }

    // Convenience: default policies with custom logger
    public RedisAggregateStore(IConnectionMultiplexer redis, ILogger<RedisAggregateStore> logger)
        : this(redis, ResiliencePolicies.CreateDefault(), logger) { }

    // Primary ctor uses the interface
    public RedisAggregateStore(
        IConnectionMultiplexer redis,
        IResiliencePolicies policies,
        ILogger<RedisAggregateStore> logger) {
        _redis = redis ?? throw new ArgumentNullException(nameof(redis));
        _logger = logger ?? NullLogger<RedisAggregateStore>.Instance;
        _pipeline = (policies ?? ResiliencePolicies.CreateDefault()).Redis;
    }

    private static string Key(string clientId) => $"client:{clientId}:nps";

    public async Task<NpsSnapshot> GetNpsAsync(string clientId, CancellationToken ct = default) {
        var db = _redis.GetDatabase();
        var key = Key(clientId);

        try {
            // wrap with your existing resiliency pipeline, e.g. _pipeline.ExecuteAsync(...)
            var vals = await _pipeline.ExecuteAsync(
                async _ => await db
                    .HashGetAsync(key, ["promoters", "passives", "detractors", "total"])
                    .ConfigureAwait(false),
                ct).ConfigureAwait(false);

            // Parse deterministically
            static int V(RedisValue v) => v.IsNull ? 0 : (int)(long)v;

            var prom = V(vals[0]);
            var pass = V(vals[1]);
            var detr = V(vals[2]);
            var total = V(vals[3]);

            var nps = total == 0 ? 0 : ((prom * 100.0 / total) - (detr * 100.0 / total));
            return new NpsSnapshot(prom, pass, detr, total, Math.Round(nps, 2));
        }
        catch (BrokenCircuitException bce) {
            Logs.RedisCircuitOpen(_logger, bce);
            return EmptySnapshot;
        }
        catch (RedisConnectionException rce) {
            Logs.RedisConnOrAuth(_logger, rce);
            return EmptySnapshot;
        }
        catch (RedisTimeoutException rte) {
            Logs.RedisTimeout(_logger, rte);
            return EmptySnapshot;
        }
        catch (Exception ex) {
            Logs.RedisError(_logger, ex);
            return EmptySnapshot;
        }
    }

    private static NpsSnapshot EmptySnapshot => new(0, 0, 0, 0, 0);

    public async Task UpdateNpsAsync(string clientId, int npsScore, CancellationToken ct = default) {
        var db = _redis.GetDatabase();
        var key = Key(clientId);
        var bucket = npsScore switch {
            >= 9 and <= 10 => "promoters",
            >= 7 and <= 8 => "passives",
            _ => "detractors"
        };

        try {
            // wrap with your existing resiliency pipeline, e.g. _pipeline.ExecuteAsync(...)
            await _pipeline.ExecuteAsync(async _ => {
                var t1 = db.HashIncrementAsync(key, bucket, 1);
                var t2 = db.HashIncrementAsync(key, "total", 1);
                await Task.WhenAll(t1, t2).ConfigureAwait(false);
            }, ct).ConfigureAwait(false);
        }
        catch (BrokenCircuitException bce) {
            Logs.RedisCircuitOpen(_logger, bce);
            // swallow: metrics will be stale but API stays up
        }
        catch (RedisConnectionException rce) {
            Logs.RedisConnOrAuth(_logger, rce);
        }
        catch (RedisTimeoutException rte) {
            Logs.RedisTimeout(_logger, rte);
        }
        catch (Exception ex) {
            Logs.RedisError(_logger, ex);
        }
    }
}

static partial class Logs {
    [LoggerMessage(EventId = 1001, Level = LogLevel.Error,
        Message = "Redis circuit open. Returning empty snapshot.")]
    public static partial void RedisCircuitOpen(ILogger logger, Exception ex);

    [LoggerMessage(EventId = 1002, Level = LogLevel.Error,
        Message = "Redis connection/auth error. Returning empty snapshot.")]
    public static partial void RedisConnOrAuth(ILogger logger, Exception ex);

    [LoggerMessage(EventId = 1003, Level = LogLevel.Warning,
        Message = "Redis timeout while fetching metrics.")]
    public static partial void RedisTimeout(ILogger logger, Exception ex);

    [LoggerMessage(EventId = 1004, Level = LogLevel.Error,
        Message = "Unexpected Redis error while fetching metrics.")]
    public static partial void RedisError(ILogger logger, Exception ex);
}