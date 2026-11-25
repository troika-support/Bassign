using Bassign.Application.Common.Directory;
using Bassign.Application.Common.Persistence;
using Bassign.Application.Common.Security;
using Bassign.Application.Media;
using Bassign.Application.Messaging;
using Bassign.Application.Messaging.Inbound;
using Bassign.Application.Messaging.StartLink;
using Bassign.Application.Organizations;
using Bassign.Application.Tasks;
using Bassign.Application.Tasks.AcceptTask;
using Bassign.Application.Tasks.DeclineTask;
using Bassign.Domain.Common;
using Bassign.Domain.Messaging;
using Bassign.Domain.Organizations;
using Bassign.Domain.Tasks;
using Mediator;
using TaskStatus = Bassign.Domain.Tasks.TaskStatus;

namespace Bassign.UnitTests.Messaging;

public class HandleInboundWhatsAppTests
{
    private const string Phone = "+263771234567";
    private const string UserId = "staff-1";

    // ---------- fakes ----------
    private sealed class Bindings : IWhatsAppBindingRepository
    {
        public List<WhatsAppBinding> All { get; } = [];
        public void Add(WhatsAppBinding binding) => All.Add(binding);
        public Task<WhatsAppBinding?> GetByUserIdAsync(string userId, CancellationToken cancellationToken = default) =>
            Task.FromResult(All.FirstOrDefault(b => b.UserId == userId));
        public Task<WhatsAppBinding?> GetVerifiedByPhoneAsync(string phoneE164, CancellationToken cancellationToken = default) =>
            Task.FromResult(All.FirstOrDefault(b => b.Verified && b.PhoneE164 == phoneE164));
        public Task<WhatsAppBinding?> GetByActiveCodeHashAsync(string codeHash, DateTimeOffset nowUtc, CancellationToken cancellationToken = default) =>
            Task.FromResult(All.FirstOrDefault(b => b.HasActiveCode(codeHash, nowUtc)));
    }

    private sealed class Log : IWhatsAppLog
    {
        public int LinkAttempts { get; set; }
        public List<WaButton> Menu { get; } = [];
        public Task<IReadOnlyList<WaButton>> LastOutboundActionsAsync(string phoneE164, DateTimeOffset since, CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyList<WaButton>>(Menu);
        public Task<DateTimeOffset?> LastInboundAtAsync(string phoneE164, CancellationToken cancellationToken = default) => Task.FromResult<DateTimeOffset?>(null);
        public void RecordInbound(WhatsAppInbound message) { }
        public Task<int> CountInboundSinceAsync(string phoneE164, DateTimeOffset since, string textPrefix, CancellationToken cancellationToken = default) => Task.FromResult(LinkAttempts);
        public void RecordOutbound(WhatsAppOutbound message, string providerMessageId, DateTimeOffset at) { }
        public Task UpdateStatusAsync(string providerMessageId, string status, CancellationToken cancellationToken = default) => Task.CompletedTask;
    }

    private sealed class Client : IWhatsAppClient
    {
        public Task<string> SendAsync(WhatsAppOutbound message, CancellationToken cancellationToken = default) => Task.FromResult("x");
        public Task<(byte[] Content, string ContentType)?> DownloadMediaAsync(string mediaId, CancellationToken cancellationToken = default) =>
            Task.FromResult<(byte[] Content, string ContentType)?>((new byte[] { 1, 2, 3 }, "image/jpeg"));
    }

    private sealed class Tokens : ISecureTokens
    {
        public (string Raw, string Hash) Create() => ("raw", "h:raw");
        public string Hash(string raw) => "h:" + raw;
    }

    /// <summary>Records what would run as whom; every command succeeds.</summary>
    private sealed class RecordingRunAs : IRunAs
    {
        public List<(string UserId, object Request)> Calls { get; } = [];
        public Task<TResponse> SendAsync<TResponse>(string userId, IRequest<TResponse> request, CancellationToken cancellationToken = default)
        {
            Calls.Add((userId, request));
            return Task.FromResult((TResponse)(object)Result<Unit>.Success(Unit.Value));
        }
    }

    private sealed class TaskRepo : ITaskRepository
    {
        public List<TaskItem> All { get; } = [];
        public int Reads { get; private set; }
        public void Add(TaskItem task) => All.Add(task);
        public Task<TaskItem?> GetByIdAsync(Guid id, CancellationToken cancellationToken = default)
        {
            Reads++;
            return Task.FromResult(All.FirstOrDefault(t => t.Id == id));
        }
        public Task<IReadOnlyList<TaskItem>> ListAsync(TaskStatus? status, string? assigneeId, CancellationToken cancellationToken = default)
        {
            Reads++;
            return Task.FromResult<IReadOnlyList<TaskItem>>(All
                .Where(t => (status is null || t.Status == status) && (assigneeId is null || t.AssigneeId == assigneeId)).ToList());
        }
    }

    private sealed class Org : IOrganizationRepository
    {
        public Task<OrganizationSettings?> FindAsync(CancellationToken cancellationToken = default) => Task.FromResult<OrganizationSettings?>(null);
        public void Add(OrganizationSettings settings) { }
        public Task<IReadOnlyList<OrgClosure>> ListClosuresAsync(DateOnly from, DateOnly to, CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyList<OrgClosure>>([]);
        public Task<OrgClosure?> FindClosureAsync(Guid id, CancellationToken cancellationToken = default) => Task.FromResult<OrgClosure?>(null);
        public void AddClosure(OrgClosure closure) { }
        public void RemoveClosure(OrgClosure closure) { }
    }

    private sealed class Users : IUserDirectory
    {
        public Task<IReadOnlyDictionary<string, string>> GetDisplayNamesAsync(IReadOnlyCollection<string> userIds, CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyDictionary<string, string>>(userIds.ToDictionary(id => id, _ => "Tendai Moyo"));
        public Task<IReadOnlyList<DirectoryUser>> ListActiveAsync(CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyList<DirectoryUser>>([]);
    }

    private sealed class Proofs : IProofStore
    {
        public Task<string> SaveAsync(Guid id, Stream content, string contentType, CancellationToken cancellationToken = default) => Task.FromResult($"proof/{id}");
        public Task<ProofFile?> OpenAsync(string reference, CancellationToken cancellationToken = default) => Task.FromResult<ProofFile?>(null);
    }

    private sealed class Uow : IUnitOfWork
    {
        public int Saves { get; private set; }
        public Task<int> SaveChangesAsync(CancellationToken cancellationToken = default) { Saves++; return Task.FromResult(1); }
    }

    private sealed class ConsentLog : Bassign.Application.Privacy.IConsentRegister
    {
        public List<(string UserId, string Purpose, bool Granted)> Entries { get; } = [];
        public void Record(string userId, string purpose, bool granted, string source, DateTimeOffset at, string version = Bassign.Application.Privacy.ConsentPurposes.CurrentNoticeVersion) =>
            Entries.Add((userId, purpose, granted));
        public Task<IReadOnlyList<Bassign.Application.Privacy.ConsentEntry>> ForUserAsync(string userId, CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyList<Bassign.Application.Privacy.ConsentEntry>>([]);
    }

    private sealed class Sut
    {
        public Bindings Bindings { get; } = new();
        public Log Log { get; } = new();
        public RecordingRunAs RunAs { get; } = new();
        public TaskRepo Tasks { get; } = new();
        public Tokens Tokens { get; } = new();
        public Uow Uow { get; } = new();
        public ConsentLog Consents { get; } = new();

        public HandleInboundWhatsAppHandler Handler => new(
            Bindings, Log, new Client(), Tokens, RunAs, Tasks, new Org(), new Users(), new Proofs(), Uow, Consents, TimeProvider.System);

        public WhatsAppBinding LinkedUser()
        {
            var b = WhatsAppBinding.For(UserId);
            b.Verify(Phone, DateTimeOffset.UtcNow);
            Bindings.Add(b);
            return b;
        }

        public TaskItem TaskFor(string assignee, bool needsPhoto = false)
        {
            var t = TaskItem.Create("Fix the gate", "mgr-1", DateTimeOffset.UtcNow, requiresProofPhoto: needsPhoto);
            t.AssignTo(assignee);
            Tasks.Add(t);
            return t;
        }

        public async Task<IReadOnlyList<WhatsAppOutbound>> Receive(string? text = null, string? replyId = null) =>
            await Handler.Handle(new HandleInboundWhatsAppCommand(
                new WhatsAppInbound($"wamid.{Guid.NewGuid():N}", Phone, DateTimeOffset.UtcNow, replyId is null ? "text" : "interactive",
                    text, replyId, null, null)), TestContext.Current.CancellationToken);
    }

    // ---------- tests ----------
    [Fact]
    public async Task Unknown_number_gets_enrollment_and_no_task_data()
    {
        var sut = new Sut();
        var task = sut.TaskFor(UserId);   // exists, but this phone isn't linked

        var replies = await sut.Receive(text: "TASKS");
        var replies2 = await sut.Receive(replyId: $"accept:{task.Id}");

        var reply = Assert.IsType<WaText>(Assert.Single(replies));
        Assert.Contains("isn't linked", reply.Body, StringComparison.Ordinal);
        Assert.DoesNotContain(task.Title, reply.Body, StringComparison.Ordinal);
        Assert.IsType<WaText>(Assert.Single(replies2));
        Assert.Equal(0, sut.Tasks.Reads);    // never even looked
        Assert.Empty(sut.RunAs.Calls);       // and never acted
    }

    [Fact]
    public async Task Accept_button_runs_AcceptTask_as_the_linked_user()
    {
        var sut = new Sut();
        sut.LinkedUser();
        var task = sut.TaskFor(UserId);

        var replies = await sut.Receive(replyId: $"{WhatsAppReplyIds.Accept}:{task.Id}");

        var call = Assert.Single(sut.RunAs.Calls);
        Assert.Equal(UserId, call.UserId);
        Assert.Equal(new AcceptTaskCommand(task.Id), call.Request);
        var reply = Assert.IsType<WaButtons>(Assert.Single(replies));
        Assert.StartsWith("Accepted", reply.Body, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Decline_button_asks_why_with_a_list_before_declining()
    {
        var sut = new Sut();
        sut.LinkedUser();
        var task = sut.TaskFor(UserId);

        var replies = await sut.Receive(replyId: $"{WhatsAppReplyIds.Decline}:{task.Id}");

        Assert.Empty(sut.RunAs.Calls);   // nothing declined yet
        var list = Assert.IsType<WaList>(Assert.Single(replies));
        Assert.Equal(DeclineReasons.All.Count, list.Rows.Count);
        Assert.Contains(list.Rows, r => r.Id == $"decline:{task.Id}:busy" && r.Title == "Too busy");
        Assert.All(list.Rows, r => Assert.True(r.Title.Length <= 24));   // WhatsApp's row-title limit
    }

    [Fact]
    public async Task Picking_a_reason_declines_with_its_label()
    {
        var sut = new Sut();
        sut.LinkedUser();
        var task = sut.TaskFor(UserId);

        await sut.Receive(replyId: WhatsAppReplyIds.DeclineWith(task.Id, "area"));

        Assert.Equal(new DeclineTaskCommand(task.Id, "Not my area"), Assert.Single(sut.RunAs.Calls).Request);
    }

    [Fact]
    public async Task Other_waits_for_a_typed_reason()
    {
        var sut = new Sut();
        var binding = sut.LinkedUser();
        var task = sut.TaskFor(UserId);

        var prompt = await sut.Receive(replyId: WhatsAppReplyIds.DeclineWith(task.Id, DeclineReasons.Other));
        Assert.Contains("reason", Assert.IsType<WaText>(Assert.Single(prompt)).Body, StringComparison.OrdinalIgnoreCase);
        Assert.Equal(task.Id, binding.AwaitingDeclineReasonForTaskId);

        await sut.Receive(text: "  On leave from Friday  ");

        Assert.Equal(new DeclineTaskCommand(task.Id, "On leave from Friday"), Assert.Single(sut.RunAs.Calls).Request);
        Assert.Null(binding.AwaitingDeclineReasonForTaskId);
    }

    [Fact]
    public async Task Typing_tasks_while_a_reason_is_awaited_cancels_the_decline()
    {
        var sut = new Sut();
        var binding = sut.LinkedUser();
        var task = sut.TaskFor(UserId);
        await sut.Receive(replyId: WhatsAppReplyIds.DeclineWith(task.Id, DeclineReasons.Other));

        await sut.Receive(text: "TASKS");

        Assert.Empty(sut.RunAs.Calls);
        Assert.Null(binding.AwaitingDeclineReasonForTaskId);
    }

    [Fact]
    public async Task Button_for_someone_elses_task_does_nothing()
    {
        var sut = new Sut();
        sut.LinkedUser();
        var task = sut.TaskFor("someone-else");

        var replies = await sut.Receive(replyId: $"{WhatsAppReplyIds.Accept}:{task.Id}");

        Assert.Empty(sut.RunAs.Calls);
        Assert.Contains("isn't assigned to you", Assert.IsType<WaText>(Assert.Single(replies)).Body, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Link_code_verifies_the_binding()
    {
        var sut = new Sut();
        var binding = WhatsAppBinding.For(UserId);
        binding.IssueLinkCode(LinkCodes.Hash(sut.Tokens, "123456"), DateTimeOffset.UtcNow);
        sut.Bindings.Add(binding);

        var replies = await sut.Receive(text: "link 123456");

        Assert.True(binding.Verified);
        Assert.Equal(Phone, binding.PhoneE164);
        Assert.Contains((UserId, Bassign.Application.Privacy.ConsentPurposes.WhatsApp, true), sut.Consents.Entries);   // consent register
        Assert.Contains("Linked", Assert.IsType<WaText>(Assert.Single(replies)).Body, StringComparison.Ordinal);
        Assert.True(sut.Uow.Saves > 0);
    }

    [Fact]
    public async Task Wrong_link_code_is_refused()
    {
        var sut = new Sut();
        var binding = WhatsAppBinding.For(UserId);
        binding.IssueLinkCode(LinkCodes.Hash(sut.Tokens, "123456"), DateTimeOffset.UtcNow);
        sut.Bindings.Add(binding);

        await sut.Receive(text: "LINK 654321");

        Assert.False(binding.Verified);
    }

    [Fact]
    public async Task Too_many_link_attempts_are_throttled()
    {
        var sut = new Sut();
        var binding = WhatsAppBinding.For(UserId);
        binding.IssueLinkCode(LinkCodes.Hash(sut.Tokens, "123456"), DateTimeOffset.UtcNow);
        sut.Bindings.Add(binding);
        sut.Log.LinkAttempts = 7;

        await sut.Receive(text: "LINK 123456");

        Assert.False(binding.Verified);
    }

    [Fact]
    public async Task Tasks_lists_open_tasks_as_a_list_message()
    {
        var sut = new Sut();
        sut.LinkedUser();
        sut.TaskFor(UserId);
        sut.TaskFor(UserId);

        var list = Assert.IsType<WaList>(Assert.Single(await sut.Receive(text: "tasks")));

        Assert.Equal(2, list.Rows.Count);
        Assert.All(list.Rows, r => Assert.StartsWith($"{WhatsAppReplyIds.View}:", r.Id, StringComparison.Ordinal));
    }

    [Theory]
    [InlineData("1")]
    [InlineData("accept")]
    [InlineData(" Accept ")]
    public async Task A_typed_number_or_word_picks_from_the_last_menu(string typed)
    {
        var sut = new Sut();
        sut.LinkedUser();
        var task = sut.TaskFor(UserId);
        sut.Log.Menu.AddRange([new WaButton($"accept:{task.Id}", "Accept"), new WaButton($"decline:{task.Id}", "Decline")]);

        await sut.Receive(text: typed);

        Assert.Equal(new AcceptTaskCommand(task.Id), Assert.Single(sut.RunAs.Calls).Request);
    }

    [Fact]
    public async Task A_number_outside_the_menu_does_nothing()
    {
        var sut = new Sut();
        sut.LinkedUser();
        var task = sut.TaskFor(UserId);
        sut.Log.Menu.Add(new WaButton($"accept:{task.Id}", "Accept"));

        await sut.Receive(text: "7");

        Assert.Empty(sut.RunAs.Calls);
    }
}
