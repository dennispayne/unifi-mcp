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
            string? requestText;
            try
            {
                requestText = await ReadMessageAsync(input, _server.MaxStdioMessageBytes, cancellationToken).ConfigureAwait(false);
            }
            catch (InvalidOperationException)
            {
                var errorText = JsonSerializer.Serialize(
                    new McpResponse("2.0", null, null, new McpError(-32600, "MCP stdio message exceeds the configured size limit.")),
                    JsonOptions);
                await WriteMessageAsync(output, errorText, cancellationToken).ConfigureAwait(false);
                await output.FlushAsync(cancellationToken).ConfigureAwait(false);
                continue;
            }

            if (requestText is null)
            {
                return;
            }

            var responseText = await HandleJsonRpcAsync(requestText, cancellationToken).ConfigureAwait(false);
            if (responseText is null)
            {
                continue;
            }

            await WriteMessageAsync(output, responseText, cancellationToken).ConfigureAwait(false);
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
                return SerializeInvalidRequest("Batch JSON-RPC requests are not supported.");
            }

            if (document.RootElement.ValueKind != JsonValueKind.Object ||
                !document.RootElement.TryGetProperty("jsonrpc", out var jsonRpcElement) ||
                jsonRpcElement.ValueKind != JsonValueKind.String ||
                !string.Equals(jsonRpcElement.GetString(), "2.0", StringComparison.Ordinal) ||
                !document.RootElement.TryGetProperty("method", out var methodElement) ||
                methodElement.ValueKind != JsonValueKind.String ||
                string.IsNullOrWhiteSpace(methodElement.GetString()))
            {
                return SerializeInvalidRequest("A JSON-RPC 2.0 object with a method is required.");
            }

            var request = JsonSerializer.Deserialize<McpRequest>(document.RootElement, JsonOptions)
                ?? throw new JsonException("Request could not be deserialized.");
            var hasId = document.RootElement.TryGetProperty("id", out var idElement);
            if (hasId && idElement.ValueKind is not (JsonValueKind.String or JsonValueKind.Number or JsonValueKind.Null))
            {
                return JsonSerializer.Serialize(
                    new McpResponse("2.0", null, null, new McpError(-32600, "JSON-RPC id must be a string, number, or null.")),
                    JsonOptions);
            }

            var response = await _server.HandleAsync(request with { HasId = hasId }, cancellationToken).ConfigureAwait(false);
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
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception)
        {
            return JsonSerializer.Serialize(
                new McpResponse(
                    "2.0",
                    null,
                    null,
                    new McpError(-32603, "Internal error.")),
                JsonOptions);
        }
    }

    private static string SerializeInvalidRequest(string message) =>
        JsonSerializer.Serialize(
            new McpResponse("2.0", null, null, new McpError(-32600, message)),
            JsonOptions);

    public static async Task WriteMessageAsync(Stream output, string payload, CancellationToken cancellationToken = default)
    {
        if (payload.Contains('\n', StringComparison.Ordinal) || payload.Contains('\r', StringComparison.Ordinal))
        {
            throw new InvalidOperationException("MCP stdio messages must not contain embedded newlines.");
        }

        var bytes = Encoding.UTF8.GetBytes(payload);
        await output.WriteAsync(bytes, cancellationToken).ConfigureAwait(false);
        await output.WriteAsync("\n"u8.ToArray(), cancellationToken).ConfigureAwait(false);
    }

    public static async Task<string?> ReadMessageAsync(
        Stream input,
        int maxMessageBytes,
        CancellationToken cancellationToken = default)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(maxMessageBytes);
        var readBuffer = ArrayPool<byte>.Shared.Rent(4096);
        var messageBuffer = new ArrayBufferWriter<byte>(Math.Min(maxMessageBytes, 4096));
        var exceedsLimit = false;

        try
        {
            while (true)
            {
                var read = await input.ReadAsync(readBuffer.AsMemory(0, 1), cancellationToken).ConfigureAwait(false);
                if (read == 0)
                {
                    if (exceedsLimit)
                    {
                        throw new InvalidOperationException($"MCP stdio message exceeds the {maxMessageBytes}-byte limit.");
                    }

                    return messageBuffer.WrittenCount == 0
                        ? null
                        : Encoding.UTF8.GetString(messageBuffer.WrittenSpan);
                }

                if (readBuffer[0] == (byte)'\n')
                {
                    if (exceedsLimit)
                    {
                        throw new InvalidOperationException($"MCP stdio message exceeds the {maxMessageBytes}-byte limit.");
                    }

                    var length = messageBuffer.WrittenCount;
                    if (length > 0 && messageBuffer.WrittenMemory.Span[length - 1] == (byte)'\r')
                    {
                        length--;
                    }

                    return Encoding.UTF8.GetString(messageBuffer.WrittenMemory[..length].ToArray());
                }

                if (messageBuffer.WrittenCount >= maxMessageBytes)
                {
                    exceedsLimit = true;
                    continue;
                }

                messageBuffer.GetSpan(1)[0] = readBuffer[0];
                messageBuffer.Advance(1);
            }
        }
        finally
        {
            ArrayPool<byte>.Shared.Return(readBuffer);
        }
    }
}