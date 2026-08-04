namespace Unifi.Mcp.Client;

public sealed class UniFiApiKeyAuthenticator : IUniFiAuthenticator
{
    private readonly TimeProvider _timeProvider;
    private readonly Func<string, string?>? _environmentVariableReader;

    public UniFiApiKeyAuthenticator(TimeProvider? timeProvider = null, Func<string, string?>? environmentVariableReader = null)
    {
        _timeProvider = timeProvider ?? TimeProvider.System;
        _environmentVariableReader = environmentVariableReader;
    }

    public Task<UniFiSessionToken> AuthenticateAsync(
        UniFiAccessProfileOptions profile,
        IUniFiTransport transport,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(profile);
        ArgumentNullException.ThrowIfNull(transport);

        var apiKey = profile.ResolveApiKey(_environmentVariableReader);
        var headerName = string.IsNullOrWhiteSpace(profile.ApiKeyHeaderName) ? "X-API-KEY" : profile.ApiKeyHeaderName.Trim();

        return Task.FromResult(new UniFiSessionToken
        {
            ExpiresAt = _timeProvider.GetUtcNow().Add(profile.SessionTtl),
            Headers = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            {
                [headerName] = apiKey.Reveal()
            }
        });
    }
}
