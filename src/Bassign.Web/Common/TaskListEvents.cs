namespace Bassign.Web.Common;

/// <summary>Per-circuit "tasks changed" signal, so a task created from the sidebar shows up on an open list.</summary>
public sealed class TaskListEvents
{
    public event Func<Task>? Changed;

    public Task RaiseAsync() => Changed?.Invoke() ?? Task.CompletedTask;
}
