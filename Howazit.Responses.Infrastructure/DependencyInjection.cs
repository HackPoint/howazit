using System.Threading.Channels;
using Howazit.Responses.Application.Abstractions;
using Howazit.Responses.Application.Models;
using Howazit.Responses.Infrastructure.Persistence;
using Howazit.Responses.Infrastructure.Protection;
using Howazit.Responses.Infrastructure.Queue;
using Howazit.Responses.Infrastructure.Realtime;
using Howazit.Responses.Infrastructure.Repositories;
using Howazit.Responses.Infrastructure.Resilience;
using Howazit.Responses.Infrastructure.Sanitization;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using StackExchange.Redis;

namespace Howazit.Responses.Infrastructure;

public static class DependencyInjection {
    /// <summary>
    /// Composition root for infra: calls modular SRP extensions below.
    /// </summary>
    public static IServiceCollection AddInfrastructure(this IServiceCollection services, IConfiguration configuration) {
        return services
            .AddSanitization()
            .AddDataProtectionAndEncryption(configuration)
            .AddQueueing()
            .AddPersistence(configuration)
            .AddResilience()
            .AddRedis(configuration)
            .AddWorkers();
    }
}

internal static class SanitizationExtensions {
    public static IServiceCollection AddSanitization(this IServiceCollection services) {
        // One sanitizer. Keep HtmlSanitizer as the default.
        services.AddSingleton<ISanitizer, HtmlSanitizer>();
        return services;
    }
}

internal static class DataProtectionExtensions {
    public static IServiceCollection AddDataProtectionAndEncryption(this IServiceCollection services,
        IConfiguration config) {
        // DataProtection (optional persisted keyring)
        var appName = "howazit-responses";
        var keyPath = config["DATAPROTECTION__KEYRING_PATH"];
        var dp = services.AddDataProtection().SetApplicationName(appName);
        if (!string.IsNullOrWhiteSpace(keyPath)) {
            try {
                Directory.CreateDirectory(keyPath);
            }
            catch { /* ignore */
            }

            dp.PersistKeysToFileSystem(new DirectoryInfo(keyPath));
        }

        // Bind ENCRYPT section (supports env ENCRYPT__PURPOSE / ENCRYPT__USERAGENT)
        services.AddOptions<DataProtectionFieldProtector.Options>()
            .Bind(config.GetSection("ENCRYPT"))
            .PostConfigure(o => {
                // Sensible defaults
                o.Purpose ??= "howazit:v1:pii";
                o.PreviousPurposes ??= Array.Empty<string>();
                // if USERAGENT not provided, default to false (no UA encryption)
            })
            .ValidateOnStart();

        // Provide the resolved Options value for constructors that take the plain Options type
        services.AddSingleton(sp => sp.GetRequiredService<IOptions<DataProtectionFieldProtector.Options>>().Value);

        // Field protector wrapper (IDataProtectionProvider is provided by AddDataProtection)
        services.AddSingleton<IFieldProtector, DataProtectionFieldProtector>();

        return services;
    }
}

internal static class QueueingExtensions {
    public static IServiceCollection AddQueueing(this IServiceCollection services) {
        // SQS-like channel
        var channel = Channel.CreateBounded<IngestRequest>(new BoundedChannelOptions(10_000) {
            FullMode = BoundedChannelFullMode.Wait,
            SingleReader = true,
            SingleWriter = false
        });

        var queue = new BackgroundQueueService<IngestRequest>(channel);
        services.AddSingleton(queue);
        services.AddSingleton<IBackgroundQueueService<IngestRequest>>(queue);
        return services;
    }
}

internal static class PersistenceExtensions {
    public static IServiceCollection AddPersistence(this IServiceCollection services, IConfiguration config) {
        // Support both ConnectionStrings:Sqlite and env overrides
        var sqliteConn =
            config.GetConnectionString("Sqlite")
            ?? config["SQLITE:CONNECTIONSTRING"]
            ?? config["SQLITE__CONNECTIONSTRING"] // tests may set this exact key via in-memory config
            ?? "Data Source=./data/howazit.db";

        services.AddDbContext<ResponsesDbContext>(o => {
            o.UseSqlite(sqliteConn);
            o.EnableDetailedErrors();
            
            o.ReplaceService<IModelCacheKeyFactory, ResponsesModelCacheKeyFactory>();
        });

        // Ensure DB created on startup
        services.AddHostedService<DbInitializer>();

        // Repo (scoped)
        services.AddScoped<IResponseRepository, EfResponseRepository>();
        return services;
    }
}

internal static class ResilienceExtensions {
    public static IServiceCollection AddResilience(this IServiceCollection services) {
        services.AddSingleton<IResiliencePolicies, ResiliencePolicies>();
        return services;
    }
}

internal static class RedisExtensions {
    public static IServiceCollection AddRedis(this IServiceCollection services, IConfiguration config) {
        // Support both REDIS:CONNECTIONSTRING and REDIS__CONNECTIONSTRING
        var redisConn =
            config["REDIS:CONNECTIONSTRING"]
            ?? config["REDIS__CONNECTIONSTRING"]
            ?? "localhost:6379,abortConnect=false";

        var options = ConfigurationOptions.Parse(redisConn);
        options.AbortOnConnectFail = false;

        services.AddSingleton<IConnectionMultiplexer>(_ => ConnectionMultiplexer.Connect(options));
        services.AddSingleton<IRedisClient, StackExchangeRedisClient>();
        services.AddSingleton<IRealtimeAggregateStore, RedisAggregateStore>();
        return services;
    }
}

internal static class WorkerExtensions {
    public static IServiceCollection AddWorkers(this IServiceCollection services) {
        services.AddHostedService<ResponseWorker>();
        return services;
    }
}