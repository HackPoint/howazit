using Howazit.Responses.Application.Abstractions;

namespace Howazit.Responses.Api.Features.Metrics;

public static class MetricsEndpoints {
    public static IEndpointRouteBuilder MapMetricsEndpoints(this IEndpointRouteBuilder app) {
        var group = app.MapGroup("/v1/metrics").WithTags("Metrics");

        group.MapGet("/nps/{clientId}",
                async (string clientId, IRealtimeAggregateStore store, CancellationToken ct) => {
                    var snapshot = await store.GetNpsAsync(clientId, ct);

                    // Ensure valid JSON number (avoid NaN/Infinity when total == 0)
                    var safeNps = double.IsFinite(snapshot.Nps) ? snapshot.Nps : 0.0;

                    return Results.Ok(new {
                        clientId,
                        promoters = snapshot.Promoters,
                        passives = snapshot.Passives,
                        detractors = snapshot.Detractors,
                        total = snapshot.Total,
                        nps = safeNps
                    });
                })
            .Produces(StatusCodes.Status200OK)
            .WithName("GetNpsByClient")
            .WithOpenApi();

        return app;
    }
}