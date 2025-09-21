using System.Reflection;
using System.Threading.RateLimiting;
using FluentValidation;
using Howazit.Responses.Api.Features.Metrics;
using Howazit.Responses.Api.Features.Responses;
using Howazit.Responses.Api.Telemetry;
using Howazit.Responses.Application.Validations;
using OpenTelemetry.Metrics;
using OpenTelemetry.Trace;
using OpenTelemetry.Resources;
using Serilog;
using Howazit.Responses.Infrastructure;
using Microsoft.Extensions.Primitives;
using OpenTelemetry.Logs;

var builder = WebApplication.CreateBuilder(args);

// Add Serilog
builder.Host.UseSerilog((ctx, cfg) => cfg
    .ReadFrom.Configuration(ctx.Configuration)
    .WriteTo.Console());

var cfg = builder.Configuration;

int GetInt(string key, int @default)
    => int.TryParse(cfg[key], out var v) ? v : @default;

var rlLimit = GetInt("RATELIMIT__PERMIT_LIMIT", 10);
var rlWindowMs = GetInt("RATELIMIT__WINDOW_MS", 1000);
var rlSegments = GetInt("RATELIMIT__SEGMENTS", 10);
var rlQueue = GetInt("RATELIMIT__QUEUE_LIMIT", 0);
var rlEnabled = cfg.GetValue<bool?>("RATELIMIT__ENABLED") ?? true;

// Swagger
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen();


builder.Services.AddRateLimiter(options => {
    options.RejectionStatusCode = StatusCodes.Status429TooManyRequests;

    // Named policy we can attach per-endpoint
    options.AddPolicy("per-client", httpContext => {
        if (!httpContext.Request.Headers.TryGetValue("X-Client-Id", out StringValues hv) ||
            StringValues.IsNullOrEmpty(hv)) {
            return RateLimitPartition.GetNoLimiter("no-client-header");
        }


        var clientKey = hv.ToString();
        // Partition key: prefer X-Client-Id header; fallback to the client IP; else "anon"
        // var key =
        //     (httpContext.Request.Headers.TryGetValue("X-Client-Id", out StringValues hv) && !StringValues.IsNullOrEmpty(hv))
        //         ? hv.ToString()
        //         : (httpContext.Connection.RemoteIpAddress?.ToString() ?? "anon");

        // Very tight limits to make tests deterministic
        return RateLimitPartition.GetTokenBucketLimiter(
            partitionKey: clientKey,
            factory: _ => new TokenBucketRateLimiterOptions {
                TokenLimit = 1, // 1 request available now
                TokensPerPeriod = 1, // replenish 1
                ReplenishmentPeriod = TimeSpan.FromSeconds(10),
                QueueLimit = rlQueue, // no queuing
                QueueProcessingOrder = QueueProcessingOrder.OldestFirst,
                AutoReplenishment = true
            });
    });

    options.OnRejected = async (context, token) => {
        if (context.Lease.TryGetMetadata(MetadataName.RetryAfter, out var retryAfter)) {
            context.HttpContext.Response.Headers.RetryAfter =
                ((int)Math.Ceiling(retryAfter.TotalSeconds)).ToString();
            context.HttpContext.Response.Headers["RateLimit-Reset"] =
                DateTimeOffset.UtcNow.Add(retryAfter).ToUnixTimeSeconds().ToString();
        }

        context.HttpContext.Response.Headers["RateLimit-Policy"] =
            $"sliding;window={rlWindowMs}ms;limit={rlLimit}";

        await context.HttpContext.Response.WriteAsync("Too many requests.", token);
    };
});


// OpenTelemetry (we’ll refine resources/instrumentation later)
// todo: OpenTelemetry (no Prometheus exporter yet; we'll add exporter later)
builder.Services.AddOpenTelemetry()
    .WithMetrics(m => m.AddAspNetCoreInstrumentation()
        .AddHttpClientInstrumentation()
        .AddRuntimeInstrumentation())
    .WithTracing(t => t.AddAspNetCoreInstrumentation()
        .AddHttpClientInstrumentation());

var serviceName = "howazit-responses-api";
var serviceVersion = Assembly.GetExecutingAssembly()
    .GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion ?? "1.0.0";

builder.Services.AddOpenTelemetry()
    .ConfigureResource(rb => rb
        .AddService(serviceName: serviceName, serviceVersion: serviceVersion)
        .AddTelemetrySdk()
        .AddEnvironmentVariableDetector()
    )
    .WithTracing(tracing => tracing
        .AddAspNetCoreInstrumentation(o => {
            o.RecordException = true;
            o.EnrichWithHttpRequest = TelemetryEnricher.EnrichWithHttpRequest;
            o.EnrichWithHttpResponse = TelemetryEnricher.EnrichWithHttpResponse;
            o.EnrichWithException = TelemetryEnricher.EnrichWithException;
        })
        .AddHttpClientInstrumentation()
        .AddEntityFrameworkCoreInstrumentation(o => {
            o.SetDbStatementForText = true;
            o.SetDbStatementForStoredProcedure = true;
        })
        .AddRedisInstrumentation()
        .AddOtlpExporter()
    )
    .WithMetrics(metrics => metrics
        .AddAspNetCoreInstrumentation()
        .AddHttpClientInstrumentation()
        .AddRuntimeInstrumentation()
        .AddProcessInstrumentation()
        .AddOtlpExporter()
    );

// Logs to OTLP
builder.Logging.AddOpenTelemetry(o => {
    o.IncludeFormattedMessage = true;
    o.IncludeScopes = true;
    o.ParseStateValues = true;
    o.AddOtlpExporter();
});

// App services (Infra: sanitizer, queue, worker, repos, db, redis)
builder.Services.AddInfrastructure(builder.Configuration);

// FluentValidation: discover validators from Application assembly
builder.Services.AddValidatorsFromAssemblyContaining<IngestRequestValidator>();


// Add services to the container.        
var app = builder.Build();

// Configure the HTTP request pipeline.
app.UseSerilogRequestLogging();
if (rlEnabled) app.UseRateLimiter();

// Swagger in all envs for interview convenience
app.UseSwagger();
app.UseSwaggerUI();

// Health
app.MapGet("/health", () => Results.Ok(new { status = "Healthy" }));

// Register: All responses endpoints
app.MapResponseEndpoints();
app.MapMetricsEndpoints();

// Root redirect
app.MapGet("/", () => Results.Redirect("/swagger"));

app.Run();

// Allow WebApplicationFactory<Program> in tests
public partial class Program { }