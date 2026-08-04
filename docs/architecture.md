# UniFi MCP architecture

## Design choices

- **.NET 8** for long-term support and secure hosting primitives.
- **One core, two hosts** so stdio and HTTP stay behaviorally aligned.
- **Named credentials + named scopes** so many UniFi access paths can reuse one auth definition without duplicating secrets.

## Runtime flow

1. A host resolves an explicit `--config` path, `UNIFI_MCP_CONFIG`, or a config file directly under the current working/application directory.
2. `UnifiMcpConfigurationLoader` validates the shared runtime config.
3. `UnifiMcpRuntime` converts named credentials/scopes into low-level UniFi access profiles.
4. `UnifiMcpServer` exposes a small, summary-first MCP tool surface.
5. `McpJsonRpcHost` handles protocol framing and JSON-RPC for both hosts.
6. Responses pass through shared sanitization before becoming model-visible.

## Transport split

- `UnifiMcp.Stdio`: one newline-delimited JSON-RPC message per stdin/stdout line, with an input-size cap and per-message error containment.
- `UnifiMcp.Http`: ASP.NET Core JSON-RPC endpoint with origin validation, content negotiation, request-size cap, and a bearer-token gate that is mandatory for remote bindings.

Both transports use the same `McpJsonRpcHost` and `UnifiMcpServer`.

## Enforcement boundaries

- Each profile combines a fixed base address, service kind, relative-path allowlist, HTTP-method allowlist, and explicit mutation setting.
- Non-GET MCP calls require scope-level mutation enablement plus a one-time HMAC approval token generated outside the MCP and bound to the exact request.
- An embedded catalog describes all 87 operations in the official Site Manager v1.0.0 and Network v10.4.57 contracts.
- Response bodies are streamed through a byte cap before centralized JSON sanitization.
- Sanitization removes secrets and common device/network identifiers and enforces collection, property, string, aggregate-output, and recursion-depth budgets.
- A configured SHA-256 certificate pin is always compared exactly; no TLS bypass is available.
