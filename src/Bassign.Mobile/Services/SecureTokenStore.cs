using System.Text.Json;
using Bassign.Field.Core.Api;
using Bassign.Field.Core.Auth;
using Microsoft.Maui.Storage;

namespace Bassign.Mobile.Services;

/// <summary>Access + refresh tokens in the platform keystore (Android Keystore-backed SecureStorage).
/// Cached in memory after the first read; the device holds no API keys.</summary>
public sealed class SecureTokenStore : ITokenStore
{
    private const string Key = "bassign.tokens";
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    private AuthTokens? _cache;
    private bool _loaded;

    public async Task<AuthTokens?> GetAsync()
    {
        if (_loaded) return _cache;
        try
        {
            var json = await SecureStorage.Default.GetAsync(Key);
            _cache = json is null ? null : JsonSerializer.Deserialize<AuthTokens>(json, Json);
        }
        catch (Exception)
        {
            // Keystore entries can become unreadable after a reinstall/restore: start signed out.
            _cache = null;
            SecureStorage.Default.Remove(Key);
        }
        _loaded = true;
        return _cache;
    }

    public async Task SaveAsync(AuthTokens tokens)
    {
        _cache = tokens;
        _loaded = true;
        await SecureStorage.Default.SetAsync(Key, JsonSerializer.Serialize(tokens, Json));
    }

    public Task ClearAsync()
    {
        _cache = null;
        _loaded = true;
        SecureStorage.Default.Remove(Key);
        return Task.CompletedTask;
    }
}
