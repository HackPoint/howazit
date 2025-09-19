using Microsoft.Data.Sqlite;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace Howazit.Responses.Infrastructure.Persistence;

public partial class DbInitializer(ILogger<DbInitializer> logger, IServiceProvider provider)
    : IHostedService {
    [LoggerMessage(LogLevel.Information, "SQLite database ensured created.")]
    static partial void LogSqliteDatabaseEnsuredCreated(ILogger<DbInitializer> logger);


    [LoggerMessage(LogLevel.Information, "SQLite schema already exists; continuing.")]
    static partial void LogSqliteDatabaseAlreadyExists(ILogger<DbInitializer> logger);


    public async Task StartAsync(CancellationToken cancellationToken) {
        using var scope = provider.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ResponsesDbContext>();
        
        try {
            await db.Database.EnsureCreatedAsync(cancellationToken);
            LogSqliteDatabaseEnsuredCreated(logger);
        }
        catch (SqliteException ex) when (ex.SqliteErrorCode == 1 &&
                                         ex.Message.Contains("already exists", StringComparison.OrdinalIgnoreCase)) {
            LogSqliteDatabaseAlreadyExists(logger);
        }
    }

    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;
}