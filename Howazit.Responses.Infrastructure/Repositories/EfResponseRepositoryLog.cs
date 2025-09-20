using Microsoft.Extensions.Logging;

namespace Howazit.Responses.Infrastructure.Repositories;

/// <summary>
/// Source-generated logging for EfResponseRepository.
/// Using extension methods keeps call sites terse: _logger.Method(...).
/// </summary>
internal static partial class EfResponseRepositoryLog
{
    // DEBUG: duplicate (idempotent no-op)
    [LoggerMessage(1000, LogLevel.Debug, "Duplicate response {clientId}/{responseId} ignored.")]
    public static partial void DuplicateResponseIgnored(ILogger logger, string clientId, string responseId);

    // WARNING: transient sqlite error with retry
    [LoggerMessage(1001, LogLevel.Warning, "Transient SQLite error (attempt {attempt}/{max}). Delaying {delay} then retrying.")]
    public static partial void TransientSqlite(ILogger logger, Exception exception, int attempt, int max, TimeSpan delay);

    // WARNING: save canceled (timing), retry
    [LoggerMessage(1002, LogLevel.Warning, "Save canceled (attempt {attempt}/{max}). Delaying {delay} then retrying.")]
    public static partial void SaveCanceled(ILogger logger, Exception exception, int attempt, int max, TimeSpan delay);

    // ERROR: gave up after all attempts
    [LoggerMessage(1003, LogLevel.Error, "Failed to insert response {clientId}/{responseId} after {maxAttempts} attempts.")]
    public static partial void InsertFailedAfterRetries(ILogger logger, string clientId, string responseId, int maxAttempts);
}