using Howazit.Responses.Application.Abstractions;
using StackExchange.Redis;

namespace Howazit.Responses.Api.Features.Metrics;

public static class MetricsEndpoints {
    public static IEndpointRouteBuilder MapMetricsEndpoints(this IEndpointRouteBuilder app) {
        app.MapGet("/v1/metrics/nps", () => Results.BadRequest(new { error = "clientId is required" }));
        app.MapGet("/v1/metrics/nps/{clientId}", async (
                string clientId,
                IRealtimeAggregateStore store,
                ILoggerFactory lf,
                CancellationToken ct) => {
                var log = lf.CreateLogger("Metrics");
                try {
                    var s = await store.GetNpsAsync(clientId, ct);
                    return Results.Ok(new {
                        clientId,
                        promoters = s.Promoters,
                        passives = s.Passives,
                        detractors = s.Detractors,
                        total = s.Total,
                        nps = (int)s.Nps
                    });
                }
                catch (RedisConnectionException ex) {
                    log.RedisAuthError(ex);
                    return Results.Ok(new
                        { clientId, promoters = 0, passives = 0, detractors = 0, total = 0, nps = 0 });
                }
                catch (RedisTimeoutException ex) {
                    log.RedisTimeout(ex);
                    return Results.Ok(new
                        { clientId, promoters = 0, passives = 0, detractors = 0, total = 0, nps = 0 });
                }
            })
            .Produces(StatusCodes.Status200OK)
            .WithName("GetNpsByClient")
            .WithOpenApi();

        return app;
    }
}

public static partial class Log {
    [LoggerMessage(EventId = 1001, Level = LogLevel.Error,
        Message = "Redis connection/auth error. Returning empty snapshot.")]
    public static partial void RedisAuthError(this ILogger logger, Exception exception);

    [LoggerMessage(EventId = 1002, Level = LogLevel.Error,
        Message = "Redis timeout. Returning empty snapshot.")]
    public static partial void RedisTimeout(this ILogger logger, Exception exception);

    [LoggerMessage(EventId = 1003, Level = LogLevel.Debug,
        Message = "Processed {ClientId}/{ResponseId} inline in tests.")]
    public static partial void ProcessedInline(this ILogger logger, string clientId, string responseId);

    [LoggerMessage(EventId = 1004, Level = LogLevel.Debug,
        Message = "Duplicate {ClientId}/{ResponseId} ignored.")]
    public static partial void DuplicateIgnored(this ILogger logger, string clientId, string responseId);
}