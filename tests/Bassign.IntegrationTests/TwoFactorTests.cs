using System.Net.Http.Json;
using System.Security.Cryptography;
using Bassign.Application.Identity;
using Bassign.Infrastructure.Identity;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Bassign.IntegrationTests;

/// <summary>Two-factor against the real Identity store and SQL: secrets encrypted, codes hashed, real TOTP codes.</summary>
[Collection(ApiCollection.Name)]
public class TwoFactorTests(BassignApiFactory api)
{
    [Fact]
    public async Task A_real_authenticator_code_turns_it_on_and_nothing_secret_is_stored_in_plain_text()
    {
        var userId = await api.UserIdAsync("tendai@bassign.local");
        await TurnOffAsync(userId);   // start clean, whatever an earlier test left
        await using var scope = api.Services.CreateAsyncScope();
        var twoFactor = scope.ServiceProvider.GetRequiredService<ITwoFactor>();
        var users = scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();
        var db = scope.ServiceProvider.GetRequiredService<Bassign.Infrastructure.Persistence.AppDbContext>();
        Task<string?> Stored(string name) => db.UserTokens.AsNoTracking()
            .Where(t => t.UserId == userId && t.LoginProvider == "[Bassign]" && t.Name == name).Select(t => t.Value).SingleOrDefaultAsync(Api.Ct);

        try
        {
            var setup = await twoFactor.BeginSetupAsync(userId, "Integration tests", Api.Ct);
            Assert.True(setup.IsSuccess, setup.IsSuccess ? null : setup.Error.Message);
            var key = setup.Value!.SharedKey.Replace(" ", "", StringComparison.Ordinal).ToUpperInvariant();
            Assert.DoesNotContain(key, await Stored("AuthenticatorKey.v1") ?? "", StringComparison.OrdinalIgnoreCase);   // encrypted

            Assert.Equal("2fa.bad_code", (await twoFactor.ConfirmSetupAsync(userId, "000000", Api.Ct)).Error.Code);
            var codes = await twoFactor.ConfirmSetupAsync(userId, Totp(key, DateTimeOffset.UtcNow), Api.Ct);
            Assert.True(codes.IsSuccess, codes.IsSuccess ? null : codes.Error.Message);
            Assert.Equal(10, codes.Value!.Count);
            Assert.True(await twoFactor.IsEnabledAsync(userId, Api.Ct));

            var storedCodes = await Stored("RecoveryCodes.v1") ?? "";
            Assert.All(codes.Value, c => Assert.DoesNotContain(c.Replace("-", "", StringComparison.Ordinal), storedCodes, StringComparison.OrdinalIgnoreCase));   // hashed

            var user = (await users.FindByIdAsync(userId))!;
            Assert.True((await users.RedeemTwoFactorRecoveryCodeAsync(user, codes.Value[0].ToLowerInvariant())).Succeeded);
            Assert.False((await users.RedeemTwoFactorRecoveryCodeAsync(user, codes.Value[0])).Succeeded);   // one use
            Assert.Equal(9, await twoFactor.RecoveryCodesLeftAsync(userId, Api.Ct));
        }
        finally
        {
            await TurnOffAsync(userId);
        }

        Assert.Null(await MethodNowAsync(userId));   // off
        Assert.True(string.IsNullOrEmpty(await Stored("RecoveryCodes.v1")));   // old codes gone
    }

    [Fact]
    public async Task An_emailed_code_works_once_expires_and_is_never_stored()
    {
        var userId = await api.UserIdAsync("tendai@bassign.local");
        await TurnOffAsync(userId);   // start clean, whatever an earlier test left
        var mailDir = api.Services.GetRequiredService<Microsoft.Extensions.Configuration.IConfiguration>()["Email:PickupDirectory"]!;
        await using var scope = api.Services.CreateAsyncScope();
        var twoFactor = scope.ServiceProvider.GetRequiredService<ITwoFactor>();
        var db = scope.ServiceProvider.GetRequiredService<Bassign.Infrastructure.Persistence.AppDbContext>();

        string NewestCode()
        {
            var file = new DirectoryInfo(mailDir).GetFiles("*.eml").OrderByDescending(f => f.LastWriteTimeUtc).First();
            using var message = MimeKit.MimeMessage.Load(file.FullName);
            return string.Concat((message.Subject ?? "").TakeWhile(c => char.IsDigit(c) || c == ' ').Where(char.IsDigit));
        }

        try
        {
            Assert.True(await twoFactor.EmailCodesAvailableAsync(userId, Api.Ct));
            Assert.False(await twoFactor.EmailCodesAvailableAsync(await api.UserIdAsync("admin@bassign.local"), Api.Ct));   // never for Admins

            Assert.True((await twoFactor.SendEmailCodeAsync(userId, Api.Ct)).IsSuccess);
            var code = NewestCode();
            Assert.Equal(6, code.Length);
            var stored = await db.UserTokens.AsNoTracking().Where(t => t.UserId == userId && t.Name == "EmailCode.v1").Select(t => t.Value).SingleAsync(Api.Ct);
            Assert.DoesNotContain(code, stored!.Split('|')[0], StringComparison.Ordinal);   // hashed

            Assert.Equal("2fa.email_cooldown", (await twoFactor.SendEmailCodeAsync(userId, Api.Ct)).Error.Code);   // one a minute

            var wrong = code == "000000" ? "000001" : "000000";
            Assert.Equal("2fa.bad_code", (await twoFactor.ConfirmEmailSetupAsync(userId, wrong, Api.Ct)).Error.Code);
            var codes = await twoFactor.ConfirmEmailSetupAsync(userId, code, Api.Ct);
            Assert.True(codes.IsSuccess, codes.IsSuccess ? null : codes.Error.Message);
            Assert.Equal(TwoFactorMethods.Email, await twoFactor.MethodAsync(userId, Api.Ct));
            Assert.False(await twoFactor.VerifyEmailCodeAsync(userId, code, Api.Ct));   // one use

            // Sign-in later: a fresh code, which stops working after 10 minutes.
            api.Clock.Advance(TimeSpan.FromMinutes(2));
            Assert.True((await twoFactor.SendEmailCodeAsync(userId, Api.Ct)).IsSuccess);
            var second = NewestCode();
            api.Clock.Advance(TimeSpan.FromMinutes(11));
            Assert.False(await twoFactor.VerifyEmailCodeAsync(userId, second, Api.Ct));
        }
        finally
        {
            api.Clock.Reset();
            await TurnOffAsync(userId);
        }
        Assert.Null(await MethodNowAsync(userId));
    }

    /// <summary>Clean-up in its own scope: the test's scope may hold a copy of the user that the API has since
    /// changed (failed-attempt counter), and Identity refuses to save over a newer version.</summary>
    private async Task TurnOffAsync(string userId)
    {
        await using var scope = api.Services.CreateAsyncScope();
        await scope.ServiceProvider.GetRequiredService<ITwoFactor>().DisableAsync(userId, Api.Ct);
    }

    /// <summary>The method as stored now (a fresh scope, so no cached copy of the user): null = two-factor off.</summary>
    private async Task<string?> MethodNowAsync(string userId)
    {
        await using var scope = api.Services.CreateAsyncScope();
        return await scope.ServiceProvider.GetRequiredService<ITwoFactor>().MethodAsync(userId, Api.Ct);
    }

    private sealed record ChallengeBody(string Ticket, string Method);
    private sealed record TokenBody(string AccessToken, string RefreshToken);
    private sealed record ProblemBody(string? Type, string? Title);

    [Fact]
    public async Task The_phone_api_asks_for_a_code_and_never_accepts_a_password_alone()
    {
        var userId = await api.UserIdAsync("tendai@bassign.local");
        await TurnOffAsync(userId);   // start clean, whatever an earlier test left
        var http = api.CreateClient();
        await using var scope = api.Services.CreateAsyncScope();
        var twoFactor = scope.ServiceProvider.GetRequiredService<ITwoFactor>();

        try
        {
            var setup = await twoFactor.BeginSetupAsync(userId, "Integration tests", Api.Ct);
            var key = setup.Value!.SharedKey.Replace(" ", "", StringComparison.Ordinal).ToUpperInvariant();
            Assert.True((await twoFactor.ConfirmSetupAsync(userId, Totp(key, DateTimeOffset.UtcNow), Api.Ct)).IsSuccess);

            // Password right → 202 and a ticket, no tokens.
            var login = await http.PostAsJsonAsync("/auth/login", new { email = "tendai@bassign.local", password = "Staff!2345" }, Api.Ct);
            Assert.Equal(System.Net.HttpStatusCode.Accepted, login.StatusCode);
            var challenge = (await login.Content.ReadFromJsonAsync<ChallengeBody>(Api.Ct))!;
            Assert.Equal(TwoFactorMethods.App, challenge.Method);
            Assert.DoesNotContain("accessToken", await login.Content.ReadAsStringAsync(Api.Ct), StringComparison.OrdinalIgnoreCase);

            // Wrong code → refused with a reason; a made-up ticket → "sign in again".
            var wrong = await http.PostAsJsonAsync("/auth/two-factor", new { ticket = challenge.Ticket, code = "000000", recovery = false }, Api.Ct);
            Assert.Equal(System.Net.HttpStatusCode.Unauthorized, wrong.StatusCode);
            Assert.Equal("auth.bad_code", (await wrong.Content.ReadFromJsonAsync<ProblemBody>(Api.Ct))!.Type);
            var forged = await http.PostAsJsonAsync("/auth/two-factor", new { ticket = "not-a-ticket", code = "123456", recovery = false }, Api.Ct);
            Assert.Equal("auth.ticket_expired", (await forged.Content.ReadFromJsonAsync<ProblemBody>(Api.Ct))!.Type);

            // Right code → tokens.
            var ok = await http.PostAsJsonAsync("/auth/two-factor", new { ticket = challenge.Ticket, code = Totp(key, DateTimeOffset.UtcNow), recovery = false }, Api.Ct);
            Assert.Equal(System.Net.HttpStatusCode.OK, ok.StatusCode);
            Assert.False(string.IsNullOrEmpty((await ok.Content.ReadFromJsonAsync<TokenBody>(Api.Ct))!.AccessToken));
        }
        finally
        {
            await TurnOffAsync(userId);
        }

        // An Admin (two-factor required, not set up) is sent to the website, not let in.
        var admin = await http.PostAsJsonAsync("/auth/login", new { email = "admin@bassign.local", password = "Admin!2345" }, Api.Ct);
        Assert.Equal(System.Net.HttpStatusCode.Unauthorized, admin.StatusCode);
        Assert.Equal("auth.two_factor_setup", (await admin.Content.ReadFromJsonAsync<ProblemBody>(Api.Ct))!.Type);
    }

    /// <summary>The browser half of passkeys can't run here; this checks the table exists and the service works on it.</summary>
    [Fact]
    public async Task The_passkey_store_is_in_place()
    {
        var userId = await api.UserIdAsync("tendai@bassign.local");
        await using var scope = api.Services.CreateAsyncScope();
        var passkeys = scope.ServiceProvider.GetRequiredService<IPasskeys>();

        Assert.Equal(0, await api.DbAsync(db => db.UserPasskeys.CountAsync(p => p.UserId == userId, Api.Ct)));
        Assert.Empty(await passkeys.ListAsync(userId, Api.Ct));
        Assert.Equal("passkey.not_found", (await passkeys.RemoveAsync(userId, "AAAA", Api.Ct)).Error.Code);
        await passkeys.RemoveAllAsync(userId, Api.Ct);   // nothing to remove: no error
    }

    /// <summary>RFC 6238 (what authenticator apps compute): HMAC-SHA1 over 30-second steps, 6 digits.</summary>
    private static string Totp(string base32Key, DateTimeOffset at)
    {
        var key = Base32(base32Key);
        var counter = BitConverter.GetBytes(at.ToUnixTimeSeconds() / 30);
        if (BitConverter.IsLittleEndian) Array.Reverse(counter);
        var hash = HMACSHA1.HashData(key, counter);
        var offset = hash[^1] & 0x0F;
        var binary = ((hash[offset] & 0x7F) << 24) | (hash[offset + 1] << 16) | (hash[offset + 2] << 8) | hash[offset + 3];
        return (binary % 1_000_000).ToString("D6", System.Globalization.CultureInfo.InvariantCulture);
    }

    private static byte[] Base32(string text)
    {
        const string alphabet = "ABCDEFGHIJKLMNOPQRSTUVWXYZ234567";
        var bytes = new List<byte>();
        int buffer = 0, bits = 0;
        foreach (var c in text.TrimEnd('='))
        {
            buffer = (buffer << 5) | alphabet.IndexOf(c, StringComparison.Ordinal);
            bits += 5;
            if (bits >= 8) { bytes.Add((byte)(buffer >> (bits - 8))); bits -= 8; }
        }
        return [.. bytes];
    }
}
