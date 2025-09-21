using Howazit.Responses.Application.Abstractions;
using Howazit.Responses.Application.Models;
using Howazit.Responses.Tests.TestDoubles;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;

namespace Howazit.Responses.Tests;

public class CustomWebAppFactory : WebApplicationFactory<Program> {
    private readonly string _dbPath = Path.Combine(Path.GetTempPath(), $"howazit_tests_{Guid.NewGuid():N}.db");
    private string DbConn => $"Data Source={Path.Combine(_dbPath, "responses.db")}";
    
    protected override void ConfigureWebHost(IWebHostBuilder builder) {
        Directory.CreateDirectory(_dbPath); // ensure it exists
        
        builder.UseEnvironment(Environments.Development);

        // Point the app to a test-specific SQLite file via configuration
        builder.ConfigureAppConfiguration((ctx, cfg) => {
            cfg.AddInMemoryCollection(new Dictionary<string, string?> {
                // strict and deterministic for tests
                ["RATELIMIT__PERMIT_LIMIT"] = "1",
                ["RATELIMIT__WINDOW_MS"]    = "10000",
                ["RATELIMIT__SEGMENTS"]     = "1",
                ["RATELIMIT__QUEUE_LIMIT"]  = "0",
                ["RATELIMIT__ENABLED"] = "false",
                
                // Primary key used by configuration.GetConnectionString("Sqlite")
                ["ConnectionStrings:Sqlite"] = DbConn,
                // Fallback key your DI also supports
                ["SQLITE__CONNECTIONSTRING"] = DbConn
            });
        });

        // Swap the realtime aggregate store with an in-memory test double
        builder.ConfigureServices(services => {
            services.AddSingleton<IRealtimeAggregateStore, InMemoryAggregateStore>();

            // Disable the hosted worker in tests
            foreach (var d in services.Where(d =>
                             d.ServiceType == typeof(IHostedService) &&
                             d.ImplementationType?.Name is not null &&
                             d.ImplementationType.Name.Contains("ResponseWorker"))
                         .ToList()) {
                services.Remove(d);
            }

            // IMPORTANT: remove *all* queue registrations for the closed generic
            foreach (var d in services.Where(d =>
                         d.ServiceType == typeof(IBackgroundQueueService<IngestRequest>)).ToList()) {
                services.Remove(d);
            }

            // Now add the synchronous test-only queue
            // Replace ALL queue-service registrations with the synchronous test queue
            services.RemoveAll<IBackgroundQueueService<IngestRequest>>();
            services.RemoveAll(typeof(IBackgroundQueueService<>));
            services.AddSingleton<IBackgroundQueueService<IngestRequest>, SynchronousBackgroundQueueService>();
        });
    }

    protected override void Dispose(bool disposing) {
        base.Dispose(disposing);
        try {
            if (File.Exists(_dbPath)) File.Delete(_dbPath);
        }
        catch {
            /* ignore */
        }
    }
}