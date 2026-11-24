namespace Bassign.Domain.Teams;

/// <summary>
/// An invitation to join the organization. Only the SHA-256 hash of the link token is stored.
/// Pending = not accepted, not revoked, not expired. Default lifetime 7 days (docs/decisions.md).
/// </summary>
public sealed class Invitation
{
    public static readonly TimeSpan DefaultLifetime = TimeSpan.FromDays(7);

    public Guid Id { get; private init; }
    public string Email { get; private init; } = string.Empty;
    public string Role { get; private init; } = string.Empty;
    public string TokenHash { get; private init; } = string.Empty;
    public string CreatedById { get; private init; } = string.Empty;
    public DateTimeOffset CreatedAt { get; private init; }
    public DateTimeOffset ExpiresAt { get; private init; }
    public DateTimeOffset? AcceptedAt { get; private set; }
    public string? AcceptedUserId { get; private set; }
    public DateTimeOffset? RevokedAt { get; private set; }

    private Invitation() { }   // EF

    public static Invitation Create(
        string email, string role, string tokenHash, string createdById, DateTimeOffset nowUtc, TimeSpan? lifetime = null)
    {
        if (string.IsNullOrWhiteSpace(email) || !email.Contains('@'))
            throw new ArgumentException("A valid email is required.", nameof(email));
        if (!TeamRoles.IsValid(role))
            throw new ArgumentException($"Unknown role '{role}'.", nameof(role));
        if (string.IsNullOrWhiteSpace(tokenHash))
            throw new ArgumentException("Token hash is required.", nameof(tokenHash));
        if (string.IsNullOrWhiteSpace(createdById))
            throw new ArgumentException("CreatedById is required.", nameof(createdById));

        return new Invitation
        {
            Id = Guid.CreateVersion7(),
            Email = email.Trim().ToLowerInvariant(),
            Role = role,
            TokenHash = tokenHash,
            CreatedById = createdById,
            CreatedAt = nowUtc,
            ExpiresAt = nowUtc + (lifetime ?? DefaultLifetime)
        };
    }

    public bool IsPending(DateTimeOffset nowUtc) => AcceptedAt is null && RevokedAt is null && nowUtc < ExpiresAt;

    public void Accept(string userId, DateTimeOffset nowUtc)
    {
        if (!IsPending(nowUtc))
            throw new InvalidOperationException("This invite is no longer valid.");
        if (string.IsNullOrWhiteSpace(userId))
            throw new ArgumentException("UserId is required.", nameof(userId));

        AcceptedAt = nowUtc;
        AcceptedUserId = userId;
    }

    public void Revoke(DateTimeOffset nowUtc)
    {
        if (AcceptedAt is not null)
            throw new InvalidOperationException("An accepted invite can't be revoked.");
        RevokedAt ??= nowUtc;
    }
}
