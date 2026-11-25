using Bassign.Application.Common.Email;
using Microsoft.Extensions.Options;

namespace Bassign.Infrastructure.Email;

public sealed class EmailSetup(IOptionsMonitor<EmailOptions> options) : IEmailSetup
{
    public EmailSetupStatus Current()
    {
        var o = options.CurrentValue;
        return new EmailSetupStatus(o.Normalized, !o.IsDisabled, o.IsReady, o.Missing(),
            string.IsNullOrWhiteSpace(o.EffectiveFromAddress) ? null : o.EffectiveFromAddress);
    }
}
