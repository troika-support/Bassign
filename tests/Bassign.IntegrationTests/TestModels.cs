using System.Net.Http.Json;

namespace Bassign.IntegrationTests;

// Just the fields the tests read from API responses.
public sealed record CreatedBody(Guid Id);
public sealed record TaskBody(Guid Id, string Title, string Status, string? AssigneeId);
public sealed record PullBody(long Token, List<SyncRowBody> Tasks, List<Guid> ActiveIds);
public sealed record SyncRowBody(Guid Id, string Status, string? AssigneeId, long Version);
public sealed record PushResultBody(Guid OpId, string Outcome, string? Message);

public static class Api
{
    public static CancellationToken Ct => TestContext.Current.CancellationToken;

    public static async Task<Guid> CreateTaskAsync(this HttpClient manager, string title, DateTimeOffset? due, string? assigneeId = null)
    {
        var response = await manager.PostAsJsonAsync("/tasks", new { title, detail = (string?)null, dueDate = due, requiresProofPhoto = false, assigneeId }, Ct);
        Assert.Equal(System.Net.HttpStatusCode.Created, response.StatusCode);
        return (await response.Content.ReadFromJsonAsync<CreatedBody>(Ct))!.Id;
    }
}
