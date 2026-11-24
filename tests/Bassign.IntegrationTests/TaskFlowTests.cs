using System.Net;
using System.Net.Http.Json;

namespace Bassign.IntegrationTests;

[Collection(ApiCollection.Name)]
public class TaskFlowTests(BassignApiFactory api)
{
    [Fact]
    public async Task Create_assign_accept_complete()
    {
        var manager = await api.ManagerAsync();
        var tendai = await api.TendaiAsync();
        var tendaiId = await api.UserIdAsync("tendai@bassign.local");

        var id = await manager.CreateTaskAsync("Fix the boardroom projector", api.Clock.GetUtcNow().AddDays(1));
        Assert.Equal(HttpStatusCode.NoContent, (await manager.PostAsJsonAsync($"/tasks/{id}/assign", new { assigneeId = tendaiId }, Api.Ct)).StatusCode);
        Assert.Equal(HttpStatusCode.NoContent, (await tendai.PostAsync($"/tasks/{id}/accept", null, Api.Ct)).StatusCode);
        Assert.Equal(HttpStatusCode.NoContent, (await tendai.PostAsJsonAsync($"/tasks/{id}/complete", new { proofPhotoRef = (string?)null }, Api.Ct)).StatusCode);

        var task = await manager.GetFromJsonAsync<TaskBody>($"/tasks/{id}", Api.Ct);
        Assert.Equal("Done", task!.Status);
        Assert.Equal(tendaiId, task.AssigneeId);

        // Assignment queued a notification in the same transaction (outbox).
        var queued = await api.DbAsync(db => Microsoft.EntityFrameworkCore.EntityFrameworkQueryableExtensions.CountAsync(
            db.OutboundNotifications, n => n.TaskId == id && n.Kind == "task.assigned", Api.Ct));
        Assert.Equal(1, queued);
    }

    [Fact]
    public async Task Staff_cannot_create_tasks()
    {
        var tendai = await api.TendaiAsync();
        var response = await tendai.PostAsJsonAsync("/tasks", new { title = "Sneaky", requiresProofPhoto = false }, Api.Ct);
        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task Staff_cannot_act_on_someone_elses_task()
    {
        var manager = await api.ManagerAsync();
        var tendai = await api.TendaiAsync();
        var id = await manager.CreateTaskAsync("Rudo's task", null, await api.UserIdAsync("rudo@bassign.local"));

        var response = await tendai.PostAsync($"/tasks/{id}/accept", null, Api.Ct);
        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task Anonymous_calls_are_refused()
    {
        var response = await api.CreateClient().GetAsync("/tasks", Api.Ct);
        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }
}
