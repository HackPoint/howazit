using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using FluentAssertions;
using Howazit.Responses.Application.Abstractions;
using Howazit.Responses.Application.Models;
using Howazit.Responses.Infrastructure.Persistence;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Howazit.Responses.Tests;

public class DualStorageIdempotencyTests : IClassFixture<CustomWebAppFactory> {
    private readonly CustomWebAppFactory _factory;

    public DualStorageIdempotencyTests(CustomWebAppFactory factory) {
        _factory = factory;

        using var scope = factory.Services.CreateScope();
        scope.ServiceProvider.GetRequiredService<IBackgroundQueueService<IngestRequest>>()
            .GetType().Name.Should().Be("SynchronousBackgroundQueueService");
        scope.ServiceProvider.GetRequiredService<IRealtimeAggregateStore>()
            .GetType().Name.Should().Be("InMemoryAggregateStore");
    }

    [Fact]
    public async Task DuplicatePostsResultInSingleRowAndSingleAggregateIncrement() {
        var client = _factory.CreateClient();

        // Use a unique client per run to avoid residue between tests
        var clientId = $"waystar-{Guid.NewGuid():N}";
        const string surveyId = "s-42";
        const string responseId = "r-abc";

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

        // First post -> 202 Accepted
        var r1 = await client.PostAsJsonAsync("/v1/responses", dto);
        r1.StatusCode.Should().Be(HttpStatusCode.Accepted);

        // Duplicate post -> 202 Accepted (ignored by repo)
        var r2 = await client.PostAsJsonAsync("/v1/responses", dto);
        r2.StatusCode.Should().Be(HttpStatusCode.Accepted);

        // Verify DB idempotency: only a single row for (clientId, responseId)
        using (var scope = _factory.Services.CreateScope()) {
            var db = scope.ServiceProvider.GetRequiredService<ResponsesDbContext>();
            var count = db.SurveyResponses.Count(r => r.ClientId == clientId && r.ResponseId == responseId);
            count.Should().Be(1);
        }

        // Verify aggregates through the public endpoint (same DI path as the app)
        // Add a tiny eventual wait to accommodate any brief yields in EF/DI.
        var ok = await Eventually(async () => {
            var resp = await client.GetAsync($"/v1/metrics/nps/{clientId}");
            if (!resp.IsSuccessStatusCode) return false;

            var json = await resp.Content.ReadAsStringAsync();
            using var doc = JsonDocument.Parse(json);
            var root = doc.RootElement;

            var promoters = root.GetProperty("promoters").GetInt32();
            var passives = root.GetProperty("passives").GetInt32();
            var detractors = root.GetProperty("detractors").GetInt32();
            var total = root.GetProperty("total").GetInt32();
            var nps = root.GetProperty("nps").GetDouble();

            return promoters == 1 && passives == 0 && detractors == 0 && total == 1 && Math.Abs(nps - 100.0) < 0.01;
        }, TimeSpan.FromSeconds(2), TimeSpan.FromMilliseconds(50));

        ok.Should().BeTrue("aggregates should reflect one promoter after two identical posts");
    }

    private static async Task<bool> Eventually(Func<Task<bool>> predicate, TimeSpan timeout, TimeSpan poll) {
        var start = DateTime.UtcNow;
        while (DateTime.UtcNow - start < timeout) {
            if (await predicate().ConfigureAwait(false)) return true;
            await Task.Delay(poll).ConfigureAwait(false);
        }

        return false;
    }
}