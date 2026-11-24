using System.Diagnostics;
using System.Text.Json;
using System.Threading.RateLimiting;
using Bassign.Infrastructure.Persistence;
using Hangfire;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Diagnostics.HealthChecks;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Microsoft.Extensions.Logging;

namespace Bassign.Infrastructure.Hosting;

public enum HostKind { Api, Web }

/// <summary>Production-safety plumbing shared by both hosts (step 15): rate limits, security headers,
/// correlation ids, health checks. See docs/hardening.md.</summary>
public static class Hardening
{
    public const string AuthPolicy = "auth";
    public const string WebhookPolicy = "webhook";

    public static IServiceCollection AddBassignHardening(this IServiceCollection services, bool jobsEnabled, bool jobServerCheck)
    {
        services.AddRateLimiter(o =>
        {
            o.RejectionStatusCode = StatusCodes.Status429TooManyRequests;
            // Sign-in / refresh / invite acceptance: slows password guessing to 10 tries a minute per address.
            o.AddPolicy(AuthPolicy, ctx => RateLimitPartition.GetFixedWindowLimiter(
                ctx.Connection.RemoteIpAddress?.ToString() ?? "unknown",
                _ => new FixedWindowRateLimiterOptions { PermitLimit = 10, Window = TimeSpan.FromMinutes(1), QueueLimit = 0 }));
            // Webhooks: generous (the provider batches), but bounded.
            o.AddPolicy(WebhookPolicy, ctx => RateLimitPartition.GetFixedWindowLimiter(
                ctx.Connection.RemoteIpAddress?.ToString() ?? "unknown",
                _ => new FixedWindowRateLimiterOptions { PermitLimit = 600, Window = TimeSpan.FromMinutes(1), QueueLimit = 0 }));
        });

        var checks = services.AddHealthChecks().AddCheck<DatabaseHealthCheck>("database", tags: ["ready"]);
        if (jobsEnabled && jobServerCheck) checks.AddCheck<JobServerHealthCheck>("jobs", tags: ["ready"]);
        return services;
    }

    /// <summary>First in the pipeline, so every log line of the request (including the request summary) carries it.</summary>
    public static IApplicationBuilder UseBassignCorrelationId(this IApplicationBuilder app) => app.UseMiddleware<CorrelationIdMiddleware>();

    public static IApplicationBuilder UseBassignSecurityHeaders(this IApplicationBuilder app, HostKind kind, bool isDevelopment) =>
        app.Use(async (ctx, next) =>
        {
            ctx.Response.OnStarting(() =>
            {
                var h = ctx.Response.Headers;
                h["X-Content-Type-Options"] = "nosniff";
                h["Referrer-Policy"] = "strict-origin-when-cross-origin";
                h["X-Frame-Options"] = "DENY";
                h["Cross-Origin-Opener-Policy"] = "same-origin";
                h["Permissions-Policy"] = "camera=(), microphone=(), geolocation=(), payment=(), usb=()";

                if (kind == HostKind.Api)
                {
                    h["Content-Security-Policy"] = "default-src 'none'; frame-ancestors 'none'";
                    h.CacheControl = "no-store";
                }
                else if (!ctx.Request.Path.StartsWithSegments("/hangfire"))   // the dashboard ships its own scripts
                {
                    var csp = "default-src 'self'; script-src 'self'; "
                        + "style-src 'self' 'unsafe-inline' https://fonts.googleapis.com; "   // MudBlazor sets inline styles
                        + "font-src 'self' https://fonts.gstatic.com data:; img-src 'self' data: blob:; "
                        + $"connect-src 'self' wss://{ctx.Request.Host}; "                      // Blazor Server's websocket
                        + "frame-ancestors 'none'; base-uri 'self'; form-action 'self'; object-src 'none'";
                    // Development: report-only, so hot reload / browser-link never break; check the console for violations.
                    h[isDevelopment ? "Content-Security-Policy-Report-Only" : "Content-Security-Policy"] = csp;
                }
                return Task.CompletedTask;
            });
            await next();
        });

    /// <summary>/health/live = the process answers (for IIS / uptime monitors); /health/ready = database (+ job server).
    /// /health stays as an alias of live. Responses list check names and status only — never exception details.</summary>
    public static IEndpointRouteBuilder MapBassignHealth(this IEndpointRouteBuilder endpoints)
    {
        var live = new HealthCheckOptions { Predicate = _ => false, ResponseWriter = WriteJson };
        endpoints.MapHealthChecks("/health", live).AllowAnonymous().DisableRateLimiting();
        endpoints.MapHealthChecks("/health/live", live).AllowAnonymous().DisableRateLimiting();
        endpoints.MapHealthChecks("/health/ready", new HealthCheckOptions { Predicate = c => c.Tags.Contains("ready"), ResponseWriter = WriteJson })
            .AllowAnonymous().DisableRateLimiting();
        return endpoints;
    }

    private static Task WriteJson(HttpContext ctx, HealthReport report)
    {
        ctx.Response.ContentType = "application/json";
        return ctx.Response.WriteAsync(JsonSerializer.Serialize(new
        {
            status = report.Status.ToString(),
            checks = report.Entries.Select(e => new { name = e.Key, status = e.Value.Status.ToString(), ms = (int)e.Value.Duration.TotalMilliseconds })
        }));
    }
}

public sealed class CorrelationIdMiddleware(RequestDelegate next, ILogger<CorrelationIdMiddleware> logger)
{
    public const string Header = "X-Correlation-ID";

    public async Task InvokeAsync(HttpContext context)
    {
        var id = Sanitize(context.Request.Headers[Header]) ?? Activity.Current?.TraceId.ToString() ?? context.TraceIdentifier;
        context.Response.OnStarting(() =>
        {
            context.Response.Headers[Header] = id;
            return Task.CompletedTask;
        });
        using (logger.BeginScope(new Dictionary<string, object> { ["CorrelationId"] = id }))
            await next(context);
    }

    /// <summary>Accept a caller's id only if it's short and plain (it ends up in logs and a response header).</summary>
    public static string? Sanitize(string? value) =>
        value is { Length: > 0 and <= 64 } && value.All(c => char.IsAsciiLetterOrDigit(c) || c is '-' or '_') ? value : null;
}

public sealed class DatabaseHealthCheck(AppDbContext db) : IHealthCheck
{
    public async Task<HealthCheckResult> CheckHealthAsync(HealthCheckContext context, CancellationToken cancellationToken = default) =>
        await db.Database.CanConnectAsync(cancellationToken)
            ? HealthCheckResult.Healthy()
            : HealthCheckResult.Unhealthy("Can't reach the database.");
}

/// <summary>Healthy when a Hangfire server (the API) has sent a heartbeat in the last 2 minutes.</summary>
public sealed class JobServerHealthCheck(JobStorage storage) : IHealthCheck
{
    public Task<HealthCheckResult> CheckHealthAsync(HealthCheckContext context, CancellationToken cancellationToken = default)
    {
        var alive = storage.GetMonitoringApi().Servers().Any(s => s.Heartbeat is { } beat && DateTime.UtcNow - beat < TimeSpan.FromMinutes(2));
        return Task.FromResult(alive
            ? HealthCheckResult.Healthy()
            : HealthCheckResult.Degraded("No job server heartbeat — is Bassign.Api running?"));
    }
}
