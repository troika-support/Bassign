namespace Bassign.Domain.Common;

/// <summary>An expected failure (validation, not-found, forbidden). Unexpected failures throw.</summary>
public readonly record struct Error(string Code, string Message)
{
    public static readonly Error None = new(string.Empty, string.Empty);
}
