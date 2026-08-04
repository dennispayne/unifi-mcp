# API coverage

unifi-mcp embeds the published UniFi contracts used for operation discovery,
request validation, and schema lookup.

| API | Contract | Operations |
| --- | --- | ---: |
| Site Manager | v1.0.0 | 14 |
| UniFi Network | v10.4.57 | 73 |
| **Total** | | **87** |

The generic API tools expose the complete embedded contract:

- `unifi.api.operations.list` searches operations by service, method, path,
  operation ID, summary, or tag.
- `unifi.api.operation.get` returns parameters, request-body schema, and
  referenced definitions.
- `unifi.api.request` executes documented GET, POST, PUT, PATCH, and DELETE
  operations within the selected scope.

Common inventory reads also have concise dedicated tools for hosts, sites,
devices, clients, networks, Wi-Fi broadcasts, ISP metrics, and device
statistics.

## Contract and runtime versions

The embedded contract version may trail a newer controller application
version. Operation execution remains contract-gated: undocumented methods or
paths are rejected until the embedded contract is updated.

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
