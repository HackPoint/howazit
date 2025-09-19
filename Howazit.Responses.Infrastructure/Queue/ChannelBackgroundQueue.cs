using System.Threading.Channels;
using Howazit.Responses.Application.Abstractions;

namespace Howazit.Responses.Infrastructure.Queue;

public sealed class BackgroundQueueService<T> : IBackgroundQueueService<T>
{
    private readonly Channel<T> _channel;

    public BackgroundQueueService(Channel<T>? channel = null)
        => _channel = channel ?? Channel.CreateBounded<T>(new BoundedChannelOptions(10_000)
            { FullMode = BoundedChannelFullMode.Wait, SingleReader = false, SingleWriter = false });

    public ChannelReader<T> Reader => _channel.Reader;
    public ChannelWriter<T> Writer => _channel.Writer;

    public ValueTask EnqueueAsync(T item, CancellationToken ct = default)
        => _channel.Writer.WriteAsync(item, ct);
}