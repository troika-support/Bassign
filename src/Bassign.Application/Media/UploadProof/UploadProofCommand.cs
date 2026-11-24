using Bassign.Domain.Common;
using FluentValidation;
using Mediator;

namespace Bassign.Application.Media.UploadProof;

/// <summary>Store a proof photo keyed by the device's operation id (safe to retry).</summary>
public sealed record UploadProofCommand(Guid OperationId, Stream Content, string ContentType, long Length)
    : IRequest<Result<string>>;

public sealed class UploadProofCommandValidator : AbstractValidator<UploadProofCommand>
{
    public UploadProofCommandValidator()
    {
        RuleFor(x => x.OperationId).NotEmpty();
        RuleFor(x => x.ContentType).Must(ProofReferences.AllowedContentTypes.Contains).WithMessage("Upload a JPEG or PNG photo.");
        RuleFor(x => x.Length).InclusiveBetween(1, ProofReferences.MaxBytes).WithMessage("Photos must be under 5 MB.");
    }
}

public sealed class UploadProofHandler(IProofStore store) : IRequestHandler<UploadProofCommand, Result<string>>
{
    public async ValueTask<Result<string>> Handle(UploadProofCommand request, CancellationToken cancellationToken) =>
        await store.SaveAsync(request.OperationId, request.Content, request.ContentType, cancellationToken);
}
