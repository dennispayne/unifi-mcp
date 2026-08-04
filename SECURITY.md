# Security guardrails for UniFi MCP usage

This repository is intended to expose UniFi data through an MCP server without sending unnecessary sensitive information to the model.

## Core principles

1. **Secrets stay server-side.** UniFi credentials, session cookies, bearer tokens, API tokens, CSRF values, and controller certificates must never be returned in tool output or written to logs.
2. **Summary first.** The model should receive the smallest useful answer first: counts, health summaries, status changes, and explicitly requested fields.
3. **Least privilege by default.** Use the smallest practical UniFi role and scope. Prefer separate read-only and mutation-capable identities/scopes.
4. **Transports stay thin.** Put policy, sanitization, and auth boundaries in shared core code so stdio and HTTP behave consistently.

## Safe secret handling

- Store secrets in an OS-backed PowerShell SecretManagement vault, not in source control or persistent environment variables.
- Use `scripts\Start-UnifiMcp.ps1` so plaintext keys exist only in the launcher/MCP process environment and are removed at exit.
- Never pass secrets in command-line arguments; process command lines are observable by other local tooling.
- PowerShell SecretManagement is the primary provider. Windows Credential Manager is not used.
- Password authentication is the default. `Initialize-UnifiMcpSecrets.ps1 -Unattended` is an explicit tradeoff for noninteractive MCP startup: local encryption remains, but the Windows user account becomes the only access boundary.
- Prefer named `credentials` reused across many `scopes`.
- Do not place real secrets in examples, fixtures, screenshots, or documentation.
- Redact sensitive request and response material before logging or returning it:
  - `Authorization`
  - `Cookie` / `Set-Cookie`
  - CSRF headers/tokens
  - passwords
  - API keys or bearer tokens
  - private keys, client secrets, session identifiers
- Log request IDs, endpoint names, counts, and durations instead of raw headers or bodies.

## Minimize model-visible sensitive data

- Prefer allowlisted fields over full payload passthrough.
- Mask or omit identifiers that are often unnecessary for reasoning, such as:
  - internal IP addresses
  - MAC addresses
  - serial numbers
  - user emails or names
  - site names or controller hostnames when not needed
- Never expose full configuration exports, packet captures, or raw audit/event dumps unless a human explicitly chooses a higher-risk path outside normal model-facing flows.

## HTTP transport guardrails

- Bind to loopback unless you have a deliberate remote deployment plan.
- The host refuses non-loopback bindings unless `UNIFI_MCP_HTTP_AUTH_TOKEN` is set.
- Set `UNIFI_MCP_HTTP_ALLOWED_ORIGINS` for trusted non-loopback browser origins; unexpected origins are rejected.
- Keep `/healthz` minimal and non-sensitive.
- Prefer HTTPS behind a trusted local reverse proxy for any remote deployment.

## Mutation and TLS boundaries

- Scopes explicitly configure allowed HTTP methods and whether mutations are enabled.
- Every POST, PUT, PATCH, or DELETE tool call must include a short-lived, one-time approval token bound to the exact scope, method, path, and body; GET remains the safe default.
- Site Manager connector forwarding is disabled by default and restricted to explicitly configured connector path prefixes.
- Prefer separate write-enabled scopes with narrowly permissioned API keys rather than granting mutation rights to broad inventory scopes.
- API paths must remain within the profile allowlist; absolute URLs, traversal, encoded separators, backslashes, and path parameters are rejected.
- Never disable certificate validation. Trust the controller certificate in Windows or configure its exact `pinnedServerCertificateSha256` fingerprint.
- Automated and live validation must use inventory, status, statistics, and metadata GET endpoints only. Mutation behavior is tested with mock transports.

## Immediate implementation expectation

Treat the MCP server as a **policy-enforcing proxy** between the model and UniFi APIs. UniFi responses are bounded, redacted, and reduced before becoming model-visible output.
