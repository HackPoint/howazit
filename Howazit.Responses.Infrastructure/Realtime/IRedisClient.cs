namespace Howazit.Responses.Infrastructure.Realtime;

public interface IRedisClient {
    Task HashIncrementAsync(string key, string field, long value, CancellationToken ct = default);
    Task<Dictionary<string, long>> HashGetAllAsync(string key, CancellationToken ct = default);
}