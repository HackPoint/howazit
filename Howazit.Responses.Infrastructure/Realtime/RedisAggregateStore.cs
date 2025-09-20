using Howazit.Responses.Application.Abstractions;
using Howazit.Responses.Infrastructure.Resilience;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Polly;
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

    public async Task UpdateNpsAsync(string clientId, int npsScore, CancellationToken ct = default) {
        var db = _redis.GetDatabase();
        var key = Key(clientId);

        var bucket = npsScore switch {
            >= 9 and <= 10 => "promoters",
            >= 7 and <= 8 => "passives",
            _ => "detractors"
        };

        await _pipeline.ExecuteAsync(async _ => {
            var t1 = db.HashIncrementAsync(key, bucket, 1);
            var t2 = db.HashIncrementAsync(key, "total", 1);
            await Task.WhenAll(t1, t2).ConfigureAwait(false);
        }, ct).ConfigureAwait(false);
    }

    public async Task<NpsSnapshot> GetNpsAsync(string clientId, CancellationToken ct = default) {
        var db = _redis.GetDatabase();
        var key = Key(clientId);

        var vals = await _pipeline.ExecuteAsync(async _ =>
            await db.HashGetAsync(key, new RedisValue[] { "promoters", "passives", "detractors", "total" })
                .ConfigureAwait(false), ct).ConfigureAwait(false);

        var prom = (int)(vals[0].IsNull ? 0 : (long)vals[0]);
        var pass = (int)(vals[1].IsNull ? 0 : (long)vals[1]);
        var detr = (int)(vals[2].IsNull ? 0 : (long)vals[2]);
        var total = (int)(vals[3].IsNull ? 0 : (long)vals[3]);

        var nps = total == 0 ? 0 : ((prom * 100.0 / total) - (detr * 100.0 / total));
        return new NpsSnapshot(prom, pass, detr, total, Math.Round(nps, 2));
    }
}