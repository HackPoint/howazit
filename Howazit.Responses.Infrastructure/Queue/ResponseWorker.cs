using Howazit.Responses.Application.Models;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace Howazit.Responses.Infrastructure.Queue;

public sealed class ResponseWorker : BackgroundService {
    private readonly BackgroundQueueService<IngestRequest> _queue;
    private readonly ILogger<ResponseWorker> _logger;

    public ResponseWorker(BackgroundQueueService<IngestRequest> queue, ILogger<ResponseWorker> logger) {
        _queue = queue;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken) {
        Logs.ResponseWorkerStarted(_logger);
        await foreach (var item in _queue.Reader.ReadAllAsync(stoppingToken)) {
            // Will do dual-storage +  + NPS aggregation.
            Logs.DequeuedResponse(_logger, item.ClientId, item.ResponseId, item.SurveyId);

            // Simulate processing time lightly to observe async behavior
            await Task.Delay(10, stoppingToken);
        }

        Logs.ResponseWorkerStopped(_logger);
    }
}