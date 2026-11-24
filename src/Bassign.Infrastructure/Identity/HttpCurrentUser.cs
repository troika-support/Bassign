using System.Security.Claims;
using Bassign.Application.Common.Security;
using Microsoft.AspNetCore.Http;

namespace Bassign.Infrastructure.Identity;

/// <summary>The acting user: a run-as override (WhatsApp, background work) first, then the HTTP caller.</summary>
public sealed class HttpCurrentUser(IHttpContextAccessor accessor, OperationUser operation) : ICurrentUser
{
    public string UserId =>
        operation.UserId is { Length: > 0 } id ? id
        : accessor.HttpContext?.User.FindFirstValue(ClaimTypes.NameIdentifier) ?? "system";
}
