using System.Text.Json;
using Howazit.Responses.Application.Abstractions;
using Howazit.Responses.Application.Models;
using Howazit.Responses.Domain.Entities;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace Howazit.Responses.Infrastructure.Queue;

public sealed class ResponseWorker : BackgroundService {
    private readonly BackgroundQueueService<IngestRequest> _queue;
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ILogger<ResponseWorker> _logger;

    public ResponseWorker(
        BackgroundQueueService<IngestRequest> queue,
        IServiceScopeFactory scopeFactory,
        ILogger<ResponseWorker> logger) {
        _queue = queue;
        _scopeFactory = scopeFactory;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken) {
        Logs.ResponseWorkerStarted(_logger);

        await foreach (var dto in _queue.Reader.ReadAllAsync(stoppingToken)) {
            using var scope = _scopeFactory.CreateScope();
            var repo = scope.ServiceProvider.GetRequiredService<IResponseRepository>();
            var agg = scope.ServiceProvider.GetRequiredService<IRealtimeAggregateStore>();

            try {
                var entity = new SurveyResponse {
                    ClientId = dto.ClientId,
                    SurveyId = dto.SurveyId,
                    ResponseId = dto.ResponseId,
                    NpsScore = dto.Responses.NpsScore ?? 0,
                    Satisfaction = dto.Responses.Satisfaction,
                    CustomFieldsJson = dto.Responses.CustomFields is null
                        ? null
                        : JsonSerializer.Serialize(dto.Responses.CustomFields),
                    Timestamp = dto.Metadata.Timestamp,
                    UserAgent = dto.Metadata.UserAgent,
                    IpAddress = dto.Metadata.IpAddress,
                    CreatedAtUtc = DateTimeOffset.UtcNow
                };

                var added = await repo.TryAddAsync(entity, stoppingToken);
                if (!added) {
                    Logs.IdempotentSkip(_logger, dto.ClientId, dto.ResponseId);
                    continue;
                }

                await agg.UpdateNpsAsync(dto.ClientId, entity.NpsScore, stoppingToken);
                Logs.StoredAndAggregated(_logger, dto.ClientId, dto.ResponseId, dto.Responses.NpsScore ?? -1);
            }
            catch (Exception ex) {
                Logs.ProcessingError(_logger, ex);
                // swallow or dead-letter depending on your strategy
            }
        }

        Logs.ResponseWorkerStopped(_logger);
    }
}