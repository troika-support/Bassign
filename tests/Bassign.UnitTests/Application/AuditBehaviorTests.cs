using Bassign.Application.Common.Auditing;
using Bassign.Application.Common.Behaviors;
using Bassign.Application.Common.Security;
using Mediator;

namespace Bassign.UnitTests.Application;

public class AuditBehaviorTests
{
    public sealed record AuditableReq : IRequest<string>, IAuditableRequest
    {
        public string AuditAction => "DidThing";
        public string AuditEntityType => "Task";
        public string? AuditEntityId => "123";
    }

    public sealed record PlainReq : IRequest<string>;

    private sealed class FakeSink : IAuditSink
    {
        public int Count { get; private set; }
        public string? LastAction { get; private set; }
        public Task RecordAsync(string action, string actorId, string entityType, string? entityId,
            CancellationToken cancellationToken = default)
        {
            Count++;
            LastAction = action;
            return Task.CompletedTask;
        }
    }

    private sealed class FakeUser : ICurrentUser
    {
        public string UserId => "u1";
    }

    [Fact]
    public async Task Records_audit_for_auditable_request()
    {
        var sink = new FakeSink();
        var behavior = new AuditBehavior<AuditableReq, string>(sink, new FakeUser());
        MessageHandlerDelegate<AuditableReq, string> next = (_, _) => ValueTask.FromResult("ok");

        await behavior.Handle(new AuditableReq(), next, CancellationToken.None);

        Assert.Equal(1, sink.Count);
        Assert.Equal("DidThing", sink.LastAction);
    }

    [Fact]
    public async Task Skips_audit_for_plain_request()
    {
        var sink = new FakeSink();
        var behavior = new AuditBehavior<PlainReq, string>(sink, new FakeUser());
        MessageHandlerDelegate<PlainReq, string> next = (_, _) => ValueTask.FromResult("ok");

        await behavior.Handle(new PlainReq(), next, CancellationToken.None);

        Assert.Equal(0, sink.Count);
    }
}
