namespace Bassign.Domain.Organizations;

/// <summary>A one-off day the organization is closed (a company shutdown, a holiday gazetted at short notice).
/// Treated like a public holiday by the work calendar.</summary>
public sealed class OrgClosure
{
    public const int NameMaxLength = 100;

    public Guid Id { get; private init; }
    public DateOnly Date { get; private init; }
    public string Name { get; private init; } = "";

    private OrgClosure() { }   // EF

    public static OrgClosure Create(DateOnly date, string name)
    {
        if (string.IsNullOrWhiteSpace(name))
            throw new ArgumentException("Give the closure a name, e.g. \"Year-end shutdown\".", nameof(name));
        var trimmed = name.Trim();
        if (trimmed.Length > NameMaxLength)
            throw new ArgumentException($"Keep the name under {NameMaxLength} characters.", nameof(name));
        return new OrgClosure { Id = Guid.NewGuid(), Date = date, Name = trimmed };
    }
}
