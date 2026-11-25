using Bassign.Application.Common.Email;
using Bassign.Application.Common.Security;
using Bassign.Application.Identity;
using Bassign.Domain.Common;
using Bassign.Infrastructure.Email;
using Mediator;
using Microsoft.Extensions.Options;

namespace Bassign.UnitTests.Hardening;

public class EmailProviderTests
{
    private sealed class Monitor<T>(T value) : IOptionsMonitor<T>
    {
        public T CurrentValue => value;
        public T Get(string? name) => value;
        public IDisposable? OnChange(Action<T, string?> listener) => null;
    }

    private sealed class Transport(string name) : IEmailTransport
    {
        public List<EmailMessage> Sent { get; } = [];
        public Exception? Fail { get; set; }
        public bool Handles(string provider) => provider == name;
        public Task SendAsync(EmailMessage message, CancellationToken cancellationToken = default)
        {
            if (Fail is not null) throw Fail;
            Sent.Add(message);
            return Task.CompletedTask;
        }
    }

    private static readonly EmailMessage Message = new("tendai@client.co.zw", "Tendai", "Hi", "<p>Hi</p>", "Hi");

    [Fact]
    public void Each_provider_lists_what_is_still_missing()
    {
        Assert.Equal(new[] { "SMTP server", "From address" }, new EmailOptions { Provider = "smtp" }.Missing());
        Assert.Equal(new[] { "Gmail address (From address)", "App password" }, new EmailOptions { Provider = "gmail" }.Missing());
        Assert.Equal(4, new EmailOptions { Provider = "m365" }.Missing().Count);
        Assert.Empty(new EmailOptions { Provider = "smtp", FromAddress = "tasks@client.co.zw", Smtp = new() { Host = "smtp.office365.com" } }.Missing());
        Assert.False(new EmailOptions().IsReady);   // default: off
    }

    [Fact]
    public void Gmail_is_an_smtp_preset_signed_in_as_the_from_address()
    {
        var smtp = new EmailOptions { Provider = "gmail", FromAddress = "minted@gmail.com", Smtp = new() { Password = "app-pass" } }.EffectiveSmtp();
        Assert.Equal(("smtp.gmail.com", 587, "starttls", "minted@gmail.com", "app-pass"), (smtp.Host, smtp.Port, smtp.Security, smtp.Username, smtp.Password));
    }

    [Fact]
    public void Microsoft_365_sends_as_the_configured_mailbox() =>
        Assert.Equal("tasks@client.co.zw", new EmailOptions { Provider = "m365", FromAddress = "ignored@x.com", Graph = new() { Sender = "tasks@client.co.zw" } }.EffectiveFromAddress);

    [Fact]
    public async Task The_router_uses_the_provider_configured_now()
    {
        var smtp = new Transport("smtp");
        var sendgrid = new Transport("sendgrid");
        var options = new EmailOptions { Provider = "sendgrid", ApiKey = "SG.x", FromAddress = "tasks@client.co.zw" };

        await new RoutingEmailSender([smtp, sendgrid], new Monitor<EmailOptions>(options)).SendAsync(Message, TestContext.Current.CancellationToken);

        Assert.Single(sendgrid.Sent);
        Assert.Empty(smtp.Sent);
    }

    [Theory]
    [InlineData("none")]
    [InlineData("smtp")]   // chosen but not filled in
    public async Task Off_or_incomplete_email_fails_without_retrying(string provider)
    {
        var router = new RoutingEmailSender([new Transport("smtp")], new Monitor<EmailOptions>(new EmailOptions { Provider = provider }));
        var ex = await Assert.ThrowsAsync<EmailSendException>(() => router.SendAsync(Message, TestContext.Current.CancellationToken));
        Assert.False(ex.Transient);
    }

    private sealed class Setup(EmailSetupStatus status) : IEmailSetup { public EmailSetupStatus Current() => status; }
    private sealed class Admin(bool isAdmin) : IIdentityAdmin
    {
        public Task<bool> EmailExistsAsync(string email, CancellationToken cancellationToken = default) => Task.FromResult(false);
        public Task<bool> IsInRoleAsync(string userId, string role, CancellationToken cancellationToken = default) => Task.FromResult(isAdmin);
        public Task<Result<string>> CreateUserAsync(string email, string displayName, string password, string role, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<IReadOnlyList<TeamMember>> ListMembersAsync(CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<Result<Unit>> SetActiveAsync(string userId, bool isActive, CancellationToken cancellationToken = default) => throw new NotSupportedException();
    }
    private sealed class Me : ICurrentUser { public string UserId => "adm-1"; }

    private sealed class Sender(Exception? fail = null) : IEmailSender
    {
        public Task SendAsync(EmailMessage message, CancellationToken cancellationToken = default) => fail is null ? Task.CompletedTask : throw fail;
    }

    [Fact]
    public async Task Test_email_explains_what_is_missing()
    {
        var handler = new SendTestEmailHandler(new Setup(new("smtp", true, false, ["SMTP server"], null)), new Sender(), new Admin(true), new Me());
        var result = await handler.Handle(new SendTestEmailCommand("me@client.co.zw"), TestContext.Current.CancellationToken);
        Assert.Contains("SMTP server", result.Error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Test_email_reports_the_providers_answer_in_plain_words()
    {
        var handler = new SendTestEmailHandler(new Setup(new("gmail", true, true, [], "minted@gmail.com")),
            new Sender(new EmailSendException("Gmail refused the sign-in. Use an app password.", transient: false)), new Admin(true), new Me());
        var result = await handler.Handle(new SendTestEmailCommand("me@client.co.zw"), TestContext.Current.CancellationToken);
        Assert.Equal("email.test_failed", result.Error.Code);
        Assert.Contains("app password", result.Error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Only_admins_can_send_a_test()
    {
        var handler = new SendTestEmailHandler(new Setup(new("gmail", true, true, [], null)), new Sender(), new Admin(false), new Me());
        Assert.Equal("email.forbidden", (await handler.Handle(new SendTestEmailCommand("me@client.co.zw"), TestContext.Current.CancellationToken)).Error.Code);
    }
}
