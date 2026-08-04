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

        RejectAmbiguousPath(relativePath, profile.Name);
        var queryIndex = relativePath.IndexOf('?', StringComparison.Ordinal);
        var pathPart = queryIndex < 0 ? relativePath : relativePath[..queryIndex];
        var queryPart = queryIndex < 0 ? string.Empty : relativePath[queryIndex..];
        var candidatePath = "/" + pathPart.TrimStart('/');
        var normalizedBase = EnsureTrailingSlash(profile.BaseAddress);
        var combined = new Uri(normalizedBase, candidatePath.TrimStart('/') + queryPart);

        if (!Uri.Compare(profile.BaseAddress, combined, UriComponents.SchemeAndServer, UriFormat.SafeUnescaped, StringComparison.OrdinalIgnoreCase).Equals(0))
        {
            throw new UniFiClientException(
                $"UniFi request for profile '{profile.Name}' rejected because the destination host is outside the configured controller.",
                profile.Name,
                RedactPath(candidatePath));
        }

        var normalizedPath = new Uri(new Uri("https://scope.invalid"), candidatePath).AbsolutePath;
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
                RedactPath(candidatePath));
        }

        return candidatePath.TrimStart('/') + queryPart;
    }

    private static string RedactPath(string path)
    {
        var queryIndex = path.IndexOf('?', StringComparison.Ordinal);
        return queryIndex >= 0 ? path[..queryIndex] : path;
    }

    private static void RejectAmbiguousPath(string path, string profileName)
    {
        var pathOnly = RedactPath(path);
        if (pathOnly.Contains(';', StringComparison.Ordinal) ||
            pathOnly.Contains("%2e", StringComparison.OrdinalIgnoreCase) ||
            pathOnly.Contains("%2f", StringComparison.OrdinalIgnoreCase) ||
            pathOnly.Contains("%5c", StringComparison.OrdinalIgnoreCase) ||
            pathOnly.Contains('\\', StringComparison.Ordinal))
        {
            throw new UniFiClientException(
                $"UniFi request for profile '{profileName}' rejected because the path contains ambiguous encoding.",
                profileName,
                pathOnly);
        }

        var decodedPath = Uri.UnescapeDataString(pathOnly);
        if (decodedPath.Split('/', StringSplitOptions.RemoveEmptyEntries)
            .Any(static segment => segment is "." or ".."))
        {
            throw new UniFiClientException(
                $"UniFi request for profile '{profileName}' rejected because the path contains traversal segments.",
                profileName,
                pathOnly);
        }
    }

    private static Uri EnsureTrailingSlash(Uri baseAddress)
    {
        var builder = new UriBuilder(baseAddress);
        if (!builder.Path.EndsWith('/'))
        {
            builder.Path += "/";
        }

        return builder.Uri;
    }
}
