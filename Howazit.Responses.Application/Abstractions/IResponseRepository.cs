using Howazit.Responses.Domain.Entities;

namespace Howazit.Responses.Application.Abstractions;

public interface IResponseRepository {
    Task<bool> ExistsAsync(string clientId, string responseId, CancellationToken ct = default);
    Task<bool> TryAddAsync(SurveyResponse entity, CancellationToken ct = default);
    Task<int> CountForClientAsync(string clientId, CancellationToken ct = default);
}