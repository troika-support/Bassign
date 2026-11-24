using Bassign.Domain.Organizations;
using Mediator;

namespace Bassign.Application.Organizations.GetOrganization;

public sealed record GetOrganizationQuery : IRequest<OrganizationVm>;

public sealed record OrganizationVm(string Name, string AccentHex, string TimeZoneId, bool SetupCompleted);

public sealed class GetOrganizationHandler(IOrganizationRepository organization)
    : IRequestHandler<GetOrganizationQuery, OrganizationVm>
{
    public async ValueTask<OrganizationVm> Handle(GetOrganizationQuery request, CancellationToken cancellationToken)
    {
        var org = await organization.FindAsync(cancellationToken) ?? OrganizationSettings.CreateDefault();
        return new OrganizationVm(org.Name, org.AccentHex, org.TimeZoneId, org.SetupCompletedAt is not null);
    }
}
