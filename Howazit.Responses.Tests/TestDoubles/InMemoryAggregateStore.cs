using System.Collections.Concurrent;
using Howazit.Responses.Application.Abstractions;

namespace Howazit.Responses.Tests.TestDoubles;

public class InMemoryAggregateStore : IRealtimeAggregateStore {
    private sealed record Counts(int Prom, int Pass, int Detr, int Total);

    private readonly ConcurrentDictionary<string, Counts> _map = new();

    public Task UpdateNpsAsync(string clientId, int npsScore, CancellationToken ct = default) {
        _map.AddOrUpdate(clientId, _ => {
                var (p, pa, d) = Bucket(npsScore);
                return new Counts(p, pa, d, 1);
            },
            (_, old) => {
                var (p, pa, d) = Bucket(npsScore);
                return new Counts(old.Prom + p, old.Pass + pa, old.Detr + d, old.Total + 1);
            });

        return Task.CompletedTask;
    }

    public Task<NpsSnapshot> GetNpsAsync(string clientId, CancellationToken ct = default) {
        var c = _map.GetOrAdd(clientId, _ => new Counts(0, 0, 0, 0));
        var nps = c.Total == 0 ? 0 : ((c.Prom * 100.0 / c.Total) - (c.Detr * 100.0 / c.Total));
        return Task.FromResult(new NpsSnapshot(c.Prom, c.Pass, c.Detr, c.Total, Math.Round(nps, 2)));
    }

    private static (int p, int pa, int d) Bucket(int s) =>
        s switch {
            >= 9 and <= 10 => (1, 0, 0),
            >= 7 and <= 8 => (0, 1, 0),
            _ => (0, 0, 1)
        };
}