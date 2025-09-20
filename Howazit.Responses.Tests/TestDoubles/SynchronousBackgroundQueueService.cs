using System.Text.Json;
using Howazit.Responses.Application.Abstractions;
using Howazit.Responses.Application.Models;
using Howazit.Responses.Domain.Entities; // adjust if your entity namespace differs
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace Howazit.Responses.Tests.TestDoubles;

/// <summary>
/// Test-only implementation that processes ingestion inline (no background worker).
/// </summary>
public sealed class SynchronousBackgroundQueueService(
    IServiceScopeFactory scopeFactory,
    ILogger<SynchronousBackgroundQueueService> logger)
    : IBackgroundQueueService<IngestRequest> {
    public async ValueTask EnqueueAsync(IngestRequest item, CancellationToken ct = default) {
        using var scope = scopeFactory.CreateScope();

        var repository = scope.ServiceProvider.GetRequiredService<IResponseRepository>();
        var aggregates = scope.ServiceProvider.GetRequiredService<IRealtimeAggregateStore>();

        var nps = item.Responses.NpsScore.GetValueOrDefault();

        var entity = new SurveyResponse {
            SurveyId = item.SurveyId,
            ClientId = item.ClientId,
            ResponseId = item.ResponseId,
            NpsScore = nps,
            Satisfaction = item.Responses.Satisfaction,
            CustomFieldsJson = item.Responses.CustomFields is null
                ? "{}"
                : JsonSerializer.Serialize(item.Responses.CustomFields),
            Timestamp = item.Metadata.Timestamp,
            UserAgent = item.Metadata.UserAgent,
            IpAddress = item.Metadata.IpAddress
        };

        var added = await repository.TryAddAsync(entity, ct).ConfigureAwait(false);

        if (added) {
            await aggregates.UpdateNpsAsync(item.ClientId, nps, ct).ConfigureAwait(false); // <-- int
            Logger.ProcessedInline(logger, item.ClientId, item.ResponseId);
        }
        else {
            Logger.DuplicateIgnored(logger, item.ClientId, item.ResponseId);
        }
    }
}

internal static partial class Logger {
    [LoggerMessage(EventId = 1001, Level = LogLevel.Debug,
        Message = "Processed {ClientId}/{ResponseId} inline in tests.")]
    public static partial void ProcessedInline(ILogger logger, string clientId, string responseId);

    [LoggerMessage(EventId = 1002, Level = LogLevel.Debug,
        Message = "Duplicate {ClientId}/{ResponseId} ignored.")]
    public static partial void DuplicateIgnored(ILogger logger, string clientId, string responseId);
}