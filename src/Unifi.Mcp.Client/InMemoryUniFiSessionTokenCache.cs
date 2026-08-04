using System.Collections.Concurrent;

namespace Unifi.Mcp.Client;

public sealed class InMemoryUniFiSessionTokenCache : IUniFiSessionTokenCache, IDisposable
{
    private readonly ConcurrentDictionary<string, UniFiSessionToken> _tokens = new(StringComparer.OrdinalIgnoreCase);
    private readonly ConcurrentDictionary<string, SemaphoreSlim> _locks = new(StringComparer.OrdinalIgnoreCase);
    private readonly TimeProvider _timeProvider;

    public InMemoryUniFiSessionTokenCache(TimeProvider? timeProvider = null)
    {
        _timeProvider = timeProvider ?? TimeProvider.System;
    }

    public async Task<UniFiSessionToken> GetOrCreateAsync(
        UniFiAccessProfileOptions profile,
        Func<CancellationToken, Task<UniFiSessionToken>> tokenFactory,
        TimeSpan refreshSkew,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(profile);
        ArgumentNullException.ThrowIfNull(tokenFactory);

        if (TryGetReusableToken(profile.Name, refreshSkew, out var token))
        {
            return token;
        }

        var profileLock = _locks.GetOrAdd(profile.Name, static _ => new SemaphoreSlim(1, 1));
        await profileLock.WaitAsync(cancellationToken).ConfigureAwait(false);

        try
        {
            if (TryGetReusableToken(profile.Name, refreshSkew, out token))
            {
                return token;
            }

            token = await tokenFactory(cancellationToken).ConfigureAwait(false);
            _tokens[profile.Name] = token;
            return token;
        }
        finally
        {
            profileLock.Release();
        }
    }

    public void Invalidate(string profileName)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(profileName);
        _tokens.TryRemove(profileName, out _);
    }

    public void Dispose()
    {
        foreach (var profileLock in _locks.Values)
        {
            profileLock.Dispose();
        }

        _locks.Clear();
        _tokens.Clear();
    }

    private bool TryGetReusableToken(string profileName, TimeSpan refreshSkew, out UniFiSessionToken token)
    {
        if (_tokens.TryGetValue(profileName, out token!)
            && !token.IsExpired(_timeProvider.GetUtcNow(), refreshSkew))
        {
            return true;
        }

        token = null!;
        return false;
    }
}
