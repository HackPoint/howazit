using System.Diagnostics;

namespace Howazit.Responses.Api.Telemetry;

public static class TelemetryEnricher {
    public static void EnrichWithHttpRequest(Activity activity, HttpRequest request) {
        if (activity is null || request is null) return;

        var conn = request.HttpContext.Connection;

        activity.SetTag("client.address", conn.RemoteIpAddress?.ToString());
        activity.SetTag("client.port", conn.RemotePort);
        activity.SetTag("user_agent.original", request.Headers.UserAgent.ToString());
        activity.SetTag("http.request_content_length", request.ContentLength);

        if (request.Headers.TryGetValue("X-Client-Id", out var cid))
            activity.SetTag("http.request.header.x_client_id", cid.ToString());

        if (request.HttpContext.GetEndpoint() is RouteEndpoint route)
            activity.SetTag("http.route", route.RoutePattern.RawText);
    }

    public static void EnrichWithHttpResponse(Activity activity, HttpResponse response) {
        if (activity is null || response is null) return;

        activity.SetTag("http.response_content_length", response.ContentLength);

        if (response.Headers.TryGetValue("Retry-After", out var retryAfter))
            activity.SetTag("http.response.header.retry_after", retryAfter.ToString());
    }

    public static void EnrichWithException(Activity activity, Exception exception) {
        if (activity is null || exception is null) return;

        activity.SetTag("exception.type", exception.GetType().FullName);
        activity.SetTag("exception.message", exception.Message);
        activity.SetTag("exception.stacktrace", exception.StackTrace);
    }
}