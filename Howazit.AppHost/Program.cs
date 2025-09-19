var builder = DistributedApplication.CreateBuilder(args);

// Discover repo root (walk up until we find a marker file)
var repoRoot = FindRepoRoot() ?? Directory.GetCurrentDirectory();

// Build absolute path to the API .csproj
var apiProjectPath = Path.Combine(repoRoot, "Howazit.Responses.Api", "Howazit.Responses.Api.csproj");
if (!File.Exists(apiProjectPath)) {
    throw new FileNotFoundException($"API project file not found at: {apiProjectPath}");
}

// Infra (Redis)
var redis = builder.AddRedis("redis", 6379);

// API project (absolute path) — no WithHttpEndpoint here to avoid duplicate 'http'
builder.AddProject("howazit-api", apiProjectPath)
    .WithEnvironment("DOTNET_ENVIRONMENT", "Development")
    .WithEnvironment("ASPNETCORE_URLS", "http://0.0.0.0:8080")
    .WithReference(redis);

builder.Build().Run();

static string? FindRepoRoot() {
    var dir = new DirectoryInfo(AppContext.BaseDirectory);
    while (dir != null) {
        if (File.Exists(Path.Combine(dir.FullName, "global.json")) ||
            File.Exists(Path.Combine(dir.FullName, ".gitignore")) ||
            Directory.GetFiles(dir.FullName, "*.sln").Length > 0) {
            return dir.FullName;
        }

        dir = dir.Parent;
    }

    return null;
}