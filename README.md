# unifi-mcp

Production-ready scaffold for a UniFi Model Context Protocol (MCP) server built on .NET 8.

## Why .NET

- Best fit for a long-lived, secure MCP server in a Microsoft-centric environment.
- Shared hosting, HTTP, configuration, and diagnostics primitives are built in.
- Cleanly supports separate stdio and HTTP entrypoints over one core policy layer.

## Solution layout

```text
UnifiMcp.sln
src/
  Unifi.Mcp.Client/  Low-level UniFi API client abstractions and session handling
  UnifiMcp.Core/     Shared MCP JSON-RPC host, runtime loader, sanitization, tool surface
  UnifiMcp.Stdio/    Console host for MCP stdio transport
  UnifiMcp.Http/     ASP.NET Core host for MCP HTTP transport
tests/
  Unifi.Mcp.Client.SmokeTests/
config/
  guardrails.example.json
  unifi-mcp.settings.example.json
```

## Architecture

- **One shared config model** with named `credentials` and named `scopes`.
- **Many scopes can reuse one credential** without duplicating secrets.
- **Two explicit transport hosts** reuse the same `UnifiMcpServer` and `McpJsonRpcHost`.
- **Small, summary-first responses** keep token usage and sensitive exposure down.

See `docs/architecture.md` and `docs/IMPLEMENTATION_NOTES.md`.

## Configuration

1. Copy `config\unifi-mcp.settings.example.json` to `config\unifi-mcp.settings.json`.
2. Set the environment variables referenced by the credential entries.
3. Optionally set `UNIFI_MCP_CONFIG` to an explicit config path.

The current example config uses `UNIFI_SITE_MANAGER_API_KEY` for UniFi Site Manager and `UNIFI_NETWORK_API_KEY` for the Network/API scope.

Example local environment setup:

```powershell
$env:UNIFI_SITE_MANAGER_API_KEY = 'set-a-real-secret-locally'
$env:UNIFI_NETWORK_API_KEY = 'set-a-real-secret-locally'
```

## Running

### Stdio transport

```powershell
$env:UNIFI_MCP_CONFIG = 'config\unifi-mcp.settings.json'
dotnet run --project src\UnifiMcp.Stdio --configuration Release
```

### HTTP transport

```powershell
$env:UNIFI_MCP_CONFIG = 'config\unifi-mcp.settings.json'
$env:UNIFI_MCP_HTTP_URLS = 'http://127.0.0.1:8765'
$env:UNIFI_MCP_HTTP_AUTH_TOKEN = 'set-a-local-bearer-token'
dotnet run --project src\UnifiMcp.Http --configuration Release
```

Default endpoints:

- `POST /mcp`
- `GET /healthz`

If `UNIFI_MCP_HTTP_AUTH_TOKEN` is set, the HTTP host requires `Authorization: Bearer <token>`.

## Initial tool surface

The scaffold exposes a small, summary-first MCP surface:

- `unifi.scopes.list`
- `unifi.scopes.get`
- `unifi.scope.read`

`unifi.scope.read` is designed for later real controller access and returns redacted, bounded summaries rather than dumping raw controller payloads by default.

## Security posture

- Credentials are referenced, not embedded, in the example runtime config.
- Tool output is redacted and size-limited.
- Reserved auth headers cannot be injected by callers.
- HTTP transport defaults to loopback and disables the server banner.
- Stdio host writes protocol traffic to stdout and keeps failures on stderr.

## Validation

- `dotnet build UnifiMcp.sln -c Release`
- `dotnet run --project tests\Unifi.Mcp.Client.SmokeTests\Unifi.Mcp.Client.SmokeTests.csproj -c Release`
- stdio initialize verified manually against `UnifiMcp.Stdio`
- HTTP `/healthz` and `/mcp` initialize verified manually against `UnifiMcp.Http`

## Remaining implementation work

- Add concrete UniFi inventory, health, and diagnostics tools on top of `unifi.scope.read`.
- Add richer MCP features such as prompts/resources if needed by target clients.
- Add broader end-to-end transport tests as the concrete tool surface expands.

## License

MIT
