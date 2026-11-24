using Bassign.Application.Media.UploadProof;
using Bassign.Application.Sync;
using Bassign.Application.Sync.PullTasks;
using Bassign.Application.Sync.PushOperations;
using Mediator;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Bassign.Api.Controllers;

/// <summary>Field-device sync: delta pull, idempotent push, and proof-photo upload.</summary>
[ApiController]
[Route("sync")]
[Authorize]
public sealed class SyncController(ISender sender) : ControllerBase
{
    /// <summary>Tasks assigned to the caller that changed after <paramref name="since"/> (0 = everything).</summary>
    [HttpGet("tasks")]
    public async Task<ActionResult<SyncPullResult>> Pull([FromQuery] long since, CancellationToken ct) =>
        Ok(await sender.Send(new PullTasksQuery(since), ct));

    /// <summary>Apply queued device actions in order; each op id is applied at most once.</summary>
    [HttpPost("push")]
    public async Task<ActionResult<IReadOnlyList<SyncOperationResult>>> Push([FromBody] PushRequest body, CancellationToken ct) =>
        Ok(await sender.Send(new PushOperationsCommand(body.Operations), ct));

    /// <summary>Upload a compressed proof photo for a queued completion; safe to retry with the same opId.</summary>
    [HttpPost("proof")]
    [RequestSizeLimit(6_000_000)]
    public async Task<IActionResult> UploadProof([FromForm] Guid opId, IFormFile file, CancellationToken ct)
    {
        await using var stream = file.OpenReadStream();
        var result = await sender.Send(new UploadProofCommand(opId, stream, file.ContentType, file.Length), ct);
        return result.IsSuccess
            ? Ok(new ProofUploaded(result.Value!))
            : Problem(statusCode: StatusCodes.Status400BadRequest, title: result.Error.Message, type: result.Error.Code);
    }
}

public sealed record PushRequest(IReadOnlyList<SyncOperation> Operations);
public sealed record ProofUploaded(string Reference);
