using Bassign.Application.Tasks.AcceptTask;
using Bassign.Application.Tasks.AssignTask;
using Bassign.Application.Tasks.CompleteTask;
using Bassign.Application.Tasks.CreateTask;
using Bassign.Application.Tasks.DeclineTask;
using Bassign.Application.Tasks.GetTaskById;
using Bassign.Application.Tasks.ListTasks;
using Bassign.Domain.Common;
using Mediator;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.ModelBinding;

namespace Bassign.Api.Controllers;

[ApiController]
[Route("tasks")]
[Authorize]
public sealed class TasksController(ISender sender) : ControllerBase
{
    [HttpPost]
    [Authorize(Policy = "Managers")]
    public async Task<IActionResult> Create([FromBody] CreateTaskRequest body, CancellationToken ct)
    {
        var result = await sender.Send(
            new CreateTaskCommand(body.Title, body.Detail, body.DueDate, body.RequiresProofPhoto, body.AssigneeId), ct);
        return result.IsSuccess
            ? CreatedAtAction(nameof(GetById), new { id = result.Value }, new { id = result.Value })
            : ToProblem(result.Error);
    }

    [HttpGet("{id:guid}", Name = nameof(GetById))]
    public async Task<IActionResult> GetById(Guid id, CancellationToken ct)
    {
        var result = await sender.Send(new GetTaskByIdQuery(id), ct);
        return result.IsSuccess ? Ok(result.Value) : ToProblem(result.Error);
    }

    [HttpGet]
    public async Task<IActionResult> List([FromQuery] string? status, [FromQuery] string? assigneeId, CancellationToken ct)
    {
        var items = await sender.Send(new ListTasksQuery(status, assigneeId), ct);
        return Ok(items);
    }

    [HttpPost("{id:guid}/assign")]
    [Authorize(Policy = "Managers")]
    public async Task<IActionResult> Assign(Guid id, [FromBody] AssignTaskRequest body, CancellationToken ct)
    {
        var result = await sender.Send(new AssignTaskCommand(id, body.AssigneeId), ct);
        return result.IsSuccess ? NoContent() : ToProblem(result.Error);
    }

    [HttpPost("{id:guid}/accept")]
    public async Task<IActionResult> Accept(Guid id, CancellationToken ct)
    {
        var result = await sender.Send(new AcceptTaskCommand(id), ct);
        return result.IsSuccess ? NoContent() : ToProblem(result.Error);
    }

    [HttpPost("{id:guid}/complete")]
    public async Task<IActionResult> Complete(
        Guid id,
        [FromBody(EmptyBodyBehavior = EmptyBodyBehavior.Allow)] CompleteTaskRequest? body,
        CancellationToken ct)
    {
        var result = await sender.Send(new CompleteTaskCommand(id, body?.ProofPhotoRef), ct);
        return result.IsSuccess ? NoContent() : ToProblem(result.Error);
    }

    [HttpPost("{id:guid}/decline")]
    public async Task<IActionResult> Decline(
        Guid id,
        [FromBody(EmptyBodyBehavior = EmptyBodyBehavior.Allow)] DeclineTaskRequest? body,
        CancellationToken ct)
    {
        var result = await sender.Send(new DeclineTaskCommand(id, body?.Reason), ct);
        return result.IsSuccess ? NoContent() : ToProblem(result.Error);
    }

    private ObjectResult ToProblem(Error error)
    {
        var status = error.Code.EndsWith("not_found", StringComparison.Ordinal) ? StatusCodes.Status404NotFound
            : error.Code.Contains("invalid", StringComparison.Ordinal) ? StatusCodes.Status409Conflict
            : StatusCodes.Status400BadRequest;
        return Problem(statusCode: status, title: error.Message, type: error.Code);
    }
}

public sealed record CreateTaskRequest(
    string Title, string? Detail, DateTimeOffset? DueDate, bool RequiresProofPhoto, string? AssigneeId);
public sealed record AssignTaskRequest(string AssigneeId);
public sealed record CompleteTaskRequest(string? ProofPhotoRef);
public sealed record DeclineTaskRequest(string? Reason);
