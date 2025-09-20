using StackExchange.Redis;

namespace Howazit.Responses.Infrastructure.Realtime;

public sealed class StackExchangeRedisClient(IConnectionMultiplexer mux) : IRedisClient {
    public async Task HashIncrementAsync(string key, string field, long value, CancellationToken ct = default) {
        var db = mux.GetDatabase();
        _ = await db.HashIncrementAsync(key, field, value).ConfigureAwait(false);
    }

    public async Task<Dictionary<string, long>> HashGetAllAsync(string key, CancellationToken ct = default) {
        var db = mux.GetDatabase();
        var entries = await db.HashGetAllAsync(key).ConfigureAwait(false);
        var dict = new Dictionary<string, long>(StringComparer.Ordinal);

        foreach (var e in entries) {
            if (long.TryParse(e.Value, out var v))
                dict[e.Name!] = v;
        }

        return dict;
    }
}