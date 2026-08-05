# API coverage

unifi-mcp embeds the published UniFi contracts used for operation discovery,
request validation, and schema lookup.

| API | Contract | Scope `service` | Operations |
| --- | --- | --- | ---: |
| Site Manager | v1.0.0 | `siteManager` | 14 |
| UniFi Network | v10.4.57 | `network` | 73 |
| UniFi Protect | v7.1.87 | `protect` | 73 |
| UniFi Access | v4.0.10 | `access` | 107 |
| UniFi Mobility | v1.0.0 | `mobility` | 8 |
| **Total** | | | **275** |

The generic API tools expose the complete embedded contract:

- `unifi.api.operations.list` searches operations by service, method, path,
  operation ID, summary, or tag.
- `unifi.api.operation.get` returns parameters, request-body schema, and
  referenced definitions.
- `unifi.api.request` executes documented GET, POST, PUT, PATCH, and DELETE
  operations within the selected scope.

Common inventory reads also have concise dedicated tools for hosts, sites,
devices, clients, networks, Wi-Fi broadcasts, ISP metrics, device statistics,
Protect cameras, Protect sensors, Protect NVRs, Access doors, Access devices,
Mobility workspaces, and Mobility devices.

## Service authentication and base paths

Every scope selects exactly one service. Credentials remain independent of
scopes, so any number of credentials and scopes can be combined.

| Service | Base address example | Auth header | Notes |
| --- | --- | --- | --- |
| `siteManager` | `https://api.ui.com` | `X-API-KEY` | Cloud API keys from the UniFi site manager. |
| `network` | `https://<console>/proxy/network/integration` | `X-API-KEY` | Console API key with Network access. |
| `protect` | `https://<console>/proxy/protect/integration` | `X-API-KEY` | Console API key with Protect access. Protect keys are not valid for Network. |
| `access` | `https://<console>:12445` | `Authorization` with `apiKeyValuePrefix: "Bearer"` | Access API token from UniFi Access → Settings → General → Advanced. Tokens carry `view:`/`edit:` permission scopes. |
| `mobility` | `https://api.ui.com` | `X-API-KEY` | Cloud key issued with `mobility` app scope plus `read:mobility` (and `write:mobility` for `PUT`). |

Suggested `allowedRelativePathPrefixes` per service:

| Service | Prefix |
| --- | --- |
| `siteManager` | `/v1` |
| `network` | `/v1` |
| `protect` | `/v1` |
| `access` | `/api/v1/developer` |
| `mobility` | `/v1/mobility` |

Site Manager scopes can additionally proxy other applications on a console
using the cloud connector, gated by `allowConnectorProxy` and
`connectorAllowedPathPrefixes` (for example `/proxy/protect/integration/v1`).

## Services without a public API

Ubiquiti does not publish an official public REST API for UniFi Talk or
UniFi Connect, so neither is embedded. UniFi Identity is not published as an
independent API; its documented operations appear inside the UniFi Access
contract under the `UniFi Identity` tag and are therefore covered by the
`access` service.

## Contract and runtime versions

The embedded contract version may trail a newer controller application
version. Operation execution remains contract-gated: undocumented methods or
paths are rejected until the embedded contract is updated.

UniFi Protect operation IDs are not published in the official contract, so
unifi-mcp assigns deterministic method-and-path identifiers such as
`getCameras` and `patchCamerasById`. Use `unifi.api.operations.list` to
discover them.

## Mutation requirements

A mutation runs only when all of these conditions are met:

1. The operation exists in the official embedded contract.
2. The selected scope enables mutations.
3. The HTTP method is allowed by that scope.
4. The path remains inside the configured path boundary.
5. The request includes a valid, one-time approval token bound to the exact
   scope, method, path, body, and expiration.

Live project validation uses GET requests only. Mutation behavior is covered
with mock transports.

## Contract sources

Embedded contracts are derived from published OpenAPI documents:

- Site Manager, Network, Protect, and Mobility contracts come from the
  MIT-licensed [Altered-Tech/unifi-openapi-specs](https://github.com/Altered-Tech/unifi-openapi-specs)
  mirror of Ubiquiti's `developer.ui.com` portal.
- The Access contract comes from the MIT-licensed
  [YuDefine/unifi-access-api-openapi](https://github.com/YuDefine/unifi-access-api-openapi)
  conversion of Ubiquiti's official UniFi Access API documentation.
