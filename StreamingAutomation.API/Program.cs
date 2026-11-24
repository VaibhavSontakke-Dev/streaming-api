using System.Diagnostics;
using System.Globalization;
using System.Text.Json.Serialization;
using Microsoft.EntityFrameworkCore;
using Scalar.AspNetCore;
using Serilog;
using StreamingAutomation.API;
using StreamingAutomation.API.ErrorHandling;
using StreamingAutomation.API.Hubs;
using StreamingAutomation.Application;
using StreamingAutomation.Application.Monitoring;
using StreamingAutomation.Infrastructure;
using StreamingAutomation.Infrastructure.Persistence;

Log.Logger = new LoggerConfiguration().WriteTo.Console(formatProvider: CultureInfo.InvariantCulture).CreateBootstrapLogger();

try
{
    var builder = WebApplication.CreateBuilder(args);

    builder.Host.UseSerilog((context, services, logger) => logger
        .ReadFrom.Configuration(context.Configuration)
        .ReadFrom.Services(services)
        .Enrich.FromLogContext()
        .WriteTo.Console(outputTemplate: "[{Timestamp:HH:mm:ss} {Level:u3}] {SourceContext}: {Message:lj} {Properties:j}{NewLine}{Exception}", formatProvider: CultureInfo.InvariantCulture));

    builder.Services
        .AddApplication(builder.Configuration)
        .AddInfrastructure(builder.Configuration);

    // Enums travel as strings ("Playing", not 2) on every channel: MVC, minimal APIs, SignalR, OpenAPI.
    builder.Services.AddControllers()
        .AddJsonOptions(o => o.JsonSerializerOptions.Converters.Add(new JsonStringEnumConverter()));
    builder.Services.ConfigureHttpJsonOptions(o => o.SerializerOptions.Converters.Add(new JsonStringEnumConverter()));

    builder.Services.AddSignalR()
        .AddJsonProtocol(o => o.PayloadSerializerOptions.Converters.Add(new JsonStringEnumConverter()));
    builder.Services.AddSingleton<LiveMonitor>();
    builder.Services.AddHostedService<MonitoringBroadcaster>();

    builder.Services.AddOpenApi(o => o.AddDocumentTransformer((document, _, _) =>
    {
        document.Info = new()
        {
            Title = "Streaming Automation API",
            Version = "v1",
            Description =
                "Orchestrates and monitors concurrent simulated playback sessions against a local Test Media Server. " +
                "Real-time updates: SignalR hub at /hubs/monitoring (OverviewUpdated, RunUpdated, RunFinished). " +
                "All figures are locally measured test telemetry, not platform views or streams.",
        };
        return Task.CompletedTask;
    }));

    builder.Services.AddExceptionHandler<ApiExceptionHandler>();
    builder.Services.AddProblemDetails(o => o.CustomizeProblemDetails = ctx =>
        ctx.ProblemDetails.Extensions.TryAdd("traceId", Activity.Current?.Id ?? ctx.HttpContext.TraceIdentifier));
    builder.Services.AddHealthChecks()
        .AddNpgSql(builder.Configuration.GetConnectionString("Default")!, name: "postgres");

    var corsOrigins = builder.Configuration.GetSection("Cors:AllowedOrigins").Get<string[]>() ?? [];
    builder.Services.AddCors(o => o.AddDefaultPolicy(p => p
        .WithOrigins(corsOrigins)
        .AllowAnyHeader()
        .AllowAnyMethod()
        .AllowCredentials())); // SignalR needs credentials with explicit origins

    // Allow time for runs to be interrupted and persisted on shutdown.
    builder.Services.Configure<HostOptions>(o => o.ShutdownTimeout = TimeSpan.FromSeconds(30));

    var app = builder.Build();

    if (app.Configuration.GetValue("Database:ApplyMigrationsOnStartup", false))
    {
        await using var scope = app.Services.CreateAsyncScope();
        await scope.ServiceProvider.GetRequiredService<AppDbContext>().Database.MigrateAsync();
    }

    app.UseSerilogRequestLogging();
    app.UseExceptionHandler();
    app.UseCors();

    app.MapOpenApi();
    app.MapScalarApiReference(); // interactive API docs at /scalar
    app.MapControllers();
    app.MapHub<MonitoringHub>(MonitoringHub.Path);
    app.MapHealthChecks("/health");
    app.MapGet("/", () => Results.Redirect("/scalar")).ExcludeFromDescription();

    await app.RunAsync();
}
catch (Exception ex) when (ex is not HostAbortedException)
{
    Log.Fatal(ex, "API host terminated unexpectedly");
    throw;
}
finally
{
    await Log.CloseAndFlushAsync();
}

namespace StreamingAutomation.API
{
    /// <summary>Marker type for WebApplicationFactory in integration tests.</summary>
    public sealed class ApiEntryPoint;
}
