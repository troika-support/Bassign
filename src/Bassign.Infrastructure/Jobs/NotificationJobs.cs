using Bassign.Application.Notifications.Jobs;
using Bassign.Application.Privacy;
using Hangfire;
using Mediator;
using Microsoft.Extensions.Logging;

namespace Bassign.Infrastructure.Jobs;

/// <summary>
/// Thin Hangfire jobs: each calls one Application handler. Every handler is an idempotent scan (dedupe keys),
/// so no retries are needed — the next scheduled run simply catches up — and two runs never overlap.
/// </summary>
public sealed class NotificationJobs(ISender sender, ILogger<NotificationJobs> logger)
{
    [DisableConcurrentExecution(timeoutInSeconds: 120)]
    [AutomaticRetry(Attempts = 0, OnAttemptsExceeded = AttemptsExceededAction.Delete)]
    public async Task RemindersAsync(CancellationToken cancellationToken) =>
        Log("reminders", await sender.Send(new SendDueRemindersCommand(), cancellationToken));

    [DisableConcurrentExecution(timeoutInSeconds: 120)]
    [AutomaticRetry(Attempts = 0, OnAttemptsExceeded = AttemptsExceededAction.Delete)]
    public async Task EscalationsAsync(CancellationToken cancellationToken) =>
        Log("escalations", await sender.Send(new EscalateOverdueTasksCommand(), cancellationToken));

    [DisableConcurrentExecution(timeoutInSeconds: 300)]
    [AutomaticRetry(Attempts = 0, OnAttemptsExceeded = AttemptsExceededAction.Delete)]
    public async Task DigestsAsync(CancellationToken cancellationToken) =>
        Log("digests", await sender.Send(new SendDailyDigestsCommand(), cancellationToken));

    /// <summary>Spec §7: hard-delete data past its retention period. Daily, idempotent.</summary>
    [DisableConcurrentExecution(timeoutInSeconds: 1800)]
    [AutomaticRetry(Attempts = 0, OnAttemptsExceeded = AttemptsExceededAction.Delete)]
    public async Task RetentionAsync(CancellationToken cancellationToken)
    {
        var report = await sender.Send(new ApplyRetentionCommand(), cancellationToken);
        if (report.Total > 0) logger.LogInformation("Retention removed {Total} record(s).", report.Total);
    }

    private void Log(string job, int queued)
    {
        if (queued > 0) logger.LogInformation("Job {Job} queued {Count} notification(s).", job, queued);
    }
}
