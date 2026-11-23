using Bassign.Application.Common.Behaviors;
using FluentValidation;
using Mediator;

namespace Bassign.UnitTests.Application;

public class ValidationBehaviorTests
{
    public sealed record SampleRequest(string Name) : IRequest<string>;

    private sealed class NameRequiredValidator : AbstractValidator<SampleRequest>
    {
        public NameRequiredValidator() => RuleFor(x => x.Name).NotEmpty();
    }

    [Fact]
    public async Task Throws_and_short_circuits_when_invalid()
    {
        var behavior = new ValidationBehavior<SampleRequest, string>([new NameRequiredValidator()]);
        var nextCalled = false;
        MessageHandlerDelegate<SampleRequest, string> next = (_, _) =>
        {
            nextCalled = true;
            return ValueTask.FromResult("ok");
        };

        await Assert.ThrowsAsync<ValidationException>(
            async () => await behavior.Handle(new SampleRequest(""), next, CancellationToken.None));
        Assert.False(nextCalled);
    }

    [Fact]
    public async Task Calls_next_when_valid()
    {
        var behavior = new ValidationBehavior<SampleRequest, string>([new NameRequiredValidator()]);
        MessageHandlerDelegate<SampleRequest, string> next = (_, _) => ValueTask.FromResult("ok");

        var result = await behavior.Handle(new SampleRequest("fine"), next, CancellationToken.None);

        Assert.Equal("ok", result);
    }
}
