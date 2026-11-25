using Bassign.Infrastructure.Settings;
using Bassign.Api;
using Bassign.Application;
using Bassign.Infrastructure;
using Bassign.Infrastructure.Hosting;
using Bassign.Infrastructure.Identity;
using Bassign.Infrastructure.Jobs;
using Bassign.Infrastructure.Options;
using Bassign.Infrastructure.Persistence;
using Bassign.Infrastructure.Persistence.Seed;
using Microsoft.EntityFrameworkCore;
using Serilog;

var builder = WebApplication.CreateBuilder(args);
builder.Configuration.AddBassignAdminSettings();   // Admin-set values from the database: above appsettings, below env vars

builder.Host.UseSerilog((context, loggerConfig) => loggerConfig
    .ReadFrom.Configuration(context.Configuration)
    .Enrich.FromLogContext()   // CorrelationId on every line of a request
    .WriteTo.Console(outputTemplate: "[{Timestamp:HH:mm:ss} {Level:u3}] {Message:lj} {CorrelationId}{NewLine}{Exception}", formatProvider: System.Globalization.CultureInfo.InvariantCulture));

builder.Services
    .AddApplication()
    .AddInfrastructure(builder.Configuration)
    .AddApi();

builder.Services.AddBassignJwtAuth(builder.Configuration);
builder.Services.AddBassignWorkers();   // notification sender + WhatsApp inbox — this host only
builder.Services.AddBassignJobs(builder.Configuration, processJobs: true);   // Hangfire: reminders, escalations, digests, retention
builder.Services.AddBassignHardening(HangfireSetup.IsEnabled(builder.Configuration), jobServerCheck: true);
// No CORS: the API serves the native phone app only, so browsers on other origins are refused by default.

// Options bound from config, validated at startup. Secrets never live in source:
// the DB connection string comes from appsettings.Development.json (dev) or an env var (prod).
builder.Services.AddOptions<DatabaseOptions>()
    .Bind(builder.Configuration.GetSection(DatabaseOptions.Section))
    .ValidateDataAnnotations()
    .ValidateOnStart();
builder.Services.AddOptions<AuthOptions>().Bind(builder.Configuration.GetSection(AuthOptions.Section));
builder.Services.AddOptions<WhatsAppOptions>().Bind(builder.Configuration.GetSection(WhatsAppOptions.Section));

var app = builder.Build();

if (app.Environment.IsDevelopment())
{
    using var scope = app.Services.CreateScope();
    try
    {
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        await db.Database.MigrateAsync();
        await DevSeeder.SeedAsync(scope.ServiceProvider);
    }
    catch (Exception ex)
    {
        Log.Warning(ex, "Dev DB migrate/seed skipped (run 'dotnet ef migrations add Initial' first).");
    }
}

app.UseBassignCorrelationId();
app.UseExceptionHandler();
app.UseSerilogRequestLogging(options =>
{
    // The WhatsApp webhook path contains a secret token: never write it to the logs.
    options.GetMessageTemplateProperties = (context, path, elapsed, status) =>
    [
        new Serilog.Events.LogEventProperty("RequestMethod", new Serilog.Events.ScalarValue(context.Request.Method)),
        new Serilog.Events.LogEventProperty("RequestPath", new Serilog.Events.ScalarValue(
            path.StartsWith("/webhooks/whatsapp/", StringComparison.OrdinalIgnoreCase) ? "/webhooks/whatsapp/[Redacted]" : path)),
        new Serilog.Events.LogEventProperty("StatusCode", new Serilog.Events.ScalarValue(status)),
        new Serilog.Events.LogEventProperty("Elapsed", new Serilog.Events.ScalarValue(elapsed))
    ];
});
app.UseBassignSecurityHeaders(HostKind.Api, app.Environment.IsDevelopment());
if (!app.Environment.IsDevelopment()) app.UseHsts();
app.UseAuthentication();
app.UseAuthorization();
app.UseRateLimiter();
app.MapControllers();
app.MapBassignHealth();   // /health, /health/live, /health/ready

Log.Information("Bassign API starting");
app.Run();

/// <summary>Exposed for WebApplicationFactory in the integration tests.</summary>
public partial class Program;
