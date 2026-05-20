# Phase 2 Command Contracts

Same conventions as Phase 1 (global options FR-024, exit codes FR-025). Phase 2 is P3 — diagnostics & admin breadth.

## US5 — `sorcha wallet …` (diagnostics, extends existing wallet command)

| Command | Args | Method + Endpoint | Output |
|---------|------|-------------------|--------|
| `wallet did-document <address>` | — | `GetWalletDidDocumentAsync` → `GET /api/v1/wallets/{address}/did-document` | DID document JSON |
| `wallet gap-status <address>` | — | `GetWalletGapStatusAsync` → `GET …/gap-status` | `GapStatusResponse` |
| `wallet accounts <address>` | — | `ListWalletAccountsAsync` → `GET …/accounts` | account table |
| `wallet addresses <address>` | — | `ListWalletAddressesAsync` → `GET …/addresses` | `AddressListResponse` table |
| `wallet delegations <address>` | — | `ListWalletDelegationsAsync` → `GET …/delegations` | `WalletAccessDto[]` table |

All read-only; 404 on unknown wallet. Exact response fields confirmed from `WalletEndpoints.cs`/`DelegationEndpoints.cs` at task time.

## US6 — `sorcha system-register …` (governance, extends existing genesis surface)

| Command | Args/Options | Method + Endpoint | Output |
|---------|--------------|-------------------|--------|
| `system-register initialize` | — | `InitializeSystemRegisterAsync` → `POST /api/system-register/initialize` | `{ message, status }` |
| `system-register publish` | `--blueprint <file.json>` (req), `--blueprint-id <id>` (req), `--previous-tx <id>` (opt) | `PublishSystemBlueprintAsync` → `POST /api/system-register/publish` (201) | `PublishBlueprintResponse` |
| `system-register classify-change <blueprintId>` | `--blueprint <file.json>` (req) | `ClassifySystemBlueprintChangeAsync` → `POST /api/system-register/blueprints/{blueprintId}/classify-change` | `{ changeType: structural\|documentation, ... }` |
| `system-register versions <blueprintId>` | — | `GetSystemBlueprintVersionsAsync` → `GET …/blueprints/{blueprintId}/versions` | versions table |

`publish` reads a blueprint JSON file (Constitution VI). 3 on authorisation failure (SystemAdmin).

## US7 — `sorcha device …` (citizen device admin, NEW command file)

| Command | Args | Method + Endpoint | Output |
|---------|------|-------------------|--------|
| `device list` | — | `ListMyDevicesAsync` → `GET /api/v1/me/devices` | `DeviceSummary[]` (active + revoked) |
| `device revoke <deviceId>` | — | `RevokeMyDeviceAsync` → `DELETE /api/v1/me/devices/{deviceId}` (204) | confirmation; 404 indistinguishable from non-existence |

Scoped to the signed-in account's own devices. `revoke` is destructive → requires explicit `<deviceId>`.

## US8 — `sorcha auth …` (token/org automation, extends existing auth command)

| Command | Args | Method + Endpoint | Output / Side-effect |
|---------|------|-------------------|----------------------|
| `auth orgs` | — | `ListMyOrganizationsAsync` → `GET /api/auth/me/organizations` | `OrgMembershipEntry[]`, marking `isCurrent` |
| `auth switch-org <organizationId>` | — | `SwitchOrgAsync` → `POST /api/auth/switch-org` | re-issued `TokenResponse` — **persist to token cache** (R-006) so later commands use the new org |
| `auth introspect` | `--token <jwt>` (opt, default current cached token) | `IntrospectTokenAsync` → `POST /api/auth/token/introspect` | `TokenIntrospectionResponse` (claims) |

No `auth refresh` command (FR-022 — refresh is transparent). Check shared `ITokenIntrospectionClient` for reuse before adding a CLI Refit method.

## US9 — `sorcha trust …` (trust-anchor administration, NEW command file — corrected scope R-003)

| Command | Args/Options | Endpoint | Output |
|---------|--------------|----------|--------|
| `trust anchor provision <tenantId>` | — | `POST /api/v1/trust/tenants/{tenantId}/provision` | trust anchor created |
| `trust anchor get <tenantId>` | — | `GET /api/v1/trust/tenants/{tenantId}/trust-anchor` | trust anchor |
| `trust org enrol <tenantId> <orgWalletAddress>` | — | `POST /api/v1/trust/tenants/{tenantId}/orgs/{orgWalletAddress}/enrol` | enrolment result |
| `trust org cert-chain <tenantId> <orgWalletAddress>` | — | `GET …/orgs/{orgWalletAddress}/cert-chain` | cert chain |
| `trust org revoke <tenantId> <orgWalletAddress>` | `--reason <text>` (opt) | `POST …/orgs/{orgWalletAddress}/revoke` | revocation result |
| `trust crl <tenantId>` | — | `GET /api/v1/trust/tenants/{tenantId}/crl` | certificate revocation list |

`org revoke` is destructive → requires explicit tenant + org. Check shared `IOrgCertChainProvider` (`Sorcha.ServiceClients.Http/Trust/`) for reuse before adding CLI Refit methods. 3 on authorisation failure.
