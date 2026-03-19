# API Contract Changes: 065 Participant Resolution & Field-Level Encryption

## Modified Endpoints

### Register Service

#### POST /api/registers/initiate

**Change**: Add optional `devMode` field to request body.

```json
{
  "name": "Council Services Register",
  "description": "...",
  "tenantId": "...",
  "devMode": true,
  "owners": [...]
}
```

**Default**: `devMode: false` (encrypted mode).

#### PUT /api/registers/{registerId}/devmode

**New endpoint**: Toggle DevMode setting on an existing register.

```json
// Request
{ "enabled": false }

// Response 200
{ "registerId": "...", "devMode": false, "effectiveFrom": "2026-03-19T12:00:00Z" }
```

**Authorization**: Register owner or SystemAdmin.

### Validator Service

#### POST /api/v1/transactions/validate

**No API change**. Internal validation logic changes:

- VAL_BP_002 resolution now checks instance bindings and register participant index
- Starting actions skip sender wallet validation (any wallet accepted)

### Blueprint Service

#### POST /api/instances

**Change**: Response includes participant binding info.

```json
// Response 201 (existing fields + new)
{
  "id": "...",
  "blueprintId": "...",
  "participantBindings": {}
}
```

#### POST /api/instances/{instanceId}/actions/{actionId}/execute

**No API change**. Internal behaviour changes:

- Starting action: binds sender wallet to participant in `ParticipantWallets`
- DevMode register: skips encryption pipeline, stores plaintext
- Non-DevMode: runs encryption pipeline with disclosure groups

#### GET /api/instances/{instanceId}/actions/{actionId}

**Change**: Response payload content depends on DevMode and caller's wallet identity:

- **DevMode**: Disclosure rules applied at read time — filters plaintext JSON to only the fields the caller's wallet is disclosed for. Caller identified by JWT `sub` claim → wallet lookup.
- **Encrypted**: Locates the caller's wrapped key in the encrypted payload groups, decrypts the symmetric key, decrypts the ciphertext, returns only the disclosed fields. Returns empty payload if caller has no wrapped key (not a disclosed recipient).
- **Auth**: Requires authenticated user. Caller's wallet resolved from JWT identity → instance participant bindings or published participant records.

### Register Service — Participant Resolution

#### GET /api/registers/{registerId}/participants/resolve

**New endpoint**: Resolve participant by blueprint role ID and organisation for a register.

```json
// Request (query params)
?participantId=id-dept&organisationName=Ashwick+Council

// Response 200
{
  "participantId": "id-dept",
  "participantName": "Identity Department",
  "organisationName": "Ashwick Council",
  "status": "Active",
  "addresses": [
    { "walletAddress": "ws11q...", "publicKey": "...", "algorithm": "ED25519", "primary": true },
    { "walletAddress": "ws11q...", "publicKey": "...", "algorithm": "ED25519", "primary": false }
  ]
}

// Response 404
{ "error": "No published participant record found" }
```

## No Changes Required

| Endpoint | Why |
|----------|-----|
| POST /api/registers/finalize | DevMode set at initiate, no finalize change |
| POST /api/blueprints | Participant model change is optional field removal |
| GET /api/blueprints/{id} | Returns participant with or without walletAddress |
| POST /api/v1/wallets/{address}/sign | Signing unchanged |
| Participant identity endpoints | Publishing unchanged — records already support multiple addresses |
