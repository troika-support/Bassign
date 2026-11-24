using System.Net;
using System.Net.Http.Json;
using Bassign.Application.Ai;
using Bassign.Application.Ai.SuggestAssignee;
using Microsoft.EntityFrameworkCore;

namespace Bassign.IntegrationTests;

/// <summary>Spec §3.F.2: AI failure never blocks the core flow.</summary>
[Collection(ApiCollection.Name)]
public class AiDegradeTests(BassignApiFactory api)
{
    [Fact]
    public async Task When_the_AI_provider_fails_assignment_still_works()
    {
        var manager = await api.ManagerAsync();
        var id = await manager.CreateTaskAsync("Service the generator — AI test", api.Clock.GetUtcNow().AddDays(1));
        api.Ai.Fail = true;
        try
        {
            var suggestion = await api.SendAsync(new SuggestAssigneeQuery(id));
            Assert.Equal(AiOutcome.Unavailable, suggestion.Outcome);
            Assert.Null(suggestion.UserId);

            var assign = await manager.PostAsJsonAsync($"/tasks/{id}/assign", new { assigneeId = await api.UserIdAsync("tendai@bassign.local") }, Api.Ct);
            Assert.Equal(HttpStatusCode.NoContent, assign.StatusCode);

            // The failure is metered, so a flaky provider shows up in AiUsage.
            Assert.True(await api.DbAsync(db => db.AiUsage.AnyAsync(u => u.Feature == "assign" && u.Outcome == "unavailable", Api.Ct)));
        }
        finally
        {
            api.Ai.Fail = false;
        }
    }

    [Fact]
    public async Task A_valid_answer_names_a_real_team_member()
    {
        var manager = await api.ManagerAsync();
        var id = await manager.CreateTaskAsync("Reconcile EcoCash receipts — AI test", api.Clock.GetUtcNow().AddDays(1));
        api.Ai.Reply = """{"candidate":"c1","confidence":0.9,"reasoning":"Lightest workload."}""";

        var suggestion = await api.SendAsync(new SuggestAssigneeQuery(id));

        Assert.Equal(AiOutcome.Ok, suggestion.Outcome);
        Assert.False(string.IsNullOrEmpty(suggestion.Name));
        Assert.True(await api.DbAsync(db => db.Users.AnyAsync(u => u.Id == suggestion.UserId && u.IsActive, Api.Ct)));
    }

    [Fact]
    public async Task A_made_up_candidate_is_ignored()
    {
        var manager = await api.ManagerAsync();
        var id = await manager.CreateTaskAsync("Paint the gate — AI test", api.Clock.GetUtcNow().AddDays(1));
        api.Ai.Reply = """{"candidate":"c99","confidence":0.99,"reasoning":"Trust me."}""";

        var suggestion = await api.SendAsync(new SuggestAssigneeQuery(id));

        Assert.Equal(AiOutcome.NoConfidentMatch, suggestion.Outcome);
        Assert.Null(suggestion.UserId);
    }
}
