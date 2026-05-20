# Quickstart: Exercising the new CLI commands

Prerequisites: the platform running locally (`docker-compose up -d`) or a profile pointed at n1, and an authenticated CLI session (`sorcha auth login`). Use the local build to avoid a stale global tool:

```powershell
dotnet build src/Apps/Sorcha.Cli -c Release
$cli = "src/Apps/Sorcha.Cli/bin/Release/net10.0/Sorcha.Cli.exe"
```

## Phase 1 smoke walk

### Transaction trust-hardening (US1)

```powershell
# Generate + save an inclusion proof, then verify it offline
& $cli transaction proof <txId> --register <registerId> --out proof.json
& $cli transaction verify-proof --register <registerId> --file proof.json   # expect isValid: true

# Revoke, then confirm lifecycle status flips (this is the fixed status command)
& $cli transaction revoke <txId> --register <registerId> --reason Erroneous
& $cli transaction status <txId> --register <registerId>                     # expect status: Revoked
```

### Register sync diagnostics (US2)

```powershell
& $cli register relationship <registerId>     # owner / validator / subscriber
& $cli register sync-state <registerId>        # Indeterminate | Syncing | CaughtUp | Error
& $cli register sync-health                     # all registers on this node
```

### Validator roster governance (US3)

```powershell
& $cli validator register --register <registerId> --validator-id local-validator `
    --public-key <pk> --grpc-endpoint https://validator:7004
& $cli validator count <registerId>
& $cli validator suspend <registerId> local-validator --reason "maintenance"
& $cli validator reactivate <registerId> local-validator
& $cli validator audit <registerId>            # shows the suspend/reactivate transitions
```

### Org key derivation (US4 — reused shared client)

```powershell
& $cli wallet org-key provision <orgId>        # mnemonic shown ONCE — capture it now
& $cli wallet org-key derive <orgId> --user-id alice --usage Identity
& $cli wallet org-key rotate <orgId> <derivedKeyId>
& $cli wallet org-key revoke <orgId> <derivedKeyId>
```

## Phase 2 smoke walk

```powershell
# Wallet diagnostics (US5)
& $cli wallet did-document <address>
& $cli wallet gap-status <address>

# System register governance (US6)
& $cli system-register publish --blueprint ./bp.json --blueprint-id my-bp
& $cli system-register versions my-bp

# Citizen device admin (US7)
& $cli device list
& $cli device revoke <deviceId>

# Auth/org automation (US8)
& $cli auth orgs
& $cli auth switch-org <organizationId>        # subsequent commands use the new org
& $cli auth introspect                          # claims of the (now-switched) token

# Trust-anchor administration (US9)
& $cli trust anchor get <tenantId>
& $cli trust org cert-chain <tenantId> <orgWalletAddress>
& $cli trust crl <tenantId>
```

## Verifying machine-readable / JSON output

Every command honours global output flags:

```powershell
& $cli register sync-state <registerId> --output json
& $cli validator count <registerId> --machine-readable    # standard automation envelope
```

## What "done" looks like (maps to Success Criteria)

- The transaction lifecycle walk above completes end-to-end, and `transaction status` reports `Revoked` (not a submission ack) — **SC-001, SC-002**.
- `register relationship` + `sync-state` answer node role/sync in one command — **SC-003**.
- The full validator roster lifecycle runs from the CLI — **SC-004**.
- Org keys provision → derive → rotate → revoke scripted with no manual steps — **SC-005**.
- Every command above appears in the CLI command reference and has a test — **SC-006**.
- No org-key DTO is duplicated in the CLI (it injects the shared client) — **SC-007**.
- The out-of-scope list is recorded in the spec — **SC-008**.
