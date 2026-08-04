# Implementation notes: secure, low-exposure UniFi MCP design

## Architecture fit

The solution separates `Unifi.Mcp.Client`, `UnifiMcp.Core`, `UnifiMcp.Http`, and `UnifiMcp.Stdio`.

- Put UniFi protocol and auth handling in `Unifi.Mcp.Client`.
- Put MCP request handling, config binding, redaction, and tool policy in `UnifiMcp.Core`.
- Keep both transport projects as thin entrypoints.

## Credentials and scopes

Use a two-level runtime configuration model:

1. `credentials`: named auth definitions that point at environment variables
2. `scopes`: named controller/site access definitions that reuse one credential

This keeps secrets centralized and lets multiple scopes share one credential cleanly.

## Tool output rules

All UniFi API responses should pass through one shared policy function before they are:

- returned to the model
- logged
- cached
- included in errors

Recommended stages:

1. classify endpoint risk
2. select an allowlisted subset of fields
3. redact sensitive headers and values
4. mask identifiers that are not needed
5. compress to a concise summary plus bounded items

## Transport notes

- `UnifiMcp.Stdio` should emit MCP protocol traffic only on stdout.
- `UnifiMcp.Http` should keep auth and routing concerns out of the core server implementation.
- MCP notifications such as `notifications/initialized` should not emit responses.

## Implemented API surfaces

- Site Manager hosts, sites, devices, ISP metrics, SD-WAN, and connector operations
- Network application info, sites, devices, clients, networks, Wi-Fi broadcasts, and device statistics
- Full Network configuration/action coverage through the generic API executor
- Operation discovery and request schemas for all 87 official contract operations
- Scope discovery and constrained generic requests for newly granted API scopes

Concrete tools enforce service type and bounded pagination. Generic mutations require scope enablement, an allowed HTTP method, official-contract matching, and a one-time request-bound approval token. All output still passes through the shared sanitizer, so adding an endpoint cannot bypass the model-exposure policy.
