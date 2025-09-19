using System.Threading.RateLimiting;
using FluentValidation;
using Howazit.Responses.Api.Features;
using Howazit.Responses.Application.Validations;
using Microsoft.AspNetCore.RateLimiting;
using OpenTelemetry.Metrics;
using OpenTelemetry.Trace;
using Serilog;
using Howazit.Responses.Infrastructure;

var builder = WebApplication.CreateBuilder(args);

// Add Serilog
builder.Host.UseSerilog((ctx, cfg) => cfg
    .ReadFrom.Configuration(ctx.Configuration)
    .WriteTo.Console());


// Swagger
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen();


// Basic rate limiter ( per client )
builder.Services.AddRateLimiter(_ => _
    .AddFixedWindowLimiter("global", options => {
        options.PermitLimit = 200;
        options.Window = TimeSpan.FromSeconds(1);
        options.QueueProcessingOrder = QueueProcessingOrder.OldestFirst;
        options.QueueLimit = 1000;
    })
);


// OpenTelemetry (we’ll refine resources/instrumentation later)
// todo: OpenTelemetry (no Prometheus exporter yet; we'll add exporter later)
builder.Services.AddOpenTelemetry()
    .WithMetrics(m => m.AddAspNetCoreInstrumentation()
        .AddHttpClientInstrumentation()
        .AddRuntimeInstrumentation()) 
    .WithTracing(t => t.AddAspNetCoreInstrumentation()
        .AddHttpClientInstrumentation());

// App services
builder.Services.AddInfrastructure();

// FluentValidation: discover validators from Application assembly
builder.Services.AddValidatorsFromAssemblyContaining<IngestRequestValidator>();


// Add services to the container.        
var app = builder.Build();

// Configure the HTTP request pipeline.
app.UseSerilogRequestLogging();
app.UseRateLimiter();

// Swagger in all envs for interview convenience
app.UseSwagger();
app.UseSwaggerUI();

// Health
app.MapGet("/health", () => Results.Ok(new { status = "Healthy" }));

// Register: All responses endpoints
app.MapResponseEndpoints();

// Root redirect
app.MapGet("/", () => Results.Redirect("/swagger"));

app.Run();

// Allow WebApplicationFactory<Program> in tests
public partial class Program { }