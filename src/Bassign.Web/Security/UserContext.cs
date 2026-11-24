namespace Bassign.Web.Security;

/// <summary>Per-operation holder for the acting user's id, set by UiSender when it opens a scope.</summary>
public sealed class UserContext
{
    public string? UserId { get; set; }
}
