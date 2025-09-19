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
}