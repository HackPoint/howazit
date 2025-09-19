using Howazit.Responses.Application.Abstractions;
using Howazit.Responses.Infrastructure.Persistence;
using Howazit.Responses.Tests.TestDoubles;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

namespace Howazit.Responses.Tests;

public class CustomWebAppFactory : WebApplicationFactory<Program> {
    private readonly string _dbPath = Path.Combine(Path.GetTempPath(), $"howazit_tests_{Guid.NewGuid():N}.db");

    protected override void ConfigureWebHost(IWebHostBuilder builder) {
        builder.UseEnvironment(Environments.Development);

        // Point the app to a test-specific SQLite file via configuration
        builder.ConfigureAppConfiguration((ctx, cfg) => {
            cfg.AddInMemoryCollection(new Dictionary<string, string?> {
                ["SQLITE__CONNECTIONSTRING"] = $"Data Source={_dbPath}"
            });
        });

        // Swap the realtime aggregate store with an in-memory test double
        builder.ConfigureServices(services => {
            var existingAgg = services.SingleOrDefault(d => d.ServiceType == typeof(IRealtimeAggregateStore));
            if (existingAgg is not null) services.Remove(existingAgg);
            services.AddSingleton<IRealtimeAggregateStore, InMemoryAggregateStore>();
        });
    }
}