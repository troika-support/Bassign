namespace Bassign.Domain.Tasks;

/// <summary>
/// An Admin-managed label for tasks (Maintenance, Deliveries…). Optional on a task. Never deleted once
/// created — archiving hides it from pickers while old tasks keep their label and reports stay correct.
/// </summary>
public sealed class TaskCategory
{
    public const int NameMaxLength = 40;

    public Guid Id { get; private init; }
    public string Name { get; private set; } = "";
    public bool IsArchived { get; private set; }
    public DateTimeOffset CreatedAt { get; private init; }

    private TaskCategory() { }   // EF

    public static TaskCategory Create(string name, DateTimeOffset nowUtc) =>
        new() { Id = Guid.CreateVersion7(), Name = Clean(name), CreatedAt = nowUtc };

    public void Rename(string name) => Name = Clean(name);
    public void Archive() => IsArchived = true;
    public void Restore() => IsArchived = false;

    private static string Clean(string name)
    {
        if (string.IsNullOrWhiteSpace(name))
            throw new ArgumentException("Give the category a name.", nameof(name));
        var trimmed = string.Join(' ', name.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries));
        if (trimmed.Length > NameMaxLength)
            throw new ArgumentException($"Keep category names under {NameMaxLength} characters.", nameof(name));
        return trimmed;
    }
}
