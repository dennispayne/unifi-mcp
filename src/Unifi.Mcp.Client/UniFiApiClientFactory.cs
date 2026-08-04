using System.Collections.Concurrent;

namespace Unifi.Mcp.Client;

public sealed class UniFiApiClientFactory : IUniFiApiClientFactory
{
    private readonly Dictionary<string, UniFiAccessProfileOptions> _profiles;
    private readonly ConcurrentDictionary<string, IUniFiApiClient> _clients = new(StringComparer.OrdinalIgnoreCase);
    private readonly IUniFiAuthenticator _passwordAuthenticator;
    private readonly IUniFiAuthenticator _apiKeyAuthenticator;
    private readonly IUniFiTransportFactory _transportFactory;
    private readonly IUniFiSessionTokenCache _tokenCache;
    private readonly UniFiApiClientOptions _options;

    public UniFiApiClientFactory(
        UniFiApiClientOptions options,
        IUniFiAuthenticator? authenticator = null,
        IUniFiTransportFactory? transportFactory = null,
        IUniFiSessionTokenCache? tokenCache = null,
        Func<string, string?>? environmentVariableReader = null)
    {
        _options = options ?? throw new ArgumentNullException(nameof(options));
        _options.Validate(environmentVariableReader);

        _profiles = _options.Profiles.ToDictionary(profile => profile.Name, StringComparer.OrdinalIgnoreCase);
        _passwordAuthenticator = authenticator ?? new UniFiPasswordAuthenticator(environmentVariableReader: environmentVariableReader);
        _apiKeyAuthenticator = new UniFiApiKeyAuthenticator(environmentVariableReader: environmentVariableReader);
        _transportFactory = transportFactory ?? new DefaultUniFiTransportFactory();
        _tokenCache = tokenCache ?? new InMemoryUniFiSessionTokenCache();
    }

    public IReadOnlyCollection<string> ProfileNames => _profiles.Keys.ToArray();

    public IUniFiApiClient Create(string profileName)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(profileName);

        if (!_profiles.TryGetValue(profileName, out var profile))
        {
            throw new KeyNotFoundException($"No UniFi access profile named '{profileName}' is configured.");
        }

        return _clients.GetOrAdd(
            profile.Name,
            _ => new UniFiApiClient(
                profile,
                _transportFactory.Create(profile),
                string.IsNullOrWhiteSpace(profile.ApiKeyEnvironmentVariable) ? _passwordAuthenticator : _apiKeyAuthenticator,
                _tokenCache,
                _options.TokenRefreshSkew));
    }

    public void Dispose()
    {
        foreach (var client in _clients.Values)
        {
            client.Dispose();
        }

        if (_tokenCache is IDisposable disposableTokenCache)
        {
            disposableTokenCache.Dispose();
        }
    }
}
