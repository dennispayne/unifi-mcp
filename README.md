# unifi-mcp

Security-first UniFi Model Context Protocol (MCP) server targeting .NET 8 and developed with the .NET 10 / Visual Studio 2026 toolchain.

## Architecture

- One shared configuration model with reusable named credentials and N named API scopes.
- Separate stdio and HTTP hosts over the same policy, tool, and sanitization core.
- Summary-first, bounded responses to minimize token use and model-visible data.
- A reusable UniFi API client with API-key and legacy session authentication.

```text
UnifiMcp.slnx
src/
  Unifi.Mcp.Client/  UniFi client, authentication, TLS, and scope enforcement
  UnifiMcp.Core/     MCP JSON-RPC, configuration, sanitization, and tools
  UnifiMcp.Stdio/    Newline-delimited stdio host
  UnifiMcp.Http/     ASP.NET Core HTTP host
tests/
  Unifi.Mcp.Client.SmokeTests/
```

See `docs/architecture.md` and `docs/IMPLEMENTATION_NOTES.md`.

## Installation

Download the archive for your platform from the GitHub Release:

- `win-x64` or `win-arm64` (`.zip`)
- `linux-x64` or `linux-arm64`
- `osx-x64` or `osx-arm64`

Verify it against `SHA256SUMS.txt`, extract it, copy
`config\unifi-mcp.settings.example.json` to
`config\unifi-mcp.settings.json`, and configure the referenced environment
variables. Each archive contains self-contained `stdio` and `http` hosts; a
separate .NET installation is not required.

## Configuration

1. Copy `config\unifi-mcp.settings.example.json` to `config\unifi-mcp.settings.json`.
2. Set the environment variables referenced by its credential entries.
3. Optionally set `UNIFI_MCP_CONFIG` to an explicit configuration path.

The example expects `UNIFI_SITE_MANAGER_API_KEY` for Site Manager and `UNIFI_NETWORK_API_KEY` for Network. For normal use, keep both values in a PowerShell SecretManagement vault and launch the MCP with `scripts\Start-UnifiMcp.ps1`; do not paste them into chat, configuration, command arguments, or persistent environment variables.

```powershell
Install-Module Microsoft.PowerShell.SecretManagement, Microsoft.PowerShell.SecretStore -Scope CurrentUser
pwsh -NoLogo -NoProfile -File scripts\Initialize-UnifiMcpSecrets.ps1
```

SecretManagement is the default and primary integration. The launcher retrieves SecureStrings, places plaintext only in its process environment for inheritance by the MCP child, and restores the environment when the MCP exits. This is local and has no Azure, Azure Key Vault, or Windows Credential Manager dependency. It is vault-provider-neutral; specify `-Vault` when not using the default vault.

The default setup requires the SecretStore password when a launcher process opens the vault. If an MCP client must launch it noninteractively, rerun setup with `-Unattended`. SecretStore remains encrypted locally, but any process running as your Windows account can then retrieve its contents without a second password. This enables child processes launched during an already-running Copilot session; it does not inject secrets into the Copilot process itself.

The Network scope targets `https://unifi/proxy/network/integration`. For a private or self-signed console certificate, trust it in Windows or set `pinnedServerCertificateSha256` to its exact SHA-256 fingerprint. Never disable TLS validation.

## Running

### Stdio

```powershell
dotnet build UnifiMcp.slnx --configuration Release
pwsh -NoLogo -NoProfile -File scripts\Start-UnifiMcp.ps1 -Mode Stdio
```

If Copilot inherits `UNIFI_SITE_MANAGER_API_KEY` and `UNIFI_NETWORK_API_KEY`, add the packaged stdio executable under `mcpServers` in `%USERPROFILE%\.copilot\mcp-config.json`:

```json
{
  "mcpServers": {
    "unifi": {
      "type": "stdio",
      "command": "C:\\Tools\\unifi-mcp-v1.0.0-win-x64\\stdio\\UnifiMcp.Stdio.exe",
      "args": [
        "--config",
        "C:\\Tools\\unifi-mcp-v1.0.0-win-x64\\config\\unifi-mcp.settings.json"
      ],
      "tools": [
        "unifi.scopes.list",
        "unifi.scopes.get",
        "unifi.scope.read",
        "unifi.api.operations.list",
        "unifi.api.operation.get",
        "unifi.api.request",
        "unifi.site_manager.hosts.list",
        "unifi.site_manager.sites.list",
        "unifi.site_manager.devices.list",
        "unifi.site_manager.isp_metrics.get",
        "unifi.network.info.get",
        "unifi.network.sites.list",
        "unifi.network.devices.list",
        "unifi.network.clients.list",
        "unifi.network.networks.list",
        "unifi.network.wifi.list",
        "unifi.network.device.statistics.get"
      ]
    }
  }
}
```

If the file already contains other servers, merge only the `"unifi"` member into its existing `mcpServers` object. Run `/mcp` after saving to inspect or restart the CLI to reload it.

### HTTP

```powershell
Set-Secret -Name UniFiMcpHttpToken -Secret (Read-Host 'HTTP bearer token' -AsSecureString)
pwsh -NoLogo -NoProfile -File scripts\Start-UnifiMcp.ps1 -Mode Http `
  -HttpAuthTokenSecretName UniFiMcpHttpToken `
  -HttpAllowedOrigins 'https://trusted-client.example'
```

Endpoints are `POST /mcp` and `GET /healthz`. MCP requests require JSON content and an `Accept` header containing `application/json` or `*/*`. When an auth token is configured, send it as a Bearer authorization token. Authentication is mandatory for any non-loopback binding. Browser origins must be loopback or explicitly allowlisted.

## Tools

Discovery and constrained generic access:

- `unifi.scopes.list`
- `unifi.scopes.get`
- `unifi.scope.read`
- `unifi.api.operations.list`
- `unifi.api.operation.get`
- `unifi.api.request`

Site Manager:

- `unifi.site_manager.hosts.list`
- `unifi.site_manager.sites.list`
- `unifi.site_manager.devices.list`
- `unifi.site_manager.isp_metrics.get`

UniFi Network:

- `unifi.network.info.get`
- `unifi.network.sites.list`
- `unifi.network.devices.list`
- `unifi.network.clients.list`
- `unifi.network.networks.list`
- `unifi.network.wifi.list`
- `unifi.network.device.statistics.get`

The embedded official operation catalog covers 14 Site Manager v1.0.0 operations and 73 Network v10.4.57 operations. `unifi.api.operation.get` returns the parameters, request-body schema, and referenced definitions for one operation. `unifi.api.request` supports GET, POST, PUT, PATCH, and DELETE, including the Site Manager connector.

Each scope controls `allowMutations` and `allowedHttpMethods`. Site Manager connector forwarding is separately gated by `allowConnectorProxy` and `connectorAllowedPathPrefixes`, so wildcard connector operations cannot escape configured API families. Every non-GET call also requires a short-lived, one-time HMAC approval token bound to its exact scope, method, path, and body:

```powershell
# Put the exact JSON request body in mutation-body.json, then generate approval.
$token = pwsh -NoProfile -File scripts\New-UnifiMutationApproval.ps1 `
  -Scope network `
  -Method POST `
  -Path '/v1/sites/<site-id>/networks' `
  -BodyPath mutation-body.json
```

Pass the resulting value as `mutationApprovalToken` with the same body. The approval secret remains in SecretManagement and is never sent to the model. Direct-EXE launches must inherit `UNIFI_MCP_MUTATION_APPROVAL_KEY` in addition to the two API-key variables; `Start-UnifiMcp.ps1` loads all three automatically.

Tools enforce path and method allowlists, cap request and response bodies, and return bounded, redacted output. Raw response summaries remain disabled unless `allowRawResponses` is explicitly enabled. Prefer the concrete tools for common reads and use the generic executor for the remaining official API surface.

## Security posture

- Configuration references environment variables rather than embedding credentials.
- Output redacts credentials and common network/device identifiers.
- Item, property, string, aggregate-character, response-body, stdio-message, and JSON-depth limits bound exposure and token usage.
- Absolute URLs, traversal, ambiguous encodings, and paths outside each scope are rejected before authentication.
- Mutations require both scope-level enablement and a one-time request-bound approval token.
- Reserved authentication headers cannot be supplied by tool callers.
- HTTP defaults to loopback, validates origins, caps requests, and requires authentication for remote bindings.
- TLS validation is always active, including exact pin comparison when configured.

See `SECURITY.md` for deployment and usage guardrails.

## Validation

```powershell
dotnet build UnifiMcp.slnx --configuration Release
dotnet run --project tests\Unifi.Mcp.Client.SmokeTests\Unifi.Mcp.Client.SmokeTests.csproj --configuration Release
```

## Creating a release

Create self-contained release archives and checksums for the current operating
system:

```powershell
pwsh -NoProfile -File scripts\Publish-Release.ps1 -Version 1.0.0
```

Artifacts are written to `artifacts\release`. Pushing an annotated `v1.0.0`
tag runs the same validation and packaging process and creates a GitHub
Release automatically.

## License

MIT
