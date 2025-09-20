using System.Globalization;
using StackExchange.Redis;

namespace Howazit.Responses.Infrastructure.Realtime;

public sealed class StackExchangeRedisClient(IConnectionMultiplexer mux) : IRedisClient {
    public Task HashIncrementAsync(string key, string field, long value, CancellationToken ct = default)
        => mux.GetDatabase().HashIncrementAsync(key, field, value);

    public async Task<Dictionary<string, long>> HashGetAllAsync(string key, CancellationToken ct = default) {
        var db = mux.GetDatabase();
        var entries = await db.HashGetAllAsync(key).ConfigureAwait(false);

        var result = new Dictionary<string, long>(StringComparer.Ordinal);
        foreach (var e in entries) {
            var name = e.Name.ToString();

            // Make the type explicit to avoid the ambiguous overload:
            var s = e.Value.ToString(); // RedisValue -> string (can be null)

            if (!long.TryParse(s, NumberStyles.Integer, CultureInfo.InvariantCulture, out var l)) {
                l = 0; // default if missing / non-numeric
            }

            result[name] = l;
        }

        return result;
    }
}