using Howazit.Responses.Api.Responses;
using Howazit.Responses.Application.Abstractions;

namespace Howazit.Responses.Api.Features.Responses;

public static class ResponseEndpoints {
    public static IEndpointRouteBuilder MapResponseEndpoints(this IEndpointRouteBuilder app) {
        app.MapPost("/v1/responses", ResponseHandlers.PostAsync)
            .RequireRateLimiting("per-client")
            .WithDescription("Ingest a survey response (async). Returns 202 Accepted.")
            .WithOpenApi();

        app.MapGet("/v1/responses/{clientId}/{responseId}/status",
                async (string clientId, string responseId, IResponseRepository repo, HttpContext http) =>
                {
                    var exists = await repo.ExistsAsync(clientId, responseId, http.RequestAborted);
                    var status = exists ? "processed" : "pending";
                    return Results.Ok(new { clientId, responseId, status });
                })
            .WithName("GetResponseStatus")
            .WithSummary("Gets processing status for a response.")
            .Produces(200);
        
        return app;
    }

}