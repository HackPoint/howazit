var builder = DistributedApplication.CreateBuilder(args);

// Infra (Redis)
var redis = builder.AddRedis("redis");

// API project (absolute path) — no WithHttpEndpoint here to avoid duplicate 'http'
builder.AddProject<Projects.Howazit_Responses_Api>("api")
    .WithOtlpExporter()
    .WithEnvironment("DOTNET_ENVIRONMENT", "Development")
    .WithEnvironment("SQLITE__CONNECTIONSTRING", "Data Source=./data/howazit.db")
    .WithEnvironment("REDIS__CONNECTIONSTRING", "redis:6379,password=redispass,abortConnect=false")
    .WithReference(redis, "REDIS__CONNECTIONSTRING")
    .WithHttpEndpoint(name: "public-http", port: 8080);

builder.Build().Run();
