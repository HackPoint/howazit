using Microsoft.Extensions.Logging;

namespace Howazit.Responses.Infrastructure.Queue;

internal static partial class Logs
{
    [LoggerMessage(EventId = 1000, Level = LogLevel.Information, Message = "ResponseWorker started")]
    public static partial void ResponseWorkerStarted(ILogger logger);

    [LoggerMessage(EventId = 1001, Level = LogLevel.Information, Message = "ResponseWorker stopped")]
    public static partial void ResponseWorkerStopped(ILogger logger);

    [LoggerMessage(EventId = 1002, Level = LogLevel.Information, Message = "Dequeued response {ClientId}/{ResponseId} (survey {SurveyId})")]
    public static partial void DequeuedResponse(ILogger logger, string clientId, string responseId, string surveyId);
    
    [LoggerMessage(EventId = 1003, Level = LogLevel.Information, Message = "Stored response and updated aggregates for {ClientId}/{ResponseId} (NPS {Nps})")]
    public static partial void StoredAndAggregated(ILogger logger, string clientId, string responseId, int nps);

    [LoggerMessage(EventId = 1005, Level = LogLevel.Debug, Message = "Duplicate response skipped (idempotent) for {ClientId}/{ResponseId}")]
    public static partial void IdempotentSkip(ILogger logger, string clientId, string responseId);

    [LoggerMessage(EventId = 1006, Level = LogLevel.Error, Message = "Error processing response")]
    public static partial void ProcessingError(ILogger logger, Exception exception);
}