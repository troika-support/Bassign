using Bassign.Application.Common.Security;

namespace Bassign.Infrastructure.Identity;

public sealed class SecureTokens : ISecureTokens
{
    public (string Raw, string Hash) Create()
    {
        var raw = TokenHasher.NewRawToken();
        return (raw, TokenHasher.Hash(raw));
    }

    public string Hash(string raw) => TokenHasher.Hash(raw);
}
