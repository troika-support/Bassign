namespace Bassign.Application.Common.Security;

/// <summary>Random link tokens; only the hash is ever persisted.</summary>
public interface ISecureTokens
{
    (string Raw, string Hash) Create();
    string Hash(string raw);
}
