using Bassign.Application.Organizations.GetOrganization;
using Mediator;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Bassign.Api.Controllers;

[ApiController]
[Route("organization")]
public sealed class OrganizationController(ISender sender) : ControllerBase
{
    /// <summary>Public: the name and accent shown on sign-in screens (the web sign-in page shows the same).</summary>
    [AllowAnonymous]
    [HttpGet("branding")]
    public async Task<ActionResult<BrandingResponse>> Branding(CancellationToken ct)
    {
        var org = await sender.Send(new GetOrganizationQuery(), ct);
        return Ok(new BrandingResponse(org.Name, Bassign.Domain.Common.ColorContrast.AccessibleOnWhite(org.AccentHex)));   // AAA-safe accent
    }
}

public sealed record BrandingResponse(string Name, string AccentHex);
