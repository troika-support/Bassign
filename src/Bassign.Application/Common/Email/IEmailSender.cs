namespace Bassign.Application.Common.Email;

public sealed record EmailMessage(string ToAddress, string? ToName, string Subject, string Html, string Text);

/// <summary>Transactional email (SendGrid in production; a pickup folder in development).</summary>
public interface IEmailSender
{
    /// <summary>Throws <see cref="EmailSendException"/> on failure.</summary>
    Task SendAsync(EmailMessage message, CancellationToken cancellationToken = default);
}

public sealed class EmailSendException(string message, bool transient, Exception? inner = null) : Exception(message, inner)
{
    public bool Transient { get; } = transient;
}
