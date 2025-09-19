namespace Howazit.Responses.Application.Abstractions;

public interface IBackgroundQueueService<in T>
{
    ValueTask EnqueueAsync(T item, CancellationToken ct = default);
}