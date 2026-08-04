using System.Text.Json;

namespace Unifi.Mcp.Client;

public sealed class UniFiApiRequest
{
    public UniFiApiRequest(
        HttpMethod method,
        string relativePath,
        byte[]? body = null,
        string? contentType = null,
        IReadOnlyDictionary<string, string>? headers = null)
    {
        Method = method ?? throw new ArgumentNullException(nameof(method));
        RelativePath = string.IsNullOrWhiteSpace(relativePath)
            ? throw new ArgumentException("A relative path is required.", nameof(relativePath))
            : relativePath;
        Body = body;
        ContentType = contentType;
        Headers = headers ?? new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        if (Body is not null && string.IsNullOrWhiteSpace(ContentType))
        {
            throw new ArgumentException("A content type is required when a request body is provided.", nameof(contentType));
        }
    }

    public HttpMethod Method { get; }

    public string RelativePath { get; }

    public byte[]? Body { get; }

    public string? ContentType { get; }

    public IReadOnlyDictionary<string, string> Headers { get; }

    public static UniFiApiRequest Get(string relativePath, IReadOnlyDictionary<string, string>? headers = null) =>
        new(HttpMethod.Get, relativePath, headers: headers);

    public static UniFiApiRequest FromJson<T>(
        HttpMethod method,
        string relativePath,
        T value,
        JsonSerializerOptions? serializerOptions = null,
        IReadOnlyDictionary<string, string>? headers = null)
    {
        return new UniFiApiRequest(
            method,
            relativePath,
            JsonSerializer.SerializeToUtf8Bytes(value, serializerOptions),
            "application/json",
            headers);
    }
}
