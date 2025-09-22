using Howazit.Responses.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Howazit.Responses.Tests.Support;

internal static class TestHelpers {
    // Convenience overload: pass the test host's ServiceProvider
    public static async Task WaitForRowAsync(
        IServiceProvider services,
        string clientId,
        string responseId,
        int timeoutMs = 2000) {
        using var scope = services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ResponsesDbContext>();
        await WaitForRowAsync(db, clientId, responseId, timeoutMs);
    }

    // Core polling loop (useful if you already have a DbContext)
    public static async Task WaitForRowAsync(
        ResponsesDbContext db,
        string clientId,
        string responseId,
        int timeoutMs = 2000) {
        var deadline = DateTime.UtcNow.AddMilliseconds(timeoutMs);
        while (DateTime.UtcNow < deadline) {
            var exists = await db.SurveyResponses
                .AsNoTracking()
                .AnyAsync(x => x.ClientId == clientId && x.ResponseId == responseId)
                .ConfigureAwait(false);

            if (exists) return;
            await Task.Delay(50).ConfigureAwait(false);
        }

        throw new TimeoutException($"Row {clientId}/{responseId} not written within {timeoutMs}ms.");
    }
}