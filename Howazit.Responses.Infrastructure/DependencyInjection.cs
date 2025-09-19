using System.Threading.Channels;
using Howazit.Responses.Application.Abstractions;
using Howazit.Responses.Application.Models;
using Howazit.Responses.Infrastructure.Persistence;
using Howazit.Responses.Infrastructure.Queue;
using Howazit.Responses.Infrastructure.Realtime;
using Howazit.Responses.Infrastructure.Repositories;
using Howazit.Responses.Infrastructure.Sanitization;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using StackExchange.Redis;

namespace Howazit.Responses.Infrastructure;

public static class DependencyInjection {
    public static IServiceCollection AddInfrastructure(this IServiceCollection services, IConfiguration configuration) {
        services.AddSingleton<ISanitizer, SimpleSanitizer>();

        // SQS-like channel
        var channel = Channel.CreateBounded<IngestRequest>(new BoundedChannelOptions(10_000) {
            FullMode = BoundedChannelFullMode.Wait,
            SingleReader = true,
            SingleWriter = false
        });
        var queue = new BackgroundQueueService<IngestRequest>(channel);
        services.AddSingleton(queue);
        services.AddSingleton<IBackgroundQueueService<IngestRequest>>(queue);

        // SQLite (scoped DbContext)
        var sqliteConn = configuration.GetConnectionString("Sqlite")
                         ?? configuration["SQLITE__CONNECTIONSTRING"]
                         ?? "Data Source=howazit.db";
        services.AddDbContext<ResponsesDbContext>(o => o.UseSqlite(sqliteConn));

        // Ensure DB created at startup (creates a scope internally)
        services.AddHostedService<DbInitializer>();

        // Repo (scoped)
        services.AddScoped<IResponseRepository, EfResponseRepository>();

        // Redis (singleton multiplexer; don’t crash app if not up yet)
        var redisConn = configuration["REDIS__CONNECTIONSTRING"] ?? "localhost:6379";
        var redisOptions = ConfigurationOptions.Parse(redisConn);
        redisOptions.AbortOnConnectFail = false;
        services.AddSingleton<IConnectionMultiplexer>(_ => ConnectionMultiplexer.Connect(redisOptions));
        services.AddSingleton<IRealtimeAggregateStore, RedisAggregateStore>();

        // Background worker (singleton) — creates scopes per message
        services.AddHostedService<ResponseWorker>();

        return services;
    }
}