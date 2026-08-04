namespace Unifi.Mcp.Client;

public sealed class UniFiAccessProfileOptions
{
    public required string Name { get; init; }

    public required Uri BaseAddress { get; init; }

    public string? Username { get; init; }

    public string? UsernameEnvironmentVariable { get; init; }

    public string? Password { get; init; }

    public string? PasswordEnvironmentVariable { get; init; }

    public string? ApiKeyEnvironmentVariable { get; init; }

    public string ApiKeyHeaderName { get; init; } = "X-API-KEY";

    public string? LoginPath { get; init; } = "/api/auth/login";

    public string? ScopeDescription { get; init; }

    public TimeSpan SessionTtl { get; init; } = TimeSpan.FromMinutes(55);

    public IReadOnlyList<string> AllowedRelativePathPrefixes { get; init; } = Array.Empty<string>();

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

        if (hasApiKey)
        {
            if (string.IsNullOrWhiteSpace(ApiKeyHeaderName))
            {
                throw new InvalidOperationException($"Profile '{Name}' must configure an API key header name.");
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
}
