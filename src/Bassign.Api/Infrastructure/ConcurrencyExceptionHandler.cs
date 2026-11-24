using Bassign.Application.Common.Persistence;
using Microsoft.AspNetCore.Diagnostics;
using Microsoft.AspNetCore.Mvc;

namespace Bassign.Api.Infrastructure;

/// <summary>Someone else changed the record first → 409 Conflict (RFC 9457 problem details).</summary>
public sealed class ConcurrencyExceptionHandler : IExceptionHandler
{
    public async ValueTask<bool> TryHandleAsync(HttpContext httpContext, Exception exception, CancellationToken cancellationToken)
    {
        if (exception is not ConcurrencyConflictException conflict)
            return false;

        httpContext.Response.StatusCode = StatusCodes.Status409Conflict;
        await httpContext.Response.WriteAsJsonAsync(new ProblemDetails
        {
            Status = StatusCodes.Status409Conflict,
            Title = conflict.Message,
            Type = "concurrency"
        }, cancellationToken);
        return true;
    }
}
