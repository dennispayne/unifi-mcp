# Security

unifi-mcp is a policy-enforcing proxy between an MCP client and UniFi APIs.
Its defaults are designed to limit credential exposure, constrain reachable
API operations, and minimize infrastructure data sent to the model.

## Supported versions

Security fixes are provided for the latest published release. Upgrade before
reporting an issue that is already resolved on `master`.

## Credential boundary

UniFi API keys, the mutation approval key, and the optional HTTP bearer token
are read from environment variables inherited by the MCP process.

- Store only environment-variable **names** in configuration.
- Populate values through the operating system, service manager, container
  platform, or MCP host immediately before launch.
- Never place secret values in JSON, source control, screenshots, prompts,
  logs, or command-line arguments.
- Give the MCP process only the variables it needs. Do not export credentials
  broadly into unrelated shells or desktop sessions.
- Treat any process running under the same operating-system identity as part
  of the trust boundary; sufficiently privileged local processes may inspect
  process memory or inherited environment data.
- Rotate a credential immediately if it appears in model context, terminal
  history, logs, crash dumps, or a committed file.

Environment variables are a handoff mechanism, not a secret store. Operators
remain free to source them from their preferred secret manager without
unifi-mcp depending on a particular vault product.

## Least privilege

- Create separate UniFi credentials for unrelated trust domains.
- Prefer read-only credentials for inventory and monitoring.
- Use separate, narrowly permissioned credentials and scopes for mutations.
- Keep allowed path prefixes and HTTP methods as narrow as practical.
- Disable Site Manager connector forwarding unless it is required; when
  enabled, restrict it to explicit connector path prefixes.

## Mutation approvals

POST, PUT, PATCH, and DELETE require all normal scope checks plus a one-time
HMAC approval token.

- Use a random, high-entropy approval key that is different from every UniFi
  API key and HTTP bearer token.
- The token is bound to the exact scope, method, path, serialized body, and
  expiration.
- Tokens are short-lived and rejected after first use.
- Generate tokens outside the model conversation with the packaged stdio
  executable's `--create-mutation-approval` mode.
- Never send the approval key itself to an MCP client or model.

Live project validation uses GET requests only. Mutation behavior is tested
against mock transports.

## Model-visible data

All upstream responses pass through centralized size limits and sanitization
before they become tool output.

- Credentials and authentication headers are removed.
- Common IP addresses, MAC addresses, serial numbers, email addresses, and
  host identifiers are redacted.
- Collection size, object properties, string length, JSON depth, response
  bytes, and aggregate output characters are bounded.
- Raw response summaries remain disabled unless explicitly enabled.

No sanitizer can determine every organization's sensitivity requirements.
Use narrow UniFi permissions and avoid requesting broad exports, packet
captures, or audit dumps through a model-facing workflow.

## Transport security

### Stdio

Run the stdio host as a dedicated, least-privileged user where practical.
Standard output is reserved for MCP protocol messages; operational failures
are written to standard error without credential values.

### HTTP

- HTTP binds to loopback by default.
- Non-loopback binding requires a bearer token.
- Browser origins must be loopback or explicitly allowlisted.
- Request bodies and accepted content types are constrained.
- Use HTTPS through a trusted reverse proxy for remote access.

## TLS

Certificate validation cannot be disabled. For private or self-signed
controllers, install the issuing certificate in the operating-system trust
store or configure the exact SHA-256 server-certificate pin. Rotate the pin
when the controller certificate changes.

## Reporting a vulnerability

Report vulnerabilities privately through
[GitHub Security Advisories](https://github.com/dennispayne/unifi-mcp/security/advisories/new).
Do not include real API keys, controller exports, or identifying network data
in public issues.
