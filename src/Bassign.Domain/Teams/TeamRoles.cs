namespace Bassign.Domain.Teams;

public static class TeamRoles
{
    public const string Admin = "Admin";
    public const string Manager = "Manager";
    public const string Staff = "Staff";

    public static readonly IReadOnlyList<string> All = [Admin, Manager, Staff];

    public static bool IsValid(string? role) => role is not null && All.Contains(role);
}
