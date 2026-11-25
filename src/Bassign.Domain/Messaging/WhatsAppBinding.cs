namespace Bassign.Domain.Messaging;

/// <summary>
/// Links one user to one WhatsApp number (spec §G.3). Verification is "reverse": the user is shown a
/// one-time code and sends "LINK 123456" from WhatsApp; the inbound message proves the number is
/// theirs. Only the code's hash is stored; codes expire after 15 minutes. Unverified bindings never
/// resolve to a user. Also holds tiny conversation state (which task is waiting for a proof photo).
/// </summary>
public sealed class WhatsAppBinding
{
    public static readonly TimeSpan LinkCodeLifetime = TimeSpan.FromMinutes(15);

    public Guid Id { get; private init; }
    public string UserId { get; private init; } = string.Empty;
    public string? PhoneE164 { get; private set; }
    public bool Verified { get; private set; }
    public DateTimeOffset? VerifiedAt { get; private set; }
    public string? LinkCodeHash { get; private set; }
    public DateTimeOffset? LinkCodeExpiresAt { get; private set; }
    public Guid? AwaitingProofForTaskId { get; private set; }
    /// <summary>Set after "Decline → Other": the next text message is the reason.</summary>
    public Guid? AwaitingDeclineReasonForTaskId { get; private set; }

    private WhatsAppBinding() { }   // EF

    public static WhatsAppBinding For(string userId)
    {
        if (string.IsNullOrWhiteSpace(userId))
            throw new ArgumentException("UserId is required.", nameof(userId));
        return new WhatsAppBinding { Id = Guid.CreateVersion7(), UserId = userId };
    }

    public void IssueLinkCode(string codeHash, DateTimeOffset nowUtc)
    {
        if (string.IsNullOrWhiteSpace(codeHash))
            throw new ArgumentException("Code hash is required.", nameof(codeHash));
        LinkCodeHash = codeHash;
        LinkCodeExpiresAt = nowUtc + LinkCodeLifetime;
    }

    public bool HasActiveCode(string codeHash, DateTimeOffset nowUtc) =>
        LinkCodeHash is not null && LinkCodeHash == codeHash && LinkCodeExpiresAt > nowUtc;

    public void Verify(string phoneE164, DateTimeOffset nowUtc)
    {
        if (!PhoneNumbers.IsE164(phoneE164))
            throw new ArgumentException("Expected an E.164 phone number.", nameof(phoneE164));
        PhoneE164 = phoneE164;
        Verified = true;
        VerifiedAt = nowUtc;
        LinkCodeHash = null;          // single use
        LinkCodeExpiresAt = null;
        AwaitingProofForTaskId = null;
        AwaitingDeclineReasonForTaskId = null;
    }

    public void Unlink()
    {
        PhoneE164 = null;
        Verified = false;
        VerifiedAt = null;
        LinkCodeHash = null;
        LinkCodeExpiresAt = null;
        AwaitingProofForTaskId = null;
        AwaitingDeclineReasonForTaskId = null;
    }

    public void AwaitProofFor(Guid taskId) => AwaitingProofForTaskId = taskId;
    public void ClearAwaitingProof() => AwaitingProofForTaskId = null;
    public void AwaitDeclineReasonFor(Guid taskId) => AwaitingDeclineReasonForTaskId = taskId;
    public void ClearAwaitingDeclineReason() => AwaitingDeclineReasonForTaskId = null;
}
