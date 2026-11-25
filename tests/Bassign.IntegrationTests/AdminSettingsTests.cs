using Bassign.Application.Settings;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace Bassign.IntegrationTests;

[Collection(ApiCollection.Name)]
public class AdminSettingsTests(BassignApiFactory api)
{
    private async Task<string> AdminIdAsync() => await api.UserIdAsync("admin@bassign.local");

    [Fact]
    public async Task A_saved_value_takes_effect_immediately()
    {
        var result = await api.SendAsync(new SaveAdminSettingsCommand(new Dictionary<string, SettingChange>
        {
            ["Email:FromName"] = new("Minted Moments Tasks")
        }), await AdminIdAsync());

        Assert.True(result.IsSuccess, result.IsSuccess ? null : result.Error.Message);
        Assert.Equal("Minted Moments Tasks", api.Services.GetRequiredService<IConfiguration>()["Email:FromName"]);

        var state = Assert.Single(await api.SendAsync(new GetAdminSettingsQuery("Email")), s => s.Key == "Email:FromName");
        Assert.Equal(SettingSource.Admin, state.Source);
    }

    [Fact]
    public async Task A_secret_is_encrypted_at_rest_and_never_returned()
    {
        const string secret = "smtp-password-for-integration-test";
        var result = await api.SendAsync(new SaveAdminSettingsCommand(new Dictionary<string, SettingChange>
        {
            ["Email:Smtp:Password"] = new(secret)
        }), await AdminIdAsync());
        Assert.True(result.IsSuccess, result.IsSuccess ? null : result.Error.Message);

        var stored = await api.DbAsync(db => db.AppSettings.AsNoTracking().SingleAsync(s => s.Key == "Email:Smtp:Password", Api.Ct));
        Assert.True(stored.IsSecret);
        Assert.NotEqual(secret, stored.Value);
        Assert.DoesNotContain(secret, stored.Value!, StringComparison.Ordinal);

        // The app can use it…
        Assert.Equal(secret, api.Services.GetRequiredService<IConfiguration>()["Email:Smtp:Password"]);
        // …but the Settings screen only learns that it is set.
        var state = Assert.Single(await api.SendAsync(new GetAdminSettingsQuery("Email")), s => s.Key == "Email:Smtp:Password");
        Assert.Null(state.Value);
        Assert.True(state.HasValue);
    }

    [Fact]
    public async Task A_value_set_on_the_server_is_locked()
    {
        // The test host sets Email__Provider as an environment variable (like a managed install would).
        var state = Assert.Single(await api.SendAsync(new GetAdminSettingsQuery("Email")), s => s.Key == "Email:Provider");
        Assert.True(state.Locked);

        var result = await api.SendAsync(new SaveAdminSettingsCommand(new Dictionary<string, SettingChange>
        {
            ["Email:Provider"] = new("sendgrid")
        }), await AdminIdAsync());
        Assert.False(result.IsSuccess);
        Assert.Equal("pickup", api.Services.GetRequiredService<IConfiguration>()["Email:Provider"]);
    }

    [Fact]
    public async Task Clearing_a_value_falls_back_to_the_default()
    {
        var admin = await AdminIdAsync();
        await api.SendAsync(new SaveAdminSettingsCommand(new Dictionary<string, SettingChange> { ["Email:Smtp:Host"] = new("smtp.office365.com") }), admin);
        await api.SendAsync(new SaveAdminSettingsCommand(new Dictionary<string, SettingChange> { ["Email:Smtp:Host"] = new(null, Clear: true) }), admin);

        Assert.Null(api.Services.GetRequiredService<IConfiguration>()["Email:Smtp:Host"]);
        Assert.False(await api.DbAsync(db => db.AppSettings.AnyAsync(s => s.Key == "Email:Smtp:Host", Api.Ct)));
    }
}

[Collection(ApiCollection.Name)]
public class EmailSettingsTests(BassignApiFactory api)
{
    [Fact]
    public async Task A_test_email_is_delivered_with_the_current_settings()
    {
        var mailDir = api.Services.GetRequiredService<IConfiguration>()["Email:PickupDirectory"]!;
        var before = Directory.Exists(mailDir) ? Directory.GetFiles(mailDir, "*.eml").Length : 0;

        var result = await api.SendAsync(new Bassign.Application.Common.Email.SendTestEmailCommand("admin@bassign.local"),
            await api.UserIdAsync("admin@bassign.local"));

        Assert.True(result.IsSuccess, result.IsSuccess ? null : result.Error.Message);
        Assert.Equal(before + 1, Directory.GetFiles(mailDir, "*.eml").Length);
    }
}

[Collection(ApiCollection.Name)]
public class SecuritySettingsTests(BassignApiFactory api)
{
    [Fact]
    public async Task Security_settings_and_retention_reach_the_privacy_notice()
    {
        var admin = await api.UserIdAsync("admin@bassign.local");
        try
        {
            var saved = await api.SendAsync(new Bassign.Application.Organizations.Security.SaveSecuritySettingsCommand(
                new(3, ["bassign.local"], "Data Protection Officer", "dpo@bassign.local", null)), admin);
            Assert.True(saved.IsSuccess, saved.IsSuccess ? null : saved.Error.Message);
            var retention = await api.SendAsync(new SaveAdminSettingsCommand(new Dictionary<string, SettingChange>
            {
                ["Retention:WhatsAppMessageDays"] = new("60")
            }), admin);
            Assert.True(retention.IsSuccess, retention.IsSuccess ? null : retention.Error.Message);

            var notice = await api.SendAsync(new Bassign.Application.Organizations.Security.GetPrivacyNoticeQuery());
            Assert.Equal("dpo@bassign.local", notice.ContactEmail);
            Assert.Equal(60, notice.WhatsAppMessageDays);   // live: no restart

            var refused = await api.SendAsync(new Bassign.Application.Teams.InviteUser.InviteUserCommand("someone@gmail.com", "Staff"), admin);
            Assert.Equal("team.domain_not_allowed", refused.Error.Code);
        }
        finally
        {
            await api.SendAsync(new Bassign.Application.Organizations.Security.SaveSecuritySettingsCommand(new(7, [], null, null, null)), admin);
            await api.SendAsync(new SaveAdminSettingsCommand(new Dictionary<string, SettingChange>
            {
                ["Retention:WhatsAppMessageDays"] = new(null, Clear: true)
            }), admin);
        }
    }

    [Fact]
    public async Task Retention_outside_the_allowed_range_is_refused()
    {
        var result = await api.SendAsync(new SaveAdminSettingsCommand(new Dictionary<string, SettingChange>
        {
            ["Retention:AuditDays"] = new("30")
        }), await api.UserIdAsync("admin@bassign.local"));
        Assert.False(result.IsSuccess);
    }
}
