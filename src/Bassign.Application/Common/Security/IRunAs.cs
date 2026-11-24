using Mediator;

namespace Bassign.Application.Common.Security;

/// <summary>Sends a request in a fresh scope AS another user — for channels with no signed-in session
/// (a WhatsApp message from a linked number). Permissions, validation and the audit trail all apply
/// exactly as if that user had acted in the app.</summary>
public interface IRunAs
{
    Task<TResponse> SendAsync<TResponse>(string userId, IRequest<TResponse> request, CancellationToken cancellationToken = default);
}
