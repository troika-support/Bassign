using System.Net;
using System.Text;
using Bassign.Application.Notifications;

namespace Bassign.Infrastructure.Email;

/// <summary>Simple, accessible HTML emails (inline styles — mail clients ignore stylesheets) plus a text part.</summary>
public static class EmailTemplates
{
    public static (string Html, string Text) Notification(NotificationMessage m, string baseUrl, string accentHex)
    {
        var link = m.Kind == NotificationKinds.DailyDigest || m.TaskId is null
            ? $"{baseUrl.TrimEnd('/')}/"
            : $"{baseUrl.TrimEnd('/')}/tasks/{m.TaskId}";
        var cta = m.Kind == NotificationKinds.DailyDigest ? "Open dashboard" : "Open task";

        var body = new StringBuilder();
        foreach (var line in m.Lines)
            body.Append("<p style=\"margin:0 0 10px;\">").Append(E(line)).Append("</p>");

        if (m.Digest is { Highlights.Count: > 0 } d)
        {
            body.Append("<table role=\"presentation\" style=\"width:100%;border-collapse:collapse;margin:8px 0 16px;font-size:14px;\">");
            foreach (var h in d.Highlights)
                body.Append("<tr><td style=\"padding:6px 0;border-top:1px solid #E7EAEF;\">").Append(E(h.Title))
                    .Append("<br><span style=\"color:#5C6470;\">").Append(E($"{h.State}{(h.Assignee is null ? " · unassigned" : $" · {h.Assignee}")}{(h.DueText is null ? "" : $" · due {h.DueText}")}"))
                    .Append("</span></td></tr>");
            body.Append("</table>");
            if (d.Summary is not null)
                body.Append("<p style=\"margin:0 0 10px;font-size:12px;color:#5C6470;\">The summary above is AI-generated from your task data.</p>");
        }

        var html = $"""
            <!doctype html><html lang="en"><head><meta charset="utf-8"><title>{E(m.Subject)}</title></head>
            <body style="margin:0;padding:24px;background:#F6F7F9;font-family:Segoe UI,Arial,sans-serif;color:#16191D;">
              <div style="max-width:560px;margin:0 auto;background:#FFFFFF;border-radius:12px;padding:28px;">
                <p style="margin:0 0 4px;font-size:13px;color:#5C6470;">{E(m.OrgName)}</p>
                <h1 style="margin:0 0 16px;font-size:20px;">{E(m.Headline)}</h1>
                {body}
                <p style="margin:20px 0 0;"><a href="{E(link)}" style="display:inline-block;background:{E(accentHex)};color:#FFFFFF;text-decoration:none;padding:12px 20px;border-radius:8px;font-weight:600;">{cta}</a></p>
              </div>
              <p style="max-width:560px;margin:16px auto 0;font-size:12px;color:#5C6470;">Sent by Bassign for {E(m.OrgName)}. Link WhatsApp in your Profile to get these on your phone instead.</p>
            </body></html>
            """;

        var text = new StringBuilder().Append(m.Headline).Append("\n\n");
        foreach (var line in m.Lines) text.Append(line).Append('\n');
        if (m.Digest is { } dg)
            foreach (var h in dg.Highlights)
                text.Append("- ").Append(h.Title).Append(" (").Append(h.State).Append(h.Assignee is null ? "" : $", {h.Assignee}").Append(")\n");
        text.Append('\n').Append(cta).Append(": ").Append(link).Append('\n');
        return (html, text.ToString());
    }

    public static (string Subject, string Html, string Text) Invite(string orgName, string role, string link, DateTimeOffset expiresAt, string accentHex)
    {
        var subject = $"You're invited to {orgName} on Bassign";
        var expires = expiresAt.ToString("d MMM yyyy", System.Globalization.CultureInfo.InvariantCulture);
        var html = $"""
            <!doctype html><html lang="en"><head><meta charset="utf-8"><title>{E(subject)}</title></head>
            <body style="margin:0;padding:24px;background:#F6F7F9;font-family:Segoe UI,Arial,sans-serif;color:#16191D;">
              <div style="max-width:560px;margin:0 auto;background:#FFFFFF;border-radius:12px;padding:28px;">
                <h1 style="margin:0 0 16px;font-size:20px;">Join {E(orgName)} on Bassign</h1>
                <p style="margin:0 0 10px;">You've been invited as <strong>{E(role)}</strong>. Bassign is where your team's tasks are assigned and tracked.</p>
                <p style="margin:20px 0;"><a href="{E(link)}" style="display:inline-block;background:{E(accentHex)};color:#FFFFFF;text-decoration:none;padding:12px 20px;border-radius:8px;font-weight:600;">Accept invite</a></p>
                <p style="margin:0;font-size:13px;color:#5C6470;">This link expires on {expires}. If you weren't expecting it, you can ignore this email.</p>
              </div>
            </body></html>
            """;
        var text = $"Join {orgName} on Bassign\n\nYou've been invited as {role}.\nAccept: {link}\n\nThis link expires on {expires}.";
        return (subject, html, text);
    }

    private static string E(string value) => WebUtility.HtmlEncode(value);
}
