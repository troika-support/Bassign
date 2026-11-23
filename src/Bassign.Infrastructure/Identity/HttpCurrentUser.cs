using System.Security.Claims;
using Bassign.Application.Common.Security;
using Microsoft.AspNetCore.Http;

namespace Bassign.Infrastructure.Identity;

/// <summary>Resolves the acting user from the current request. Falls back to "system" (e.g. jobs).</summary>
public sealed class HttpCurrentUser(IHttpContextAccessor accessor) : ICurrentUser
{
    public string UserId =>
        accessor.HttpContext?.User.FindFirstValue(ClaimTypes.NameIdentifier) ?? "system";
}
