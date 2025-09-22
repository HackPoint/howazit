using System.Data;
using System.Net.Http.Json;
using FluentAssertions;
using Howazit.Responses.Infrastructure.Persistence;
using Howazit.Responses.Infrastructure.Protection;
using Howazit.Responses.Tests.Support;
using Microsoft.AspNetCore.TestHost;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Xunit;

namespace Howazit.Responses.Tests;

public class EncryptionTests(CustomWebAppFactory factory) : IClassFixture<CustomWebAppFactory> {
    [Fact]
    public async Task IpAddressIsEncryptedAtRestAndDecryptsOnRead() {
        var clientId = $"enc-{Guid.NewGuid():N}";
        var client = factory.CreateClient();

        var dto = new Application.Models.IngestRequest {
            SurveyId = "s-enc",
            ClientId = clientId,
            ResponseId = "r-1",
            Responses = new Application.Models.ResponsesPayload { NpsScore = 10 },
            Metadata = new Application.Models.MetadataPayload {
                Timestamp = DateTimeOffset.UtcNow,
                UserAgent = "curl/8.5.0",
                IpAddress = "203.0.113.42"
            }
        };

        var r = await client.PostAsJsonAsync("/v1/responses", dto);
        r.EnsureSuccessStatusCode();

        // EF read -> plaintext
        using (var scope = factory.Services.CreateScope()) {
            var db = scope.ServiceProvider.GetRequiredService<ResponsesDbContext>();
            var row = await db.SurveyResponses
                .AsNoTracking()
                .SingleAsync(x => x.ClientId == clientId && x.ResponseId == "r-1");

            row.IpAddress.Should().Be("203.0.113.42");
            row.UserAgent.Should()
                .Be("curl/8.5.0"); // decrypted or plaintext depending on toggle; either way plaintext via EF
        }

        // RAW DB -> IpAddress must NOT equal plaintext (ciphertext at rest)
        using (var scope = factory.Services.CreateScope()) {
            var db = scope.ServiceProvider.GetRequiredService<ResponsesDbContext>();
            await db.Database.OpenConnectionAsync();
            await using var cmd = db.Database.GetDbConnection().CreateCommand();
            cmd.CommandText = """
                              SELECT IpAddress, UserAgent
                              FROM survey_responses
                              WHERE ClientId = $c AND ResponseId = $r
                              """;
            var pC = cmd.CreateParameter();
            pC.ParameterName = "$c";
            pC.Value = clientId;
            cmd.Parameters.Add(pC);
            var pR = cmd.CreateParameter();
            pR.ParameterName = "$r";
            pR.Value = "r-1";
            cmd.Parameters.Add(pR);

            await using var reader = await cmd.ExecuteReaderAsync(CommandBehavior.SingleRow);
            reader.Read().Should().BeTrue();

            var ipRaw = reader.GetString(0);
            ipRaw.Should().NotBe("203.0.113.42");

            var uaRaw = reader.IsDBNull(1) ? null : reader.GetString(1);
            uaRaw.Should().NotBeNull(); // may be ciphertext or plaintext based on toggle
        }
    }

    [Fact]
    public async Task KeyRotationReadsOldCipherWhenPreviousPurposesConfigured() {
        var clientId = $"rot-{Guid.NewGuid():N}";
        var client = factory.CreateClient();

        // Write one row under current purpose (call it A)
        var r1 = await client.PostAsJsonAsync("/v1/responses", new Application.Models.IngestRequest {
            SurveyId = "s-rot",
            ClientId = clientId,
            ResponseId = "r-1",
            Responses = new Application.Models.ResponsesPayload { NpsScore = 7 },
            Metadata = new Application.Models.MetadataPayload {
                Timestamp = DateTimeOffset.UtcNow,
                UserAgent = "ua-rot",
                IpAddress = "198.51.100.10"
            }
        });
        r1.EnsureSuccessStatusCode();

        // Mutate protector options to simulate rotation (Purpose=B, Previous=[A])
        using (var scope = factory.Services.CreateScope()) {
            var opts = scope.ServiceProvider.GetRequiredService<DataProtectionFieldProtector.Options>();
            var oldPurpose = opts.Purpose;
            opts.Purpose = "howazit:v2:test"; // new "rotated" purpose
            opts.PreviousPurposes = new[] { oldPurpose };
        }

        // Now read the row again; it should still decrypt using PreviousPurposes
        using (var scope = factory.Services.CreateScope()) {
            var db = scope.ServiceProvider.GetRequiredService<ResponsesDbContext>();
            var row = await db.SurveyResponses.AsNoTracking()
                .SingleAsync(x => x.ClientId == clientId && x.ResponseId == "r-1");

            row.IpAddress.Should().Be("198.51.100.10");
            row.UserAgent.Should().Be("ua-rot");
        }
    }

    [Fact]
    public async Task UserAgentToggleControlsCiphertextAtRest() {
        // Spin up a host where UA encryption is ON
        var localFactory = factory.WithWebHostBuilder(b => {
            b.ConfigureAppConfiguration((ctx, cfg) => {
                cfg.AddInMemoryCollection(new Dictionary<string, string?> {
                    // cover both styles
                    ["ENCRYPT__USERAGENT"] = "true",
                    ["ENCRYPT:USERAGENT"] = "true"
                });
            });

            b.ConfigureTestServices(services => {
                // Replace the options singleton the app registers in AddInfrastructure(...)
                services.RemoveAll<DataProtectionFieldProtector.Options>();
                services.AddSingleton(
                    new DataProtectionFieldProtector.Options {
                        Purpose = "howazit:vTEST:pii",
                        PreviousPurposes = [],
                        EncryptUserAgent = true // <- crucial
                    });
            });
        });
        
        var client = localFactory.CreateClient();
        var respId   = "r-ua-1";
        var clientId = $"ua-{Guid.NewGuid():N}";
        
        
        var dto = new Application.Models.IngestRequest
        {
            SurveyId = "s-ua",
            ClientId = clientId,
            ResponseId = respId,
            Responses = new Application.Models.ResponsesPayload { NpsScore = 9 },
            Metadata = new Application.Models.MetadataPayload
            {
                Timestamp = DateTimeOffset.UtcNow,
                UserAgent = "BrowserY",
                IpAddress = "198.51.100.77"
            }
        };

        var post = await client.PostAsJsonAsync("/v1/responses", dto);
        post.EnsureSuccessStatusCode();
        
        // Wait until the background worker actually persisted the row
        await TestHelpers.WaitForRowAsync(factory.Services, clientId, respId);
        
        using var scope = localFactory.Services.CreateScope();

        // Sanity: prove the toggle is ON in this host
        var opts = scope.ServiceProvider.GetRequiredService<DataProtectionFieldProtector.Options>();
        opts.EncryptUserAgent.Should().BeTrue();

        var db = scope.ServiceProvider.GetRequiredService<ResponsesDbContext>();
        await db.Database.OpenConnectionAsync();

        await using var cmd = db.Database.GetDbConnection().CreateCommand();
        cmd.CommandText = "SELECT UserAgent FROM survey_responses WHERE ClientId=$c AND ResponseId=$r";
        var p1 = cmd.CreateParameter(); p1.ParameterName = "$c"; p1.Value = clientId; cmd.Parameters.Add(p1);
        var p2 = cmd.CreateParameter(); p2.ParameterName = "$r"; p2.Value = "r-ua-1"; cmd.Parameters.Add(p2);

        await using var reader = await cmd.ExecuteReaderAsync();
        reader.Read().Should().BeTrue();

        var uaRaw = reader.IsDBNull(0) ? null : reader.GetString(0);

        // With encryption ON, ciphertext != plaintext
        uaRaw.Should().NotBe("BrowserY");
    }

    [Fact]
    public async Task NonEncryptedFieldsUnchangedAtRest() {
        var client = factory.CreateClient();
        var clientId = $"plain-{Guid.NewGuid():N}";

        var dto = new Application.Models.IngestRequest {
            SurveyId = "s-plain",
            ClientId = clientId,
            ResponseId = "r-1",
            Responses = new Application.Models.ResponsesPayload {
                NpsScore = 9,
                Satisfaction = "great"
            },
            Metadata = new Application.Models.MetadataPayload {
                Timestamp = DateTimeOffset.UtcNow,
                UserAgent = "UA-p",
                IpAddress = "100.64.0.1"
            }
        };

        var r = await client.PostAsJsonAsync("/v1/responses", dto);
        r.EnsureSuccessStatusCode();

        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ResponsesDbContext>();
        await db.Database.OpenConnectionAsync();

        await using var cmd = db.Database.GetDbConnection().CreateCommand();
        cmd.CommandText = """
                          SELECT SurveyId, ResponseId, Satisfaction, NpsScore
                          FROM survey_responses
                          WHERE ClientId=$c AND ResponseId=$r
                          """;
        var pC = cmd.CreateParameter();
        pC.ParameterName = "$c";
        pC.Value = clientId;
        cmd.Parameters.Add(pC);
        var pR = cmd.CreateParameter();
        pR.ParameterName = "$r";
        pR.Value = "r-1";
        cmd.Parameters.Add(pR);

        await using var reader = await cmd.ExecuteReaderAsync(CommandBehavior.SingleRow);
        reader.Read().Should().BeTrue();

        reader.GetString(0).Should().Be("s-plain"); // SurveyId plaintext
        reader.GetString(1).Should().Be("r-1"); // ResponseId plaintext
        reader.GetString(2).Should().Be("great"); // Satisfaction plaintext
        reader.GetInt32(3).Should().Be(9); // NpsScore untouched
    }
}