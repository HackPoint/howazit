using System.Threading.Channels;
using Howazit.Responses.Application.Abstractions;
using Howazit.Responses.Application.Models;
using Howazit.Responses.Infrastructure.Queue;
using Howazit.Responses.Infrastructure.Sanitization;
using Microsoft.Extensions.DependencyInjection;

namespace Howazit.Responses.Infrastructure;

public static class DependencyInjection {
    public static IServiceCollection AddInfrastructure(this IServiceCollection services) {
        // Sanitizer
        services.AddSingleton<ISanitizer, SimpleSanitizer>();

        // Channel-backed queue for IngestRequest
        var channel = Channel.CreateBounded<IngestRequest>(new BoundedChannelOptions(10_000) {
            FullMode = BoundedChannelFullMode.Wait,
            SingleReader = true,
            SingleWriter = false
        });

        var queue = new BackgroundQueueService<IngestRequest>(channel);
        services.AddSingleton(queue);
        services.AddSingleton<IBackgroundQueueService<IngestRequest>>(queue);

        // Background worker
        services.AddHostedService<ResponseWorker>();

        return services;
    }
}