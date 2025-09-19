using System.Net;
using System.Net.Http.Json;
using FluentAssertions;
using Howazit.Responses.Application.Abstractions;
using Howazit.Responses.Application.Models;
using Howazit.Responses.Infrastructure.Persistence;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Howazit.Responses.Tests;

public class DualStorageIdempotencyTests(CustomWebAppFactory factory) : IClassFixture<CustomWebAppFactory> {
    [Fact]
    public async Task DuplicatePostsResultInSingleRowAndSingleAggregateIncrement() {
        var client = factory.CreateClient();

        var dto = new IngestRequest {
            SurveyId = "s-42",
            ClientId = "waystar",
            ResponseId = "r-abc",
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

        // first post
        var r1 = await client.PostAsJsonAsync("/v1/responses", dto);
        r1.StatusCode.Should().Be(HttpStatusCode.Accepted);

        // duplicate post
        var r2 = await client.PostAsJsonAsync("/v1/responses", dto);
        r2.StatusCode.Should().Be(HttpStatusCode.Accepted);

        // verify DB row count (==1 for client)
        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ResponsesDbContext>();
        var count = db.SurveyResponses.Count(r => r.ClientId == "waystar" && r.ResponseId == "r-abc");
        count.Should().Be(1);

        // verify aggregates
        var agg = scope.ServiceProvider.GetRequiredService<IRealtimeAggregateStore>();
        var snap = await agg.GetNpsAsync("waystar");
        snap.Promoters.Should().Be(1);
        snap.Detractors.Should().Be(0);
        snap.Passives.Should().Be(0);
        snap.Total.Should().Be(1);
        snap.Nps.Should().Be(100.00);
    }
}