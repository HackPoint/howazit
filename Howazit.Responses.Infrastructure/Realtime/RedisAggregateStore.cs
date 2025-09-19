using Howazit.Responses.Application.Abstractions;
using StackExchange.Redis;

namespace Howazit.Responses.Infrastructure.Realtime;

public class RedisAggregateStore(IConnectionMultiplexer redis) : IRealtimeAggregateStore {
    private static string Key(string clientId) => $"client:{clientId}:nps";

    public async Task UpdateNpsAsync(string clientId, int npsScore, CancellationToken ct = default) {
        var db = redis.GetDatabase();
        var key = Key(clientId);

        string bucket = npsScore switch {
            >= 9 and <= 10 => "promoters",
            >= 7 and <= 8 => "passives",
            _ => "detractors"
        };

        // atomic increments
        var tasks = new Task[] {
            db.HashIncrementAsync(key, bucket, 1),
            db.HashIncrementAsync(key, "total", 1)
        };
        await Task.WhenAll(tasks);
    }

    public async Task<NpsSnapshot> GetNpsAsync(string clientId, CancellationToken ct = default) {
        var db = redis.GetDatabase();
        var key = Key(clientId);

        var vals = await db.HashGetAsync(key, ["promoters", "passives", "detractors", "total"]);
        var prom = (int)(vals[0].IsNull ? 0 : (long)vals[0]);
        var pass = (int)(vals[1].IsNull ? 0 : (long)vals[1]);
        var detr = (int)(vals[2].IsNull ? 0 : (long)vals[2]);
        var total = (int)(vals[3].IsNull ? 0 : (long)vals[3]);

        var nps = total == 0 ? 0 : ((prom * 100.0 / total) - (detr * 100.0 / total));
        return new NpsSnapshot(prom, pass, detr, total, Math.Round(nps, 2));
    }
}