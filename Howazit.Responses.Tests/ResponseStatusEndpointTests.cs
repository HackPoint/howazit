using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using FluentAssertions;
using Howazit.Responses.Application.Models;
using Xunit;

namespace Howazit.Responses.Tests;

public class ResponseStatusEndpointTests(CustomWebAppFactory factory) : IClassFixture<CustomWebAppFactory> {
    [Fact]
    public async Task NewResponseEventuallyShowsProcessed() {
        var client = factory.CreateClient();

        var dto = new IngestRequest {
            SurveyId = "s-42",
            ClientId = "waystar",
            ResponseId = "r-abc",
            Responses = new ResponsesPayload {
                NpsScore = 10, Satisfaction = "great",
                CustomFields = new Dictionary<string, object?> { ["src"] = "test" }
            },
            Metadata = new MetadataPayload {
                Timestamp = DateTimeOffset.UtcNow,
                UserAgent = "tests",
                IpAddress = "1.1.1.1"
            }
        };

        var post = await client.PostAsJsonAsync("/v1/responses", dto);
        post.StatusCode.Should().Be(HttpStatusCode.Accepted);

        // Since tests use the synchronous queue impl, this should be processed immediately.
        var doc = await client.GetFromJsonAsync<JsonElement>(
            "/v1/responses/waystar/r-abc/status");

        doc.TryGetProperty("status", out var statusProp).Should().BeTrue("status field must exist");
        statusProp.GetString().Should().Be("processed");
    }

    [Fact]
    public async Task UnknownResponseShowsPending() {
        var client = factory.CreateClient();
        var doc = await client.GetFromJsonAsync<JsonElement>(
            "/v1/responses/foo/bar/status");

        doc.TryGetProperty("status", out var statusProp).Should().BeTrue("status field must exist");
        statusProp.GetString().Should().Be("pending");
    }
}