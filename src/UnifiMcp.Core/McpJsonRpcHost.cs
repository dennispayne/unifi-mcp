using System.Buffers;
using System.Text;
using System.Text.Json;

namespace UnifiMcp.Core;

public sealed class McpJsonRpcHost
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        DefaultIgnoreCondition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull
    };

    private readonly UnifiMcpServer _server;

    public McpJsonRpcHost(UnifiMcpServer server)
    {
        _server = server ?? throw new ArgumentNullException(nameof(server));
    }

    public async Task HandleStdioAsync(Stream input, Stream output, CancellationToken cancellationToken = default)
    {
        while (!cancellationToken.IsCancellationRequested)
        {
            var requestText = await ReadFrameAsync(input, cancellationToken).ConfigureAwait(false);
            if (requestText is null)
            {
                return;
            }

            var responseText = await HandleJsonRpcAsync(requestText, cancellationToken).ConfigureAwait(false);
            if (responseText is null)
            {
                continue;
            }

            await WriteFrameAsync(output, responseText, cancellationToken).ConfigureAwait(false);
            await output.FlushAsync(cancellationToken).ConfigureAwait(false);
        }
    }

    public async Task<string?> HandleJsonRpcAsync(string requestText, CancellationToken cancellationToken = default)
    {
        try
        {
            using var document = JsonDocument.Parse(requestText);
            if (document.RootElement.ValueKind == JsonValueKind.Array)
            {
                return JsonSerializer.Serialize(
                    new McpResponse(
                        "2.0",
                        null,
                        null,
                        new McpError(-32600, "Batch JSON-RPC requests are not supported.")),
                    JsonOptions);
            }

            var request = JsonSerializer.Deserialize<McpRequest>(document.RootElement, JsonOptions)
                ?? throw new JsonException("Request could not be deserialized.");

            var response = await _server.HandleAsync(request, cancellationToken).ConfigureAwait(false);
            return response is null ? null : JsonSerializer.Serialize(response, JsonOptions);
        }
        catch (JsonException)
        {
            return JsonSerializer.Serialize(
                new McpResponse(
                    "2.0",
                    null,
                    null,
                    new McpError(-32700, "Invalid JSON-RPC payload.")),
                JsonOptions);
        }
    }

    public static async Task WriteFrameAsync(Stream output, string payload, CancellationToken cancellationToken = default)
    {
        var bytes = Encoding.UTF8.GetBytes(payload);
        var header = Encoding.ASCII.GetBytes($"Content-Length: {bytes.Length}\r\n\r\n");
        await output.WriteAsync(header, cancellationToken).ConfigureAwait(false);
        await output.WriteAsync(bytes, cancellationToken).ConfigureAwait(false);
    }

    public static async Task<string?> ReadFrameAsync(Stream input, CancellationToken cancellationToken = default)
    {
        var buffer = ArrayPool<byte>.Shared.Rent(1);
        try
        {
            var header = new StringBuilder();
            var lastFour = new Queue<char>(4);

            while (true)
            {
                var read = await input.ReadAsync(buffer.AsMemory(0, 1), cancellationToken).ConfigureAwait(false);
                if (read == 0)
                {
                    return null;
                }

                var current = (char)buffer[0];
                header.Append(current);
                lastFour.Enqueue(current);
                if (lastFour.Count > 4)
                {
                    lastFour.Dequeue();
                }

                if (lastFour.Count == 4 && new string(lastFour.ToArray()) == "\r\n\r\n")
                {
                    break;
                }
            }

            var contentLength = ParseContentLength(header.ToString());
            var payloadBuffer = new byte[contentLength];
            var offset = 0;
            while (offset < contentLength)
            {
                var read = await input.ReadAsync(payloadBuffer.AsMemory(offset, contentLength - offset), cancellationToken).ConfigureAwait(false);
                if (read == 0)
                {
                    throw new EndOfStreamException("Unexpected end of stream while reading MCP frame payload.");
                }

                offset += read;
            }

            return Encoding.UTF8.GetString(payloadBuffer);
        }
        finally
        {
            ArrayPool<byte>.Shared.Return(buffer);
        }
    }

    private static int ParseContentLength(string header)
    {
        foreach (var line in header.Split("\r\n", StringSplitOptions.RemoveEmptyEntries))
        {
            if (!line.StartsWith("Content-Length:", StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            var value = line["Content-Length:".Length..].Trim();
            if (int.TryParse(value, out var contentLength) && contentLength >= 0)
            {
                return contentLength;
            }
        }

        throw new InvalidOperationException("MCP frame is missing a valid Content-Length header.");
    }
}