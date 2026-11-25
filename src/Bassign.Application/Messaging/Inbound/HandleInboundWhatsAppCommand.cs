using System.Text.RegularExpressions;
using Bassign.Application.Common.Directory;
using Bassign.Application.Common.Persistence;
using Bassign.Application.Common.Security;
using Bassign.Application.Media;
using Bassign.Application.Messaging.StartLink;
using Bassign.Application.Organizations;
using Bassign.Application.Tasks;
using Bassign.Application.Tasks.AcceptTask;
using Bassign.Application.Tasks.CompleteTask;
using Bassign.Application.Tasks.DeclineTask;
using Bassign.Domain.Common;
using Bassign.Domain.Messaging;
using Bassign.Domain.Organizations;
using Bassign.Domain.Tasks;
using Mediator;
using TaskStatus = Bassign.Domain.Tasks.TaskStatus;

namespace Bassign.Application.Messaging.Inbound;

/// <summary>
/// One inbound WhatsApp message → zero or more replies (docs/whatsapp.md, design §8.12). Replies are
/// always inside the 24-hour window (the person just wrote to us), so they may be interactive.
/// Unknown / unverified numbers get the enrollment prompt and NEVER any task data (spec §G.3).
/// Every task action runs AS the linked user, so permissions and the audit trail apply.
/// </summary>
public sealed record HandleInboundWhatsAppCommand(WhatsAppInbound Message) : IRequest<IReadOnlyList<WhatsAppOutbound>>;

public sealed partial class HandleInboundWhatsAppHandler(
    IWhatsAppBindingRepository bindings,
    IWhatsAppLog log,
    IWhatsAppClient client,
    ISecureTokens tokens,
    IRunAs runAs,
    ITaskRepository tasks,
    IOrganizationRepository organization,
    IUserDirectory users,
    IProofStore proofs,
    IUnitOfWork uow,
    Privacy.IConsentRegister consents,
    TimeProvider clock) : IRequestHandler<HandleInboundWhatsAppCommand, IReadOnlyList<WhatsAppOutbound>>
{
    private const int MaxLinkAttemptsPerHour = 6;
    private static readonly string[] ListWords = ["tasks", "task", "list", "menu", "hi", "hello", "start"];

    public async ValueTask<IReadOnlyList<WhatsAppOutbound>> Handle(HandleInboundWhatsAppCommand request, CancellationToken cancellationToken)
    {
        var msg = request.Message;
        var org = await organization.FindAsync(cancellationToken) ?? OrganizationSettings.CreateDefault();

        // 1) "LINK 123456" — verify this number for whoever was shown that code.
        if (msg.Text is { } text && LinkPattern().Match(text) is { Success: true } link)
            return [await LinkAsync(msg.FromE164, link.Groups[1].Value, org, cancellationToken)];

        // 2) Everything else needs a verified binding. Fail closed.
        var binding = await bindings.GetVerifiedByPhoneAsync(msg.FromE164, cancellationToken);
        if (binding is null)
            return [Enrollment(msg.FromE164, org)];

        // "1" or "Accept" typed (or a Twilio template button) → the matching option of the last menu we sent.
        if (msg.ReplyId is null && msg.Text is { } typed && await MatchMenuAsync(msg.FromE164, typed, cancellationToken) is { } chosen)
            msg = msg with { ReplyId = chosen };

        IReadOnlyList<WhatsAppOutbound> replies;
        if (WhatsAppReplyIds.TryParse(msg.ReplyId, out var action, out var taskId, out var arg))
        {
            binding.ClearAwaitingDeclineReason();   // they tapped something else instead of typing a reason
            replies = await ActAsync(binding, action, taskId, arg, msg.FromE164, org, cancellationToken);
        }
        else if (msg.Type == "image")
            replies = [await ProofPhotoAsync(binding, msg, cancellationToken)];
        else if (msg.Text is { } words && ListWords.Contains(words.Trim().ToLowerInvariant()))
        {
            binding.ClearAwaitingDeclineReason();
            replies = await MyTasksAsync(binding.UserId, msg.FromE164, org, cancellationToken);
        }
        else if (binding.AwaitingDeclineReasonForTaskId is { } declining && msg.Text is { } why && !string.IsNullOrWhiteSpace(why))
        {
            binding.ClearAwaitingDeclineReason();
            replies = await DeclineAsync(binding, declining, why.Trim(), msg.FromE164, cancellationToken);
        }
        else
            replies = [Help(msg.FromE164)];

        await uow.SaveChangesAsync(cancellationToken);   // binding conversation state (awaiting proof)
        return replies;
    }

    private async Task<WhatsAppOutbound> LinkAsync(string phone, string code, OrganizationSettings org, CancellationToken ct)
    {
        var now = clock.GetUtcNow();
        if (await log.CountInboundSinceAsync(phone, now.AddHours(-1), "LINK", ct) > MaxLinkAttemptsPerHour)
            return new WaText(phone, "Too many link attempts from this number. Please try again in an hour.");

        var pending = await bindings.GetByActiveCodeHashAsync(LinkCodes.Hash(tokens, code), now, ct);
        if (pending is null)
            return new WaText(phone, "That code isn't valid or has expired. In Bassign, open Profile → Link WhatsApp to get a new one.");

        var existing = await bindings.GetVerifiedByPhoneAsync(phone, ct);
        if (existing is not null && existing.UserId != pending.UserId)
            return new WaText(phone, "This WhatsApp number is already linked to another Bassign account. Ask your manager for help.");

        pending.Verify(phone, now);
        consents.Record(pending.UserId, Privacy.ConsentPurposes.WhatsApp, granted: true, source: "whatsapp:link", at: now);
        await uow.SaveChangesAsync(ct);

        var name = (await users.GetDisplayNamesAsync([pending.UserId], ct)).GetValueOrDefault(pending.UserId, "there");
        return new WaText(phone,
            $"✅ Linked! Hi {name.Split(' ')[0]} — your {org.Name} tasks will come here. Reply TASKS any time to see them.");
    }

    private async Task<string?> MatchMenuAsync(string phone, string typed, CancellationToken ct)
    {
        var text = typed.Trim();
        if (text.Length is 0 or > 40) return null;
        var menu = await log.LastOutboundActionsAsync(phone, clock.GetUtcNow() - WhatsAppComposer.Window, ct);
        if (menu.Count == 0) return null;
        if (int.TryParse(text, System.Globalization.NumberStyles.Integer, System.Globalization.CultureInfo.InvariantCulture, out var n))
            return n >= 1 && n <= menu.Count ? menu[n - 1].Id : null;
        return menu.FirstOrDefault(m => string.Equals(m.Title, text, StringComparison.OrdinalIgnoreCase))?.Id;
    }

    private static WaText Enrollment(string phone, OrganizationSettings org) =>
        new(phone, $"Hi! This number isn't linked to {org.Name} on Bassign yet. Open Bassign → Profile → Link WhatsApp, then send the code it shows you here.");

    private static WaText Help(string phone) =>
        new(phone, "Reply TASKS to see your tasks. Use the buttons on a task to accept, decline or mark it done — and send a photo when a task asks for one.");

    private async Task<IReadOnlyList<WhatsAppOutbound>> ActAsync(
        WhatsAppBinding binding, string action, Guid taskId, string? arg, string phone, OrganizationSettings org, CancellationToken ct)
    {
        var task = await tasks.GetByIdAsync(taskId, ct);
        if (task is null || task.AssigneeId != binding.UserId)
            return [new WaText(phone, "That task isn't assigned to you any more.")];

        var due = DueText.Format(task.DueDate, org.TimeZoneId);
        switch (action)
        {
            case WhatsAppReplyIds.View:
                return [WhatsAppComposer.TaskCard(phone, task.Id, task.Title, task.Status.ToString(), due, task.RequiresProofPhoto)];

            case WhatsAppReplyIds.Accept:
            {
                var result = await runAs.SendAsync(binding.UserId, new AcceptTaskCommand(task.Id), ct);
                if (!result.IsSuccess) return [new WaText(phone, result.Error.Message)];
                var dueBit = due is null ? "" : $" — due {due}";
                if (task.RequiresProofPhoto)
                {
                    binding.AwaitProofFor(task.Id);
                    return [new WaText(phone, $"Accepted{dueBit}. Send a photo here when it's done.")];
                }
                return [new WaButtons(phone, $"Accepted{dueBit}. Tap Mark done when it's finished.",
                    [new WaButton($"{WhatsAppReplyIds.Done}:{task.Id}", "Mark done")])];
            }

            case WhatsAppReplyIds.Decline:
            {
                if (task.Status != TaskStatus.Todo)
                    return [new WaText(phone, "Only a new task can be declined.")];
                if (arg == DeclineReasons.Other)
                {
                    binding.AwaitDeclineReasonFor(task.Id);
                    return [new WaText(phone, $"What's the reason for declining \"{task.Title}\"? Reply in a few words.")];
                }
                if (DeclineReasons.LabelFor(arg) is { } reason)
                    return await DeclineAsync(binding, task.Id, reason, phone, ct);
                if (!org.DeclineReasonRequired)
                    return await DeclineAsync(binding, task.Id, null, phone, ct);

                // Decision #20: one more tap — pick why, so the manager can reassign well.
                var rows = DeclineReasons.All
                    .Select(r => new WaListRow(WhatsAppReplyIds.DeclineWith(task.Id, r.Code), r.Label,
                        r.Code == DeclineReasons.Other ? "Type your own reason" : null))
                    .ToList();
                return [new WaList(phone, $"Why are you declining \"{task.Title}\"?", "Pick a reason", rows)];
            }

            case WhatsAppReplyIds.Done:
            {
                if (task.RequiresProofPhoto && task.Status == TaskStatus.Accepted)
                {
                    binding.AwaitProofFor(task.Id);
                    return [new WaText(phone, "This task needs a photo. Send a photo of the finished work here to mark it done.")];
                }
                var result = await runAs.SendAsync(binding.UserId, new CompleteTaskCommand(task.Id, null), ct);
                return [new WaText(phone, result.IsSuccess ? "Marked done — thanks." : result.Error.Message)];
            }

            default:
                return [Help(phone)];
        }
    }

    private async Task<IReadOnlyList<WhatsAppOutbound>> DeclineAsync(WhatsAppBinding binding, Guid taskId, string? reason, string phone, CancellationToken ct)
    {
        var result = await runAs.SendAsync(binding.UserId, new DeclineTaskCommand(taskId, reason), ct);
        return [new WaText(phone, result.IsSuccess ? "Declined — your manager will reassign it." : result.Error.Message)];
    }

    private async Task<WhatsAppOutbound> ProofPhotoAsync(WhatsAppBinding binding, WhatsAppInbound msg, CancellationToken ct)
    {
        var taskId = binding.AwaitingProofForTaskId ?? await SingleTaskAwaitingPhotoAsync(binding.UserId, ct);
        if (taskId is null)
            return new WaText(msg.FromE164, "Thanks for the photo. Which task is it for? Reply TASKS, open the task, tap Mark done, then send the photo.");
        if (msg.MediaId is null)
            return new WaText(msg.FromE164, "I couldn't read that photo. Please send it again.");

        // Media URLs are short-lived: download now (this runs in the background worker, not the webhook).
        var media = await client.DownloadMediaAsync(msg.MediaId, ct);
        if (media is not { } photo || !ProofReferences.AllowedContentTypes.Contains(photo.ContentType))
            return new WaText(msg.FromE164, "I couldn't download that photo. Please send it again as a normal picture.");

        using var stream = new MemoryStream(photo.Content);
        var reference = await proofs.SaveAsync(Guid.NewGuid(), stream, photo.ContentType, ct);
        var result = await runAs.SendAsync(binding.UserId, new CompleteTaskCommand(taskId.Value, reference), ct);
        if (!result.IsSuccess)
            return new WaText(msg.FromE164, result.Error.Message);

        binding.ClearAwaitingProof();
        return new WaText(msg.FromE164, "Photo received — marked done. Thanks!");
    }

    /// <summary>If exactly one accepted task is waiting for a photo, a photo obviously belongs to it.</summary>
    private async Task<Guid?> SingleTaskAwaitingPhotoAsync(string userId, CancellationToken ct)
    {
        var mine = await tasks.ListAsync(TaskStatus.Accepted, userId, ct);
        var waiting = mine.Where(t => t.RequiresProofPhoto).Take(2).ToList();
        return waiting.Count == 1 ? waiting[0].Id : null;
    }

    private async Task<IReadOnlyList<WhatsAppOutbound>> MyTasksAsync(string userId, string phone, OrganizationSettings org, CancellationToken ct)
    {
        var open = (await tasks.ListAsync(null, userId, ct))
            .Where(t => t.Status is TaskStatus.Todo or TaskStatus.Accepted)
            .OrderBy(t => t.DueDate ?? DateTimeOffset.MaxValue)
            .ToList();

        if (open.Count == 0)
            return [new WaText(phone, "You're all caught up — new tasks will appear here.")];

        if (open.Count == 1)
        {
            var t = open[0];
            return [WhatsAppComposer.TaskCard(phone, t.Id, t.Title, t.Status.ToString(), DueText.Format(t.DueDate, org.TimeZoneId), t.RequiresProofPhoto)];
        }

        var rows = open.Take(10).Select(t => new WaListRow(
            $"{WhatsAppReplyIds.View}:{t.Id}", t.Title,
            (t.Status == TaskStatus.Accepted ? "In progress" : "To do")
                + (DueText.Format(t.DueDate, org.TimeZoneId) is { } d ? $" · due {d}" : ""))).ToList();
        var more = open.Count > 10 ? $" (showing the first 10 of {open.Count})" : "";
        return [new WaList(phone, $"You have {open.Count} open tasks{more}. Pick one:", "View tasks", rows)];
    }

    [GeneratedRegex(@"^\s*LINK\s*(\d{6})\s*$", RegexOptions.IgnoreCase)]
    private static partial Regex LinkPattern();
}
