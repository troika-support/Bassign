using System.Net.Http.Json;

namespace Bassign.IntegrationTests;

/// <summary>The offline field app's contract: delta pull by rowversion, idempotent push, server-wins on conflict.</summary>
[Collection(ApiCollection.Name)]
public class SyncTests(BassignApiFactory api)
{
    private static object Op(Guid opId, Guid taskId, string action) => new { opId, taskId, action, proofRef = (string?)null, reason = (string?)null };

    [Fact]
    public async Task Push_is_idempotent_and_pull_returns_only_changes()
    {
        var manager = await api.ManagerAsync();
        var tendai = await api.TendaiAsync();
        var id = await manager.CreateTaskAsync("Count stock — sync test", api.Clock.GetUtcNow().AddDays(2), await api.UserIdAsync("tendai@bassign.local"));

        var first = await tendai.GetFromJsonAsync<PullBody>("/sync/tasks?since=0", Api.Ct);
        var row = Assert.Single(first!.Tasks, t => t.Id == id);
        Assert.Equal("Todo", row.Status);
        Assert.Contains(id, first.ActiveIds);

        var opId = Guid.NewGuid();
        var push = await tendai.PostAsJsonAsync("/sync/push", new { operations = new[] { Op(opId, id, "accept") } }, Api.Ct);
        Assert.Equal("applied", Assert.Single((await push.Content.ReadFromJsonAsync<List<PushResultBody>>(Api.Ct))!).Outcome);

        // The phone retries after a dropped connection: same op id → no second effect.
        var retry = await tendai.PostAsJsonAsync("/sync/push", new { operations = new[] { Op(opId, id, "accept") } }, Api.Ct);
        Assert.Equal("duplicate", Assert.Single((await retry.Content.ReadFromJsonAsync<List<PushResultBody>>(Api.Ct))!).Outcome);

        var delta = await tendai.GetFromJsonAsync<PullBody>($"/sync/tasks?since={first.Token}", Api.Ct);
        var changed = Assert.Single(delta!.Tasks, t => t.Id == id);
        Assert.Equal("Accepted", changed.Status);
        Assert.True(changed.Version > row.Version);

        var nothingNew = await tendai.GetFromJsonAsync<PullBody>($"/sync/tasks?since={delta.Token}", Api.Ct);
        Assert.DoesNotContain(nothingNew!.Tasks, t => t.Id == id);
    }

    [Fact]
    public async Task An_action_on_a_task_reassigned_while_offline_is_rejected_and_dropped()
    {
        var manager = await api.ManagerAsync();
        var tendai = await api.TendaiAsync();
        var id = await manager.CreateTaskAsync("Deliver to Borrowdale — conflict test", api.Clock.GetUtcNow().AddDays(1), await api.UserIdAsync("tendai@bassign.local"));
        await tendai.GetFromJsonAsync<PullBody>("/sync/tasks?since=0", Api.Ct);   // phone has it

        // While the phone is offline, the manager gives it to Rudo.
        (await manager.PostAsJsonAsync($"/tasks/{id}/assign", new { assigneeId = await api.UserIdAsync("rudo@bassign.local") }, Api.Ct)).EnsureSuccessStatusCode();

        var push = await tendai.PostAsJsonAsync("/sync/push", new { operations = new[] { Op(Guid.NewGuid(), id, "accept") } }, Api.Ct);
        Assert.Equal("rejected", Assert.Single((await push.Content.ReadFromJsonAsync<List<PushResultBody>>(Api.Ct))!).Outcome);

        var pull = await tendai.GetFromJsonAsync<PullBody>("/sync/tasks?since=0", Api.Ct);
        Assert.DoesNotContain(id, pull!.ActiveIds);   // the phone removes it

        var rudo = await api.RudoAsync();
        var rudoPull = await rudo.GetFromJsonAsync<PullBody>("/sync/tasks?since=0", Api.Ct);
        Assert.Contains(id, rudoPull!.ActiveIds);
    }
}
