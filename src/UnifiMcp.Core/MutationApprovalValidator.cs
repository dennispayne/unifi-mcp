using System.Collections.Concurrent;
using System.Globalization;
using System.Security.Cryptography;
using System.Text;

namespace UnifiMcp.Core;

internal sealed class MutationApprovalValidator
{
    private readonly string _keyEnvironmentVariable;
    private readonly int _maxAgeSeconds;
    private readonly ConcurrentDictionary<string, byte> _usedTokens = new(StringComparer.Ordinal);

    public MutationApprovalValidator(string keyEnvironmentVariable, int maxAgeSeconds)
    {
        _keyEnvironmentVariable = keyEnvironmentVariable;
        _maxAgeSeconds = maxAgeSeconds;
    }

    public void Validate(string token, string scope, string method, string path, byte[]? body)
    {
        if (string.IsNullOrWhiteSpace(token))
        {
            throw new InvalidOperationException("Mutation requests require a short-lived mutationApprovalToken.");
        }

        var parts = token.Split('.', 2, StringSplitOptions.None);
        if (parts.Length != 2 ||
            !long.TryParse(parts[0], NumberStyles.None, CultureInfo.InvariantCulture, out var expiresAt))
        {
            throw new InvalidOperationException("Mutation approval token is invalid.");
        }

        var now = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
        if (expiresAt < now || expiresAt > now + _maxAgeSeconds)
        {
            throw new InvalidOperationException("Mutation approval token is expired or outside the allowed lifetime.");
        }

        var key = Environment.GetEnvironmentVariable(_keyEnvironmentVariable);
        if (string.IsNullOrWhiteSpace(key))
        {
            throw new InvalidOperationException(
                $"Mutation approval key environment variable '{_keyEnvironmentVariable}' is not available.");
        }

        var bodyHash = Convert.ToHexString(SHA256.HashData(body ?? []));
        var message = $"{expiresAt}\n{scope}\n{method}\n{path}\n{bodyHash}";
        var expected = HMACSHA256.HashData(Encoding.UTF8.GetBytes(key), Encoding.UTF8.GetBytes(message));

        byte[] provided;
        try
        {
            provided = Convert.FromBase64String(parts[1]);
        }
        catch (FormatException)
        {
            throw new InvalidOperationException("Mutation approval token is invalid.");
        }

        if (provided.Length != expected.Length || !CryptographicOperations.FixedTimeEquals(provided, expected))
        {
            throw new InvalidOperationException("Mutation approval token does not match this request.");
        }

        if (!_usedTokens.TryAdd(token, 0))
        {
            throw new InvalidOperationException("Mutation approval token has already been used.");
        }
    }
}
