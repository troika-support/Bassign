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

// Options bound from config, validated at startup. Secrets never live in source:
// the DB connection string comes from appsettings.Development.json (dev) or an env var (prod).
builder.Services.AddOptions<DatabaseOptions>()
    .Bind(builder.Configuration.GetSection(DatabaseOptions.Section))
    .ValidateDataAnnotations()
    .ValidateOnStart();
builder.Services.AddOptions<AuthOptions>().Bind(builder.Configuration.GetSection(AuthOptions.Section));
builder.Services.AddOptions<WhatsAppOptions>().Bind(builder.Configuration.GetSection(WhatsAppOptions.Section));
builder.Services.AddOptions<AiOptions>().Bind(builder.Configuration.GetSection(AiOptions.Section));

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
app.UseSerilogRequestLogging();
app.UseAuthentication();
app.UseAuthorization();
app.MapControllers();
app.MapGet("/health", () => Results.Ok(new { status = "ok", service = "Bassign.Api" }));

Log.Information("Bassign API starting");
app.Run();
