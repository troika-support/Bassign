using Bassign.Domain.Teams;
using Bassign.Infrastructure.Options;
using Hangfire;
using Hangfire.Dashboard;
using Hangfire.SqlServer;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace Bassign.Infrastructure.Jobs;

/// <summary>
/// Hangfire on the app's SQL Server database (schema "hangfire", created automatically). The API host
/// PROCESSES jobs; the Web host only shows the admin dashboard. Set Jobs:Enabled=false to switch it off
/// (e.g. integration tests).
/// </summary>
public static class HangfireSetup
{
    public const string DashboardPath = "/hangfire";

    public static bool IsEnabled(IConfiguration configuration) =>
        !string.Equals(configuration["Jobs:Enabled"], "false", StringComparison.OrdinalIgnoreCase);

    public static IServiceCollection AddBassignJobs(this IServiceCollection services, IConfiguration configuration, bool processJobs)
    {
        if (!IsEnabled(configuration)) return services;

        var connectionString = configuration.GetSection(DatabaseOptions.Section)["ConnectionString"]
            ?? throw new InvalidOperationException("Database:ConnectionString is not configured.");

        services.AddHangfire(config => config
            .SetDataCompatibilityLevel(CompatibilityLevel.Version_180)
            .UseSimpleAssemblyNameTypeSerializer()
            .UseRecommendedSerializerSettings()
            .UseSqlServerStorage(connectionString, new SqlServerStorageOptions { SchemaName = "hangfire", PrepareSchemaIfNecessary = true }));

        if (processJobs)
        {
            services.AddScoped<NotificationJobs>();
            services.AddHangfireServer(o =>
            {
                o.ServerName = $"bassign-api:{Environment.MachineName}";
                o.WorkerCount = 2;
            });
            services.AddHostedService<RecurringJobsRegistrar>();
        }
        return services;
    }
}

/// <summary>Registers (or updates) the recurring schedule at startup. Times are checked against the org's
/// time zone inside each handler, so the crons are simply "how often to look".</summary>
public sealed class RecurringJobsRegistrar(IRecurringJobManager jobs, ILogger<RecurringJobsRegistrar> logger) : IHostedService
{
    public Task StartAsync(CancellationToken cancellationToken)
    {
        try
        {
            jobs.AddOrUpdate<NotificationJobs>("reminders", j => j.RemindersAsync(CancellationToken.None), "*/5 * * * *", new RecurringJobOptions());
            jobs.AddOrUpdate<NotificationJobs>("escalations", j => j.EscalationsAsync(CancellationToken.None), "*/5 * * * *", new RecurringJobOptions());
            jobs.AddOrUpdate<NotificationJobs>("daily-digest", j => j.DigestsAsync(CancellationToken.None), "*/15 * * * *", new RecurringJobOptions());
            jobs.AddOrUpdate<NotificationJobs>("retention", j => j.RetentionAsync(CancellationToken.None), "30 0 * * *", new RecurringJobOptions());   // 02:30 Harare
        }
        catch (Exception ex)
        {
            // Never block the API from starting (e.g. database briefly unavailable); restart re-registers.
            logger.LogError(ex, "Couldn't register recurring jobs.");
        }
        return Task.CompletedTask;
    }

    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;
}

/// <summary>The dashboard is for Admins only (it can trigger and delete jobs).</summary>
public sealed class AdminOnlyDashboardFilter : IDashboardAuthorizationFilter
{
    public bool Authorize(DashboardContext context)
    {
        var user = context.GetHttpContext().User;
        return user.Identity?.IsAuthenticated == true && user.IsInRole(TeamRoles.Admin);
    }
}
