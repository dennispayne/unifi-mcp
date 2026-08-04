using System.Text.Json;

namespace UnifiMcp.Core;

public sealed record McpRequest(
    string Jsonrpc,
    JsonElement? Id,
    string Method,
    JsonElement? Params,
    [property: System.Text.Json.Serialization.JsonIgnore]
    bool HasId = false);

public sealed record McpResponse(
    string Jsonrpc,
    [property: System.Text.Json.Serialization.JsonIgnore(Condition = System.Text.Json.Serialization.JsonIgnoreCondition.Never)]
    JsonElement? Id,
    JsonElement? Result,
    McpError? Error);

public sealed record McpError(int Code, string Message, JsonElement? Data = null);

public sealed record McpTextContent(string Type, string Text);

public sealed record McpToolDescriptor(
    string Name,
    string Description,
    JsonElement? InputSchema);

public sealed record McpCallToolResult(
    IReadOnlyList<McpTextContent> Content,
    JsonElement? StructuredContent = null,
    bool IsError = false);

public sealed record McpInitializeResult(
    string ProtocolVersion,
    JsonElement ServerInfo,
    JsonElement Capabilities,
    string? Instructions = null);