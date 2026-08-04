using System.Globalization;
using System.Security.Cryptography;
using System.Text;

namespace UnifiMcp.Core;

public static class MutationApprovalToken
{
    public static string Create(
        string key,
        string scope,
        string method,
        string path,
        byte[]? body,
        DateTimeOffset expiresAt)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(key);
        ArgumentException.ThrowIfNullOrWhiteSpace(scope);
        ArgumentException.ThrowIfNullOrWhiteSpace(method);
        ArgumentException.ThrowIfNullOrWhiteSpace(path);

        var expiresAtUnixSeconds = expiresAt.ToUnixTimeSeconds();
        var message = BuildMessage(expiresAtUnixSeconds, scope, method, path, body);
        var signature = HMACSHA256.HashData(
            Encoding.UTF8.GetBytes(key),
            Encoding.UTF8.GetBytes(message));
        return $"{expiresAtUnixSeconds.ToString(CultureInfo.InvariantCulture)}.{Convert.ToBase64String(signature)}";
    }

    internal static string BuildMessage(long expiresAt, string scope, string method, string path, byte[]? body)
    {
        var bodyHash = Convert.ToHexString(SHA256.HashData(body ?? []));
        return $"{expiresAt}\n{scope}\n{method}\n{path}\n{bodyHash}";
    }
}
