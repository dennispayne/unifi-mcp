using System.Buffers;
using System.Text;
using Unifi.Mcp.Client;

namespace UnifiMcp.Core;

internal static class BoundedResponseReader
{
    public static async Task<string> ReadAsStringAsync(
        HttpResponseMessage response,
        int maxBytes,
        string profileName,
        string path,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(response);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(maxBytes);

        if (response.Content is null)
        {
            return string.Empty;
        }

        if (response.Content.Headers.ContentLength > maxBytes)
        {
            throw CreateTooLargeException(profileName, path, maxBytes);
        }

        await using var source = await response.Content.ReadAsStreamAsync(cancellationToken).ConfigureAwait(false);
        using var destination = new MemoryStream(Math.Min(maxBytes, 16 * 1024));
        var buffer = ArrayPool<byte>.Shared.Rent(16 * 1024);

        try
        {
            var totalBytes = 0;
            while (true)
            {
                var read = await source.ReadAsync(buffer.AsMemory(0, buffer.Length), cancellationToken).ConfigureAwait(false);
                if (read == 0)
                {
                    break;
                }

                totalBytes += read;
                if (totalBytes > maxBytes)
                {
                    throw CreateTooLargeException(profileName, path, maxBytes);
                }

                await destination.WriteAsync(buffer.AsMemory(0, read), cancellationToken).ConfigureAwait(false);
            }

            var encoding = TryGetEncoding(response.Content.Headers.ContentType?.CharSet) ?? Encoding.UTF8;
            return encoding.GetString(destination.GetBuffer(), 0, checked((int)destination.Length));
        }
        finally
        {
            ArrayPool<byte>.Shared.Return(buffer);
        }
    }

    private static Encoding? TryGetEncoding(string? charset)
    {
        if (string.IsNullOrWhiteSpace(charset))
        {
            return null;
        }

        try
        {
            return Encoding.GetEncoding(charset.Trim('"'));
        }
        catch (ArgumentException)
        {
            return null;
        }
    }

    private static UniFiClientException CreateTooLargeException(string profileName, string path, int maxBytes) =>
        new(
            $"UniFi response for profile '{profileName}' exceeded the configured {maxBytes}-byte limit.",
            profileName,
            StripQuery(path));

    private static string StripQuery(string path)
    {
        var queryIndex = path.IndexOf('?', StringComparison.Ordinal);
        return queryIndex < 0 ? path : path[..queryIndex];
    }
}
