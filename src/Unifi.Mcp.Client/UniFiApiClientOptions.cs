using System.Text.Json;

namespace Unifi.Mcp.Client;

public sealed class UniFiApiClientOptions
{
    public TimeSpan TokenRefreshSkew { get; init; } = TimeSpan.FromMinutes(2);

    public IReadOnlyList<UniFiAccessProfileOptions> Profiles { get; init; } = Array.Empty<UniFiAccessProfileOptions>();

    public void Validate(Func<string, string?>? environmentVariableReader = null)
    {
        if (TokenRefreshSkew < TimeSpan.Zero)
        {
            throw new InvalidOperationException("Token refresh skew must be zero or greater.");
        }

        if (Profiles.Count == 0)
        {
            throw new InvalidOperationException("At least one UniFi access profile must be configured.");
        }

        var seenNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var profile in Profiles)
        {
            profile.Validate(environmentVariableReader);
            if (!seenNames.Add(profile.Name))
            {
                throw new InvalidOperationException($"Duplicate UniFi access profile name '{profile.Name}'.");
            }
        }
    }
}

public static class UniFiApiClientOptionsLoader
{
    private static readonly JsonSerializerOptions SerializerOptions = new(JsonSerializerDefaults.Web)
    {
        PropertyNameCaseInsensitive = true,
        ReadCommentHandling = JsonCommentHandling.Skip,
        AllowTrailingCommas = true
    };

    public static UniFiApiClientOptions LoadFromFile(string path, Func<string, string?>? environmentVariableReader = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        using var stream = File.OpenRead(path);
        return Load(stream, environmentVariableReader);
    }

    public static UniFiApiClientOptions Load(Stream stream, Func<string, string?>? environmentVariableReader = null)
    {
        ArgumentNullException.ThrowIfNull(stream);

        var options = JsonSerializer.Deserialize<UniFiApiClientOptions>(stream, SerializerOptions)
            ?? throw new InvalidOperationException("UniFi API client configuration could not be deserialized.");

        options.Validate(environmentVariableReader);
        return options;
    }
}
