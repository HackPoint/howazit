using Howazit.Responses.Api.Responses;

namespace Howazit.Responses.Api.Features.Responses;

public static class ResponseEndpoints {
    public static IEndpointRouteBuilder MapResponseEndpoints(this IEndpointRouteBuilder app) {
        app.MapPost("/v1/responses", ResponseHandlers.PostAsync)
            .WithDescription("Ingest a survey response (async). Returns 202 Accepted.")
            .WithOpenApi();

        return app;
    }
}