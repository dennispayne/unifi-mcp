namespace Unifi.Mcp.Client;

internal static class UniFiPathScopeGuard
{
    public static string EnsureAllowed(UniFiAccessProfileOptions profile, string relativePath)
    {
        ArgumentNullException.ThrowIfNull(profile);
        ArgumentException.ThrowIfNullOrWhiteSpace(relativePath);

        if (Uri.TryCreate(relativePath, UriKind.Absolute, out _))
        {
            throw new UniFiClientException(
                $"UniFi request for profile '{profile.Name}' rejected because absolute URLs are not allowed.",
                profile.Name,
                RedactPath(relativePath));
        }

        var candidate = relativePath.StartsWith('/') ? relativePath : "/" + relativePath;
        var combined = new Uri(profile.BaseAddress, candidate);

        if (!Uri.Compare(profile.BaseAddress, combined, UriComponents.SchemeAndServer, UriFormat.SafeUnescaped, StringComparison.OrdinalIgnoreCase).Equals(0))
        {
            throw new UniFiClientException(
                $"UniFi request for profile '{profile.Name}' rejected because the destination host is outside the configured controller.",
                profile.Name,
                RedactPath(candidate));
        }

        var normalizedPath = combined.AbsolutePath;
        var allowed = profile.GetNormalizedAllowedPathPrefixes()
            .Any(prefix =>
                normalizedPath.Equals(prefix, StringComparison.OrdinalIgnoreCase)
                || normalizedPath.StartsWith(prefix + "/", StringComparison.OrdinalIgnoreCase)
                || normalizedPath.StartsWith(prefix, StringComparison.OrdinalIgnoreCase) && prefix.EndsWith('/'));

        if (!allowed)
        {
            throw new UniFiClientException(
                $"UniFi request for profile '{profile.Name}' rejected because the path is outside the configured scope.",
                profile.Name,
                RedactPath(candidate));
        }

        return combined.PathAndQuery;
    }

    private static string RedactPath(string path)
    {
        var queryIndex = path.IndexOf('?', StringComparison.Ordinal);
        return queryIndex >= 0 ? path[..queryIndex] : path;
    }
}
