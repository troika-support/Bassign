using System.Buffers.Text;
using Bassign.Application.Identity;
using Bassign.Domain.Common;
using Mediator;
using Microsoft.AspNetCore.Identity;

namespace Bassign.Infrastructure.Identity;

/// <summary>Lists and removes a person's passkeys (only the public half is ever stored).</summary>
public sealed class PasskeyService(UserManager<ApplicationUser> users) : IPasskeys
{
    public static string IdOf(UserPasskeyInfo passkey) => Base64Url.EncodeToString(passkey.CredentialId);

    public async Task<IReadOnlyList<PasskeyInfo>> ListAsync(string userId, CancellationToken cancellationToken = default)
    {
        if (await users.FindByIdAsync(userId) is not { } user) return [];
        return [.. (await users.GetPasskeysAsync(user))
            .OrderBy(p => p.CreatedAt)
            .Select(p => new PasskeyInfo(IdOf(p), string.IsNullOrWhiteSpace(p.Name) ? "Passkey" : p.Name, p.CreatedAt))];
    }

    public async Task<Result<Unit>> RemoveAsync(string userId, string passkeyId, CancellationToken cancellationToken = default)
    {
        if (await users.FindByIdAsync(userId) is not { } user) return new Error("identity.not_found", "Account not found.");
        var match = (await users.GetPasskeysAsync(user)).FirstOrDefault(p => IdOf(p) == passkeyId);
        if (match is null) return new Error("passkey.not_found", "That passkey was already removed.");
        var removed = await users.RemovePasskeyAsync(user, match.CredentialId);
        if (!removed.Succeeded) return new Error("passkey.remove", "Couldn't remove that passkey. Try again.");
        return Unit.Value;
    }

    public async Task RemoveAllAsync(string userId, CancellationToken cancellationToken = default)
    {
        if (await users.FindByIdAsync(userId) is not { } user) return;
        foreach (var passkey in await users.GetPasskeysAsync(user))
            await users.RemovePasskeyAsync(user, passkey.CredentialId);
    }
}
