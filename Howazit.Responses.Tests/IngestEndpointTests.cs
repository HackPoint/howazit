using System.Net;
using System.Net.Http.Json;
using FluentAssertions;
using Howazit.Responses.Application.Models;
using Microsoft.AspNetCore.Mvc.Testing;
using Xunit;

namespace Howazit.Responses.Tests;

public class IngestEndpointTests(WebApplicationFactory<Program> factory)
    : IClassFixture<WebApplicationFactory<Program>> {
    private readonly WebApplicationFactory<Program> _factory = factory.WithWebHostBuilder(_ => { });


    [Fact]
    public async Task PostValidReturns202() {
        var client = _factory.CreateClient();
        var dto = new IngestRequest {
            SurveyId = "s-1",
            ClientId = "acme",
            ResponseId = "r-001",
            Responses = new ResponsesPayload {
                NpsScore = 9,
                Satisfaction = "<b>satisfied</b>",
                CustomFields = new Dictionary<string, object?> { ["free_text"] = "<script>x</script>hello" }
            },
            Metadata = new MetadataPayload {
                Timestamp = DateTimeOffset.UtcNow,
                UserAgent = "curl/8.0",
                IpAddress = "1.2.3.4"
            }
        };

        var resp = await client.PostAsJsonAsync("/v1/responses", dto);
        resp.StatusCode.Should().Be(HttpStatusCode.Accepted);

        var json = await resp.Content.ReadFromJsonAsync<Dictionary<string, string>>();
        json.Should().NotBeNull();
        json!.Should().ContainKey("responseId").WhoseValue.Should().Be("r-001");

        resp.Headers.Location.Should().NotBeNull();
        resp.Headers.Location!.ToString().Should().Contain("/v1/responses/acme/r-001/status");
    }

    [Fact]
    public async Task PostInvalidReturns400ProblemDetails() {
        var client = _factory.CreateClient();
        var bad = new {
            // missing surveyId/clientId/responseId, invalid NPS, bad IP
            responses = new { nps_score = 42 },
            metadata = new { timestamp = DateTimeOffset.UtcNow.AddDays(1), ip_address = "999.999.1.1" }
        };

        var resp = await client.PostAsJsonAsync("/v1/responses", bad);
        resp.StatusCode.Should().Be(HttpStatusCode.BadRequest);

        var body = await resp.Content.ReadAsStringAsync();
        body.Should().Contain("Validation failed");
        body.Should().Contain("surveyId");
        body.Should().Contain("clientId");
        body.Should().Contain("responseId");
        body.Should().Contain("nps_score");
        body.Should().Contain("ip_address");
    }
}