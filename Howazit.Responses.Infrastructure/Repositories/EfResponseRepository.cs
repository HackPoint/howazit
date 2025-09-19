using Howazit.Responses.Application.Abstractions;
using Howazit.Responses.Domain.Entities;
using Howazit.Responses.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace Howazit.Responses.Infrastructure.Repositories;

public partial class EfResponseRepository(ResponsesDbContext db, ILogger<EfResponseRepository> logger)
    : IResponseRepository {
    private readonly ResponsesDbContext _db = db;
    private readonly ILogger<EfResponseRepository> _logger = logger;

    public Task<bool> ExistsAsync(string clientId, string responseId, CancellationToken ct = default)
        => _db.SurveyResponses.AnyAsync(r => r.ClientId == clientId && r.ResponseId == responseId, ct);

    public async Task<bool> TryAddAsync(SurveyResponse entity, CancellationToken ct = default) {
        _db.SurveyResponses.Add(entity);
        try {
            await _db.SaveChangesAsync(ct);
            return true;
        }
        catch (DbUpdateException ex) {
            // Unique index violation => idempotent duplicate
            LogDuplicateResponseClientidResponseid(ex, entity.ClientId, entity.ResponseId);
            _db.ChangeTracker.Clear();
            return false;
        }
    }

    public Task<int> CountForClientAsync(string clientId, CancellationToken ct = default)
        => _db.SurveyResponses.Where(r => r.ClientId == clientId).CountAsync(ct);

    
    [LoggerMessage(LogLevel.Debug, "Duplicate response {clientId}/{responseId}")]
    partial void LogDuplicateResponseClientidResponseid(DbUpdateException dbUpdateException, string clientId,
        string responseId);
}