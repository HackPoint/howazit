using System.Net.Http.Json;
using System.Text.Json;
using FluentAssertions;
using Howazit.Responses.Application.Abstractions;
using Howazit.Responses.Application.Models;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Howazit.Responses.Tests;

public class MetricsEndpointTests : IClassFixture<CustomWebAppFactory> {
    private readonly CustomWebAppFactory _factory;

    public MetricsEndpointTests(CustomWebAppFactory factory) {
        _factory = factory;

        // Guards: ensure test doubles are actually wired
        using var scope = _factory.Services.CreateScope();
        scope.ServiceProvider.GetRequiredService<IBackgroundQueueService<IngestRequest>>()
            .GetType().Name.Should().Be("SynchronousBackgroundQueueService");
        scope.ServiceProvider.GetRequiredService<IRealtimeAggregateStore>()
            .GetType().Name.Should().Be("InMemoryAggregateStore");
    }

    [Fact]
    public async Task GetNpsEmptyClientReturnsZeros() {
        var client = _factory.CreateClient();
        var clientId = $"acme-empty-{Guid.NewGuid():N}";

        var resp = await client.GetAsync($"/v1/metrics/nps/{clientId}");
        resp.IsSuccessStatusCode.Should().BeTrue();

        var json = await resp.Content.ReadAsStringAsync();
        using var doc = JsonDocument.Parse(json);
        var root = doc.RootElement;

        root.GetProperty("clientId").GetString().Should().Be(clientId);
        root.GetProperty("promoters").GetInt32().Should().Be(0);
        root.GetProperty("passives").GetInt32().Should().Be(0);
        root.GetProperty("detractors").GetInt32().Should().Be(0);
        root.GetProperty("total").GetInt32().Should().Be(0);
        root.GetProperty("nps").GetDouble().Should().Be(0.0);
    }

    [Fact]
    public async Task PostPromoterThenMetricsShowOnePromoter() {
        var client = _factory.CreateClient();

        var clientId = $"acme-{Guid.NewGuid():N}";
        const string surveyId = "s-42";
        const string responseId = "r-1";

        var dto = new IngestRequest {
            SurveyId = surveyId,
            ClientId = clientId,
            ResponseId = responseId,
            Responses = new ResponsesPayload {
                NpsScore = 10,
                Satisfaction = "great",
                CustomFields = new Dictionary<string, object?> { ["source"] = "web" }
            },
            Metadata = new MetadataPayload {
                Timestamp = DateTimeOffset.UtcNow,
                UserAgent = "tests",
                IpAddress = "1.1.1.1"
            }
        };

        var post = await client.PostAsJsonAsync("/v1/responses", dto);
        post.IsSuccessStatusCode.Should().BeTrue();

        // Read back via the public metrics endpoint (same DI path as production)
        var resp = await client.GetAsync($"/v1/metrics/nps/{clientId}");
        resp.IsSuccessStatusCode.Should().BeTrue();

        var json = await resp.Content.ReadAsStringAsync();
        using var doc = JsonDocument.Parse(json);
        var root = doc.RootElement;

        root.GetProperty("clientId").GetString().Should().Be(clientId);
        root.GetProperty("promoters").GetInt32().Should().Be(1);
        root.GetProperty("passives").GetInt32().Should().Be(0);
        root.GetProperty("detractors").GetInt32().Should().Be(0);
        root.GetProperty("total").GetInt32().Should().Be(1);
        Math.Abs(root.GetProperty("nps").GetDouble() - 100.0).Should().BeLessThan(0.01);
    }
}