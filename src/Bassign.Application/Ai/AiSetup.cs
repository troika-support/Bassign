using Bassign.Application.Common.Security;
using Bassign.Application.Identity;
using Bassign.Domain.Common;
using Bassign.Domain.Teams;
using Mediator;

namespace Bassign.Application.Ai;

public sealed record AiUsageSummary(int Calls, int Failures, long InputTokens, long OutputTokens, DateTimeOffset Since);

public sealed record AiSetupStatus(
    string Provider, string? Model, bool Configured, bool SuggestionsEnabled, bool SummariesEnabled, double MinConfidence,
    /// <summary>The key in use came from whoever manages the install (not entered by this organisation's Admin).</summary>
    bool UsingProviderKey,
    AiUsageSummary Usage);

public sealed record AiTestResult(bool Ok, string Message);

public interface IAiSetup
{
    Task<AiSetupStatus> CurrentAsync(CancellationToken cancellationToken = default);
    /// <summary>A one-word round trip to the provider with the current settings.</summary>
    Task<AiTestResult> TestAsync(CancellationToken cancellationToken = default);
}

public sealed record GetAiStatusQuery : IRequest<AiSetupStatus>;

public sealed class GetAiStatusHandler(IAiSetup setup) : IRequestHandler<GetAiStatusQuery, AiSetupStatus>
{
    public async ValueTask<AiSetupStatus> Handle(GetAiStatusQuery request, CancellationToken cancellationToken) => await setup.CurrentAsync(cancellationToken);
}

public sealed record TestAiCommand : IRequest<Result<string>>;

public sealed class TestAiHandler(IAiSetup setup, IIdentityAdmin identity, ICurrentUser currentUser) : IRequestHandler<TestAiCommand, Result<string>>
{
    public async ValueTask<Result<string>> Handle(TestAiCommand request, CancellationToken cancellationToken)
    {
        if (!await identity.IsInRoleAsync(currentUser.UserId, TeamRoles.Admin, cancellationToken))
            return new Error("ai.forbidden", "Only an Admin can test AI.");
        var result = await setup.TestAsync(cancellationToken);
        return result.Ok ? result.Message : new Error("ai.test_failed", result.Message);
    }
}
