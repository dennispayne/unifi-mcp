# UniFi MCP architecture

## Design choices

- **.NET 8** for long-term support and secure hosting primitives.
- **One core, two hosts** so stdio and HTTP stay behaviorally aligned.
- **Named credentials + named scopes** so many UniFi access paths can reuse one auth definition without duplicating secrets.

## Runtime flow

1. A host resolves `UNIFI_MCP_CONFIG`, `config\unifi-mcp.settings.json`, or `unifi-mcp.settings.json`.
2. `UnifiMcpConfigurationLoader` validates the shared runtime config.
3. `UnifiMcpRuntime` converts named credentials/scopes into low-level UniFi access profiles.
4. `UnifiMcpServer` exposes a small, summary-first MCP tool surface.
5. `McpJsonRpcHost` handles protocol framing and JSON-RPC for both hosts.
6. Responses pass through shared sanitization before becoming model-visible.

## Transport split

- `UnifiMcp.Stdio`: MCP framing over stdin/stdout.
- `UnifiMcp.Http`: ASP.NET Core endpoint for HTTP JSON-RPC requests with a health endpoint, request-size cap, JSON content-type enforcement, and optional bearer-token gate.

Both transports use the same `McpJsonRpcHost` and `UnifiMcpServer`.
