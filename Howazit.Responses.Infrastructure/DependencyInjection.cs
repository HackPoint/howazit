using System.Threading.Channels;
using Howazit.Responses.Application.Abstractions;
using Howazit.Responses.Application.Models;
using Howazit.Responses.Infrastructure.Persistence;
using Howazit.Responses.Infrastructure.Queue;
using Howazit.Responses.Infrastructure.Realtime;
using Howazit.Responses.Infrastructure.Repositories;
using Howazit.Responses.Infrastructure.Resilience;
using Howazit.Responses.Infrastructure.Sanitization;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using StackExchange.Redis;

namespace Howazit.Responses.Infrastructure;

public static class DependencyInjection {
    public static IServiceCollection AddInfrastructure(this IServiceCollection services, IConfiguration configuration)
    {
        services.AddSingleton<ISanitizer, SimpleSanitizer>();

        // SQS-like channel (unchanged)
        var channel = Channel.CreateBounded<IngestRequest>(new BoundedChannelOptions(10_000) {
            FullMode = BoundedChannelFullMode.Wait,
            SingleReader = true,
            SingleWriter = false
        });
        var queue = new BackgroundQueueService<IngestRequest>(channel);
        services.AddSingleton(queue);
        services.AddSingleton<IBackgroundQueueService<IngestRequest>>(queue);

        // ---------- SQLite ----------
        // Prefer ConnectionStrings:Sqlite, then env key "SQLITE:CONNECTIONSTRING"
        var sqliteConn =
            configuration.GetConnectionString("Sqlite")
            ?? configuration["SQLITE:CONNECTIONSTRING"]    // <- env var SQLITE__CONNECTIONSTRING maps to this key
            ?? "Data Source=./data/howazit.db";                 // good default for container

        services.AddDbContext<ResponsesDbContext>(o =>
        {
            o.UseSqlite(sqliteConn);
            o.EnableDetailedErrors();
        });

        services.AddHostedService<DbInitializer>();
        services.AddScoped<IResponseRepository, EfResponseRepository>();

        // Resilience (unchanged)
        services.AddSingleton<IResiliencePolicies, ResiliencePolicies>();

        // Sanitizer (use whichever you settled on)
        services.AddSingleton<ISanitizer, HtmlSanitizer>();

        // ---------- Redis ----------
        // Read env key "REDIS:CONNECTIONSTRING" (REDIS__CONNECTIONSTRING in Docker)
        var redisConn =
            configuration["REDIS:CONNECTIONSTRING"]
            ?? "localhost:6379,abortConnect=false"; // local dev fallback only

        var redisOptions = ConfigurationOptions.Parse(redisConn);
        redisOptions.AbortOnConnectFail = false;

        services.AddSingleton<IConnectionMultiplexer>(_ => ConnectionMultiplexer.Connect(redisOptions));
        services.AddSingleton<IRedisClient, StackExchangeRedisClient>();
        services.AddSingleton<IRealtimeAggregateStore, RedisAggregateStore>();

        // Worker
        services.AddHostedService<ResponseWorker>();

        return services;
    }
}