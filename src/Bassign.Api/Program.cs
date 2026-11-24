using Bassign.Api;
using Bassign.Application;
using Bassign.Infrastructure;
using Bassign.Infrastructure.Identity;
using Bassign.Infrastructure.Options;
using Bassign.Infrastructure.Persistence;
using Bassign.Infrastructure.Persistence.Seed;
using Microsoft.EntityFrameworkCore;
using Serilog;

var builder = WebApplication.CreateBuilder(args);

builder.Host.UseSerilog((context, loggerConfig) =>
    loggerConfig.ReadFrom.Configuration(context.Configuration).WriteTo.Console());

builder.Services
    .AddApplication()
    .AddInfrastructure(builder.Configuration)
    .AddApi();

builder.Services.AddBassignJwtAuth(builder.Configuration);
builder.Services.AddBassignWorkers();   // notification sender + WhatsApp inbox — this host only

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
app.UseAuthentication();
app.UseAuthorization();
app.MapControllers();
app.MapGet("/health", () => Results.Ok(new { status = "ok", service = "Bassign.Api" }));

Log.Information("Bassign API starting");
app.Run();
