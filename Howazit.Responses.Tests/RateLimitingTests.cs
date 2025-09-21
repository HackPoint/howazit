using System.Globalization;
using System.Net;
using System.Net.Http.Json;
using FluentAssertions;
using Howazit.Responses.Application.Models;
using Microsoft.Extensions.Configuration;
using Xunit;

namespace Howazit.Responses.Tests;

public class RateLimitingTests(CustomWebAppFactory factory) : IClassFixture<CustomWebAppFactory> {
    private static IngestRequest MakeDto(string clientId, string responseId) => new() {
        SurveyId = "s-rl",
        ClientId = clientId,
        ResponseId = responseId,
        Responses = new ResponsesPayload {
            NpsScore = 10,
            Satisfaction = "ok",
            CustomFields = new Dictionary<string, object?> { ["k"] = "v" }
        },
        Metadata = new MetadataPayload {
            Timestamp = DateTimeOffset.UtcNow,
            UserAgent = "tests",
            IpAddress = "1.1.1.1"
        }
    };

    [Fact]
    public async Task EnforcesLimitAndReturns429WithHeaders() {
        var client = Factory();

        var clientId = $"rl-{Guid.NewGuid():N}";

        async Task<HttpResponseMessage> PostOnce(string id) {
            var dto = new IngestRequest {
                SurveyId = "s-rl",
                ClientId = clientId,
                ResponseId = id,
                Responses = new ResponsesPayload { NpsScore = 10 },
                Metadata = new MetadataPayload { Timestamp = DateTimeOffset.UtcNow }
            };

            using var req = new HttpRequestMessage(HttpMethod.Post, "/v1/responses");
            req.Content = JsonContent.Create(dto);

            // Make sure the partition key resolver picks this (you already validate header/payload match)
            req.Headers.Add("X-Client-Id", clientId);

            return await client.SendAsync(req);
        }

        var r1 = await PostOnce("r-1"); // should be 202
        var r2 = await PostOnce("r-2"); // should be 429 (window=10s, limit=1)

        r1.StatusCode.Should().Be(HttpStatusCode.Accepted);
        r2.StatusCode.Should().Be(HttpStatusCode.TooManyRequests);

        // headers from OnRejected
        r2.Headers.Should().ContainKey("Retry-After");
        r2.Headers.Should().ContainKey("RateLimit-Reset");
    }

    [Fact]
    public async Task ResetsAfterWindow() {
        // Create a client with a tiny, deterministic RL config just for this test
        var clientId = $"rl-{Guid.NewGuid():N}";
        var client = Factory(clientId);
        
        // First request -> allowed
        var r1 = await client.PostAsJsonAsync("/v1/responses", MakeDto(clientId, "r-a-1"));
        r1.StatusCode.Should().Be(HttpStatusCode.Accepted);

        // Second immediately -> 429 (use headers to learn when to retry)
        var r2 = await client.PostAsJsonAsync("/v1/responses", MakeDto(clientId, "r-a-2"));
        r2.StatusCode.Should().Be(HttpStatusCode.TooManyRequests);

        await DelayUntilResetAsync(r2);

        // After reset -> allowed again
        var r3 = await client.PostAsJsonAsync("/v1/responses", MakeDto(clientId, "r-b-1"));
        r3.StatusCode.Should().Be(HttpStatusCode.Accepted);
    }


    [Fact]
    public async Task PerClientIsolation() {
        var client = factory.CreateClient();
        var clientA = $"rl-A-{Guid.NewGuid():N}";
        var clientB = $"rl-B-{Guid.NewGuid():N}";

        // Client A floods
        var aResults = await Task.WhenAll(Enumerable.Range(0, 12).Select(async i => {
            var dto = MakeDto(clientA, $"ra-{i}");
            using var req = new HttpRequestMessage(HttpMethod.Post, "/v1/responses");
            req.Content = JsonContent.Create(dto);
            req.Headers.Add("X-Client-Id", clientA);
            return (await client.SendAsync(req)).StatusCode;
        }));

        // Client B should still be allowed even if A is limited
        var dtoB = MakeDto(clientB, "rb-1");
        using var reqB = new HttpRequestMessage(HttpMethod.Post, "/v1/responses");
        reqB.Content = JsonContent.Create(dtoB);
        reqB.Headers.Add("X-Client-Id", clientB);
        var respB = await client.SendAsync(reqB);

        aResults.Count(c => c == HttpStatusCode.TooManyRequests).Should().BeGreaterThan(0);
        respB.StatusCode.Should().Be(HttpStatusCode.Accepted);
    }

    private HttpClient Factory(string clientId = "rl-test-client") {
        var client = factory.WithWebHostBuilder(b => {
            b.ConfigureAppConfiguration((ctx, cfg) => {
                cfg.AddInMemoryCollection(new Dictionary<string, string?> {
                    ["RATELIMIT__ENABLED"] = "true",
                    ["RATELIMIT__PERMIT_LIMIT"] = "1",
                    ["RATELIMIT__WINDOW_MS"] = "30000",
                    ["RATELIMIT__SEGMENTS"] = "1",
                    ["RATELIMIT__QUEUE_LIMIT"] = "0",
                });
            });
        }).CreateClient();
        client.DefaultRequestHeaders.Add("X-Client-Id", clientId);

        return client;
    }

    private static async Task DelayUntilResetAsync(HttpResponseMessage response) {
        // Prefer standard "Retry-After" if present (delta-seconds or HTTP-date)
        if (response.Headers.TryGetValues("Retry-After", out var raVals)) {
            var raw = raVals.FirstOrDefault();
            if (!string.IsNullOrWhiteSpace(raw)) {
                if (int.TryParse(raw, NumberStyles.Integer, CultureInfo.InvariantCulture, out var seconds)) {
                    await Task.Delay(TimeSpan.FromSeconds(seconds) + TimeSpan.FromMilliseconds(150));
                    return;
                }

                if (DateTimeOffset.TryParse(raw, CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal,
                        out var when)) {
                    var delay = when - DateTimeOffset.UtcNow;
                    if (delay < TimeSpan.Zero) delay = TimeSpan.Zero;
                    await Task.Delay(delay + TimeSpan.FromMilliseconds(150));
                    return;
                }
            }
        }

        // Fallback: "RateLimit-Reset" (some middlewares emit seconds or milliseconds)
        if (response.Headers.TryGetValues("RateLimit-Reset", out var rlVals)) {
            var raw = rlVals.FirstOrDefault();
            if (!string.IsNullOrWhiteSpace(raw)) {
                if (double.TryParse(raw, NumberStyles.Float, CultureInfo.InvariantCulture, out var seconds)) {
                    await Task.Delay(TimeSpan.FromSeconds(seconds) + TimeSpan.FromMilliseconds(150));
                    return;
                }

                if (long.TryParse(raw, NumberStyles.Integer, CultureInfo.InvariantCulture, out var ms)) {
                    await Task.Delay(TimeSpan.FromMilliseconds(ms) + TimeSpan.FromMilliseconds(150));
                    return;
                }
            }
        }

        // Last-resort buffer if headers are missing
        await Task.Delay(600);
    }
}