using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace Howazit.Responses.Infrastructure.Persistence;

public partial class DbInitializer(ILogger<DbInitializer> logger, IServiceProvider provider)
    : IHostedService {

    [LoggerMessage(LogLevel.Information, "SQLite database ensured created.")]
    private static partial void LogDbReady(ILogger logger);

    [LoggerMessage(LogLevel.Warning, "Could not create SQLite directory for data source '{dataSource}'. Continuing.")]
    private static partial void LogDirCreateSkipped(ILogger logger, string dataSource);

    [LoggerMessage(LogLevel.Error, "Failed to initialize SQLite.")]
    private static partial void LogDbInitError(ILogger logger, Exception ex);


    [LoggerMessage(LogLevel.Information, "SQLite schema already exists; continuing.")]
    static partial void LogSqliteDatabaseAlreadyExists(ILogger<DbInitializer> logger);


    public async Task StartAsync(CancellationToken cancellationToken) {
        using var scope = provider.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ResponsesDbContext>();

        try {
            TryEnsureSqliteDirectory(db, logger);
            await db.Database.EnsureCreatedAsync(cancellationToken);
            LogDbReady(logger);
        }
        catch (SqliteException ex) when (ex.SqliteErrorCode == 1 &&
                                         ex.Message.Contains("already exists", StringComparison.OrdinalIgnoreCase)) {
            LogSqliteDatabaseAlreadyExists(logger);
        }
    }

    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;

    private static void TryEnsureSqliteDirectory(ResponsesDbContext ctx, ILogger logger) {
        var connStr = ctx.Database.GetDbConnection().ConnectionString;

        // Only act on SQLite file-based data sources
        SqliteConnectionStringBuilder? csb = null;
        try {
            csb = new SqliteConnectionStringBuilder(connStr);
        }
        catch { /* not SQLite or unparsable; nothing to do */
        }

        if (csb is null) return;

        var dataSource = csb.DataSource;
        if (string.IsNullOrWhiteSpace(dataSource)) return;

        // Skip in-memory databases
        var dsLower = dataSource.Trim().ToLowerInvariant();
        if (dsLower == ":memory:" || dsLower.StartsWith("file::memory:"))
            return;

        // Resolve to a full path and ensure its directory exists
        try {
            var fullPath = Path.GetFullPath(dataSource);
            var dir = Path.GetDirectoryName(fullPath);
            if (!string.IsNullOrEmpty(dir)) {
                Directory.CreateDirectory(dir);
            }
        }
        catch (UnauthorizedAccessException) {
            // In some host test environments a root-level path may be unwritable; don't fail startup
            LogDirCreateSkipped(logger, dataSource);
        }
        catch (IOException) {
            LogDirCreateSkipped(logger, dataSource);
        }
    }
}