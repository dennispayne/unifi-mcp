using System.Text.Json;
using Unifi.Mcp.Client;

namespace UnifiMcp.Core;

public sealed class UnifiMcpConfiguration
{
    public TimeSpan TokenRefreshSkew { get; init; } = TimeSpan.FromMinutes(2);

    public UnifiMcpServerOptions Server { get; init; } = new();

    public IReadOnlyList<UniFiCredentialOptions> Credentials { get; init; } = Array.Empty<UniFiCredentialOptions>();

    public IReadOnlyList<UniFiScopeOptions> Scopes { get; init; } = Array.Empty<UniFiScopeOptions>();

    public void Validate(Func<string, string?>? environmentVariableReader = null)
    {
        Server.Validate();

        if (TokenRefreshSkew < TimeSpan.Zero)
        {
            throw new InvalidOperationException("TokenRefreshSkew must be zero or greater.");
        }

        if (Credentials.Count == 0)
        {
            throw new InvalidOperationException("At least one UniFi credential must be configured.");
        }

        if (Scopes.Count == 0)
        {
            throw new InvalidOperationException("At least one UniFi scope must be configured.");
        }

        var credentialLookup = new Dictionary<string, UniFiCredentialOptions>(StringComparer.OrdinalIgnoreCase);
        foreach (var credential in Credentials)
        {
            credential.Validate(environmentVariableReader);
            if (!credentialLookup.TryAdd(credential.Name, credential))
            {
                throw new InvalidOperationException($"Duplicate UniFi credential name '{credential.Name}'.");
            }
        }

        var scopeNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var scope in Scopes)
        {
            scope.Validate();
            if (!scopeNames.Add(scope.Name))
            {
                throw new InvalidOperationException($"Duplicate UniFi scope name '{scope.Name}'.");
            }

            if (!credentialLookup.ContainsKey(scope.Credential))
            {
                throw new InvalidOperationException($"Scope '{scope.Name}' references unknown credential '{scope.Credential}'.");
            }
        }
    }

    public UniFiApiClientOptions ToClientOptions(Func<string, string?>? environmentVariableReader = null)
    {
        Validate(environmentVariableReader);

        var credentialLookup = Credentials.ToDictionary(
            credential => credential.Name,
            credential => credential,
            StringComparer.OrdinalIgnoreCase);

        var profiles = Scopes
            .Select(scope => scope.ToAccessProfile(credentialLookup[scope.Credential]))
            .ToArray();

        var options = new UniFiApiClientOptions
        {
            TokenRefreshSkew = TokenRefreshSkew,
            Profiles = profiles
        };

        options.Validate(environmentVariableReader);
        return options;
    }
}

public sealed class UniFiCredentialOptions
{
    public string Name { get; init; } = string.Empty;

    public string? Username { get; init; }

    public string? UsernameEnvironmentVariable { get; init; }

    public string? Password { get; init; }

    public string? PasswordEnvironmentVariable { get; init; }

    public string? ApiKeyEnvironmentVariable { get; init; }

    public string ApiKeyHeaderName { get; init; } = "X-API-KEY";

    public string? ApiKeyValuePrefix { get; init; }

    public void Validate(Func<string, string?>? environmentVariableReader = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(Name);
        var hasApiKey = !string.IsNullOrWhiteSpace(ApiKeyEnvironmentVariable);
        var hasUsernamePassword =
            !string.IsNullOrWhiteSpace(Username) ||
            !string.IsNullOrWhiteSpace(UsernameEnvironmentVariable) ||
            !string.IsNullOrWhiteSpace(Password) ||
            !string.IsNullOrWhiteSpace(PasswordEnvironmentVariable);

        if (hasApiKey == hasUsernamePassword)
        {
            throw new InvalidOperationException($"Credential '{Name}' must configure either an API key environment variable or a username/password pair, but not both.");
        }

        if (hasApiKey)
        {
            if (string.IsNullOrWhiteSpace(ApiKeyHeaderName))
            {
                throw new InvalidOperationException($"Credential '{Name}' must configure an API key header name.");
            }

            _ = ResolveApiKey(environmentVariableReader);
            return;
        }

        _ = ResolveUsername(environmentVariableReader);
        _ = ResolvePassword(environmentVariableReader);
    }

    public string ResolveUsername(Func<string, string?>? environmentVariableReader = null)
    {
        var directValue = Username?.Trim();
        var environmentVariable = UsernameEnvironmentVariable?.Trim();

        if (!string.IsNullOrWhiteSpace(directValue) && !string.IsNullOrWhiteSpace(environmentVariable))
        {
            throw new InvalidOperationException($"Credential '{Name}' must configure either a direct username or a username environment variable, not both.");
        }

        if (!string.IsNullOrWhiteSpace(directValue))
        {
            return directValue;
        }

        return ReadRequiredEnvironmentValue(
            environmentVariable,
            environmentVariableReader,
            $"Credential '{Name}' is missing a username or username environment variable.");
    }

    public string ResolvePassword(Func<string, string?>? environmentVariableReader = null)
    {
        var directValue = Password;
        var environmentVariable = PasswordEnvironmentVariable?.Trim();

        if (!string.IsNullOrWhiteSpace(directValue) && !string.IsNullOrWhiteSpace(environmentVariable))
        {
            throw new InvalidOperationException($"Credential '{Name}' must configure either a direct password or a password environment variable, not both.");
        }

        if (!string.IsNullOrWhiteSpace(directValue))
        {
            return directValue;
        }

        return ReadRequiredEnvironmentValue(
            environmentVariable,
            environmentVariableReader,
            $"Credential '{Name}' is missing a password or password environment variable.");
    }

    public string ResolveApiKey(Func<string, string?>? environmentVariableReader = null)
    {
        var environmentVariable = ApiKeyEnvironmentVariable?.Trim();
        return ReadRequiredEnvironmentValue(
            environmentVariable,
            environmentVariableReader,
            $"Credential '{Name}' is missing an API key environment variable.");
    }

    private string ReadRequiredEnvironmentValue(
        string? environmentVariable,
        Func<string, string?>? environmentVariableReader,
        string missingConfigurationMessage)
    {
        if (string.IsNullOrWhiteSpace(environmentVariable))
        {
            throw new InvalidOperationException(missingConfigurationMessage);
        }

        var reader = environmentVariableReader ?? Environment.GetEnvironmentVariable;
        var value = reader(environmentVariable);
        if (string.IsNullOrWhiteSpace(value))
        {
            throw new InvalidOperationException($"Credential '{Name}' references environment variable '{environmentVariable}', but no value was found.");
        }

        return value;
    }
}

public sealed class UniFiScopeOptions
{
    public string Name { get; init; } = string.Empty;

    public Uri BaseAddress { get; init; } = null!;

    public UniFiServiceKind Service { get; init; }

    public string Credential { get; init; } = string.Empty;

    public string? LoginPath { get; init; } = "/api/auth/login";

    public string? ScopeDescription { get; init; }

    public TimeSpan SessionTtl { get; init; } = TimeSpan.FromMinutes(55);

    public string? PinnedServerCertificateSha256 { get; init; }

    public IReadOnlyList<string> AllowedRelativePathPrefixes { get; init; } = Array.Empty<string>();

    public bool AllowMutations { get; init; }

    public IReadOnlyList<string> AllowedHttpMethods { get; init; } = ["GET"];

    public bool AllowConnectorProxy { get; init; }

    public IReadOnlyList<string> ConnectorAllowedPathPrefixes { get; init; } = Array.Empty<string>();

    public void Validate()
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(Name);
        ArgumentNullException.ThrowIfNull(BaseAddress);
        ArgumentException.ThrowIfNullOrWhiteSpace(Credential);

        if (!BaseAddress.IsAbsoluteUri)
        {
            throw new InvalidOperationException($"Scope '{Name}' must use an absolute UniFi controller base address.");
        }

        if (!string.IsNullOrWhiteSpace(BaseAddress.UserInfo))
        {
            throw new InvalidOperationException($"Scope '{Name}' base address must not embed credentials.");
        }

        if (!string.Equals(BaseAddress.Scheme, Uri.UriSchemeHttps, StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException($"Scope '{Name}' must use HTTPS.");
        }

        if (SessionTtl <= TimeSpan.Zero)
        {
            throw new InvalidOperationException($"Scope '{Name}' must configure a positive session TTL.");
        }

        if (AllowedRelativePathPrefixes.Count == 0)
        {
            throw new InvalidOperationException($"Scope '{Name}' must configure at least one allowed relative path prefix.");
        }
    }

    public UniFiAccessProfileOptions ToAccessProfile(UniFiCredentialOptions credential)
    {
        ArgumentNullException.ThrowIfNull(credential);

        return new UniFiAccessProfileOptions
        {
            Name = Name,
            BaseAddress = BaseAddress,
            Service = Service,
            Username = credential.Username,
            UsernameEnvironmentVariable = credential.UsernameEnvironmentVariable,
            Password = credential.Password,
            PasswordEnvironmentVariable = credential.PasswordEnvironmentVariable,
            ApiKeyEnvironmentVariable = credential.ApiKeyEnvironmentVariable,
            ApiKeyHeaderName = credential.ApiKeyHeaderName,
            ApiKeyValuePrefix = credential.ApiKeyValuePrefix,
            LoginPath = LoginPath,
            ScopeDescription = ScopeDescription,
            SessionTtl = SessionTtl,
            PinnedServerCertificateSha256 = PinnedServerCertificateSha256,
            AllowedRelativePathPrefixes = AllowedRelativePathPrefixes,
            AllowMutations = AllowMutations,
            AllowedHttpMethods = AllowedHttpMethods,
            AllowConnectorProxy = AllowConnectorProxy,
            ConnectorAllowedPathPrefixes = ConnectorAllowedPathPrefixes
        };
    }
}

public static class UnifiMcpConfigurationLoader
{
    private static readonly JsonSerializerOptions SerializerOptions = new(JsonSerializerDefaults.Web)
    {
        PropertyNameCaseInsensitive = true,
        ReadCommentHandling = JsonCommentHandling.Skip,
        AllowTrailingCommas = true
    };

    public static UnifiMcpConfiguration LoadFromFile(string path, Func<string, string?>? environmentVariableReader = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        using var stream = File.OpenRead(path);
        return Load(stream, environmentVariableReader);
    }

    public static UnifiMcpConfiguration Load(Stream stream, Func<string, string?>? environmentVariableReader = null)
    {
        ArgumentNullException.ThrowIfNull(stream);

        var configuration = JsonSerializer.Deserialize<UnifiMcpConfiguration>(stream, SerializerOptions)
            ?? throw new InvalidOperationException("UniFi MCP configuration could not be deserialized.");

        configuration.Validate(environmentVariableReader);
        return configuration;
    }
}
