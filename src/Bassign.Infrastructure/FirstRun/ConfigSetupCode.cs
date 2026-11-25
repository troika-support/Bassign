using Bassign.Application.FirstRun;
using Microsoft.Extensions.Configuration;

namespace Bassign.Infrastructure.FirstRun;

/// <summary>Setup__Code from the server's environment (never appsettings, never the database). Read live,
/// so the installer can remove it after the first Admin exists without a restart.</summary>
public sealed class ConfigSetupCode(IConfiguration configuration) : ISetupCode
{
    public string? Value => configuration["Setup:Code"];
}
