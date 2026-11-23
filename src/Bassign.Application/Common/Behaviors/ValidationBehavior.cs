using FluentValidation;
using Mediator;

namespace Bassign.Application.Common.Behaviors;

/// <summary>Runs all FluentValidation validators before the handler; throws on failure (mapped to
/// ProblemDetails at the API boundary). Handlers keep using Result&lt;T&gt; for domain-expected failures.</summary>
public sealed class ValidationBehavior<TMessage, TResponse>(IEnumerable<IValidator<TMessage>> validators)
    : IPipelineBehavior<TMessage, TResponse>
    where TMessage : notnull, IMessage
{
    public async ValueTask<TResponse> Handle(
        TMessage message, MessageHandlerDelegate<TMessage, TResponse> next, CancellationToken cancellationToken)
    {
        if (validators.Any())
        {
            var context = new ValidationContext<TMessage>(message);
            var results = await Task.WhenAll(validators.Select(v => v.ValidateAsync(context, cancellationToken)));
            var failures = results.SelectMany(r => r.Errors).Where(f => f is not null).ToList();
            if (failures.Count != 0)
                throw new ValidationException(failures);
        }

        return await next(message, cancellationToken);
    }
}
