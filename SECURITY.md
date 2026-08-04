# Security guardrails for UniFi MCP usage

This repository is intended to expose UniFi data through an MCP server without sending unnecessary sensitive information to the model.

## Core principles

1. **Secrets stay server-side.** UniFi credentials, session cookies, bearer tokens, API tokens, CSRF values, and controller certificates must never be returned in tool output or written to logs.
2. **Summary first.** The model should receive the smallest useful answer first: counts, health summaries, status changes, and explicitly requested fields.
3. **Least privilege by default.** Use the smallest practical UniFi role and scope. Prefer dedicated read-only identities.
4. **Transports stay thin.** Put policy, sanitization, and auth boundaries in shared core code so stdio and HTTP behave consistently.

## Safe secret handling

- Store secrets in local environment variables or an OS secret store, not in source control.
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
- If HTTP is exposed beyond loopback, require an authorization layer such as `UNIFI_MCP_HTTP_BEARER_TOKEN`.
- Keep `/health` minimal and non-sensitive.

## Immediate implementation expectation

Treat the MCP server as a **policy-enforcing proxy** between the model and UniFi APIs. UniFi responses should be normalized, redacted, and reduced before becoming model-visible output.
