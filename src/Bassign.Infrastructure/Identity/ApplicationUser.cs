using Microsoft.AspNetCore.Identity;

namespace Bassign.Infrastructure.Identity;

public sealed class ApplicationUser : IdentityUser
{
    public string DisplayName { get; set; } = string.Empty;
    public bool IsActive { get; set; } = true;

    /// <summary>Set when personal data was erased (anonymised) under the Data Protection Act.</summary>
    public DateTimeOffset? ErasedAt { get; set; }
}
