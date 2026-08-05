using System.Text.Json.Serialization;

namespace Unifi.Mcp.Client;

[JsonConverter(typeof(JsonStringEnumConverter<UniFiServiceKind>))]
public enum UniFiServiceKind
{
    Generic,
    SiteManager,
    Network,
    Protect,
    Access,
    Mobility
}

public sealed class UniFiAccessProfileOptions
{
    private static readonly HashSet<string> SupportedHttpMethods = new(StringComparer.OrdinalIgnoreCase)
    {
        "GET",
        "POST",
        "PUT",
        "PATCH",
        "DELETE"
    };

    public required string Name { get; init; }

    public required Uri BaseAddress { get; init; }

    public UniFiServiceKind Service { get; init; }

    public string? Username { get; init; }

    public string? UsernameEnvironmentVariable { get; init; }

    public string? Password { get; init; }

    public string? PasswordEnvironmentVariable { get; init; }

    public string? ApiKeyEnvironmentVariable { get; init; }

    public string ApiKeyHeaderName { get; init; } = "X-API-KEY";

    public string? ApiKeyValuePrefix { get; init; }

    public string? LoginPath { get; init; } = "/api/auth/login";

    public string? ScopeDescription { get; init; }

    public TimeSpan SessionTtl { get; init; } = TimeSpan.FromMinutes(55);

    public string? PinnedServerCertificateSha256 { get; init; }

    public IReadOnlyList<string> AllowedRelativePathPrefixes { get; init; } = Array.Empty<string>();

    public bool AllowMutations { get; init; }

    public IReadOnlyList<string> AllowedHttpMethods { get; init; } = ["GET"];

    public bool AllowConnectorProxy { get; init; }

    public IReadOnlyList<string> ConnectorAllowedPathPrefixes { get; init; } = Array.Empty<string>();

    internal string ResolveUsername(Func<string, string?>? environmentVariableReader = null)
    {
        var directUsername = Username?.Trim();
        var envVarName = UsernameEnvironmentVariable?.Trim();

        if (!string.IsNullOrWhiteSpace(directUsername) && !string.IsNullOrWhiteSpace(envVarName))
        {
            throw new InvalidOperationException($"Profile '{Name}' must configure either a direct username or a username environment variable, not both.");
        }

        if (!string.IsNullOrWhiteSpace(directUsername))
        {
            return directUsername;
        }

        if (string.IsNullOrWhiteSpace(envVarName))
        {
            throw new InvalidOperationException($"Profile '{Name}' is missing a username or username environment variable.");
        }

        var readEnvironmentVariable = environmentVariableReader ?? Environment.GetEnvironmentVariable;
        var value = readEnvironmentVariable(envVarName);
        if (string.IsNullOrWhiteSpace(value))
        {
            throw new InvalidOperationException($"Profile '{Name}' references environment variable '{envVarName}' for the username, but no value was found.");
        }

        return value;
    }

    internal SecretValue ResolvePassword(Func<string, string?>? environmentVariableReader = null)
    {
        var directPassword = Password;
        var envVarName = PasswordEnvironmentVariable?.Trim();

        if (!string.IsNullOrWhiteSpace(directPassword) && !string.IsNullOrWhiteSpace(envVarName))
        {
            throw new InvalidOperationException($"Profile '{Name}' must configure either a direct password or a password environment variable, not both.");
        }

        if (!string.IsNullOrWhiteSpace(directPassword))
        {
            return new SecretValue(directPassword);
        }

        if (string.IsNullOrWhiteSpace(envVarName))
        {
            throw new InvalidOperationException($"Profile '{Name}' is missing a password or password environment variable.");
        }

        var readEnvironmentVariable = environmentVariableReader ?? Environment.GetEnvironmentVariable;
        var value = readEnvironmentVariable(envVarName);

        if (string.IsNullOrWhiteSpace(value))
        {
            throw new InvalidOperationException($"Profile '{Name}' references environment variable '{envVarName}', but no value was found.");
        }

        return new SecretValue(value);
    }

    internal IReadOnlyList<string> GetNormalizedAllowedPathPrefixes()
    {
        return AllowedRelativePathPrefixes
            .Where(static prefix => !string.IsNullOrWhiteSpace(prefix))
            .Select(static prefix => NormalizePathPrefix(prefix))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();
    }

    internal IReadOnlySet<string> GetNormalizedAllowedHttpMethods() =>
        AllowedHttpMethods
            .Where(static method => !string.IsNullOrWhiteSpace(method))
            .Select(static method => method.Trim().ToUpperInvariant())
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

    internal IReadOnlyList<string> GetNormalizedConnectorAllowedPathPrefixes() =>
        ConnectorAllowedPathPrefixes
            .Where(static prefix => !string.IsNullOrWhiteSpace(prefix))
            .Select(static prefix => NormalizePathPrefix(prefix))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();

    public void Validate(Func<string, string?>? environmentVariableReader = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(Name);
        ArgumentNullException.ThrowIfNull(BaseAddress);
        var hasApiKey = !string.IsNullOrWhiteSpace(ApiKeyEnvironmentVariable);
        var hasUsernamePassword =
            !string.IsNullOrWhiteSpace(Username) ||
            !string.IsNullOrWhiteSpace(UsernameEnvironmentVariable) ||
            !string.IsNullOrWhiteSpace(Password) ||
            !string.IsNullOrWhiteSpace(PasswordEnvironmentVariable);

        if (hasApiKey == hasUsernamePassword)
        {
            throw new InvalidOperationException($"Profile '{Name}' must configure either an API key environment variable or a username/password pair, not both.");
        }

        if (!BaseAddress.IsAbsoluteUri)
        {
            throw new InvalidOperationException($"Profile '{Name}' must use an absolute UniFi controller base address.");
        }

        if (!string.IsNullOrWhiteSpace(BaseAddress.UserInfo))
        {
            throw new InvalidOperationException($"Profile '{Name}' base address must not embed credentials.");
        }

        if (!string.Equals(BaseAddress.Scheme, Uri.UriSchemeHttps, StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException($"Profile '{Name}' must use HTTPS.");
        }

        if (SessionTtl <= TimeSpan.Zero)
        {
            throw new InvalidOperationException($"Profile '{Name}' must configure a positive session TTL.");
        }

        ValidateCertificatePin();

        if (hasApiKey)
        {
            if (string.IsNullOrWhiteSpace(ApiKeyHeaderName))
            {
                throw new InvalidOperationException($"Profile '{Name}' must configure an API key header name.");
            }

            if (ApiKeyValuePrefix is not null &&
                (ApiKeyValuePrefix.Trim().Length == 0 ||
                 ApiKeyValuePrefix.Trim().Any(static character => !char.IsAsciiLetterOrDigit(character) && character is not '-')))
            {
                throw new InvalidOperationException(
                    $"Profile '{Name}' must configure an alphanumeric API key value prefix such as 'Bearer'.");
            }

            _ = ResolveApiKey(environmentVariableReader);
        }
        else
        {
            if (string.IsNullOrWhiteSpace(LoginPath))
            {
                throw new InvalidOperationException($"Profile '{Name}' must configure a login path.");
            }

            _ = ResolveUsername(environmentVariableReader);
            _ = ResolvePassword(environmentVariableReader);
        }

        if (GetNormalizedAllowedPathPrefixes().Count == 0)
        {
            throw new InvalidOperationException($"Profile '{Name}' must configure at least one allowed relative path prefix.");
        }

        var allowedMethods = GetNormalizedAllowedHttpMethods();
        if (allowedMethods.Count == 0 || allowedMethods.Any(method => !SupportedHttpMethods.Contains(method)))
        {
            throw new InvalidOperationException($"Profile '{Name}' contains an unsupported HTTP method.");
        }

        if (!allowedMethods.Contains("GET"))
        {
            throw new InvalidOperationException($"Profile '{Name}' must allow GET.");
        }

        if (!AllowMutations && allowedMethods.Any(static method => !string.Equals(method, "GET", StringComparison.OrdinalIgnoreCase)))
        {
            throw new InvalidOperationException($"Profile '{Name}' must enable mutations before allowing non-GET methods.");
        }

        if (AllowConnectorProxy && Service != UniFiServiceKind.SiteManager)
        {
            throw new InvalidOperationException($"Profile '{Name}' can enable connector proxy only for Site Manager.");
        }

        if (AllowConnectorProxy && GetNormalizedConnectorAllowedPathPrefixes().Count == 0)
        {
            throw new InvalidOperationException(
                $"Profile '{Name}' must configure connector path prefixes when connector proxy is enabled.");
        }
    }

    internal SecretValue ResolveApiKey(Func<string, string?>? environmentVariableReader = null)
    {
        var envVarName = ApiKeyEnvironmentVariable?.Trim();
        if (string.IsNullOrWhiteSpace(envVarName))
        {
            throw new InvalidOperationException($"Profile '{Name}' is missing an API key environment variable.");
        }

        var readEnvironmentVariable = environmentVariableReader ?? Environment.GetEnvironmentVariable;
        var value = readEnvironmentVariable(envVarName);

        if (string.IsNullOrWhiteSpace(value))
        {
            throw new InvalidOperationException($"Profile '{Name}' references environment variable '{envVarName}', but no value was found.");
        }

        return new SecretValue(value);
    }

    private static string NormalizePathPrefix(string prefix)
    {
        var normalized = prefix.Trim();
        return normalized.StartsWith('/') ? normalized : "/" + normalized;
    }

    private void ValidateCertificatePin()
    {
        if (string.IsNullOrWhiteSpace(PinnedServerCertificateSha256))
        {
            return;
        }

        var normalized = PinnedServerCertificateSha256.Replace(":", string.Empty, StringComparison.Ordinal).Trim();
        if (normalized.Length != 64 || normalized.Any(static character => !Uri.IsHexDigit(character)))
        {
            throw new InvalidOperationException($"Profile '{Name}' must configure a 64-character SHA-256 certificate pin.");
        }
    }
}
