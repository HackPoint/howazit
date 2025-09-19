namespace Howazit.Responses.Application.Abstractions;

public interface IRealtimeAggregateStore {
    Task UpdateNpsAsync(string clientId, int npsScore, CancellationToken ct = default);
    Task<NpsSnapshot> GetNpsAsync(string clientId, CancellationToken ct = default);
}

public sealed record NpsSnapshot(int Promoters, int Passives, int Detractors, int Total, double Nps);