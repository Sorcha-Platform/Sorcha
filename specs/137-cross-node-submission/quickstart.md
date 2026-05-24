# Quickstart & verification: Cross-node submission round-trip (Stage 5)

Two tiers, per the design's verification strategy. **Tier 1** runs on this (build) machine and gates
the work. **Tier 2** is the live n1↔local round-trip, deferred to the machine holding
`genesis-validator-key.json` + n1 SSH, but its procedure is authored and committed here so the run
is turn-key.

## Tier 1 — build-machine gate (unit + single-node integration)

Run from repo root.

```powershell
dotnet build
dotnet test
```

### Unit coverage to add (per component)

- **C1**: `CreateInstance` resolves a published-only blueprint (draft store empty) → instance created, no 400. Replica relationship (`IsOwner=false`) → `PublishBlueprintToRegisterAsync` NOT called; owner (`IsOwner=true`) → called.
- **C2**: `BlueprintRecoveryService` on a `register:created` event recovers exactly that register's published blueprints; Redis-unavailable path logs and degrades; periodic safety net still recovers a missed register.
- **C3 (field)**: `FormSchemaService` maps `format:"sorcha-holder-key"` → `ControlTypes.HolderKey`; `HolderKeyRenderer` writes the three nested pointers from a stubbed holder-keys response; `x-holder-key` passes validation.
- **C3 (issuance)**: recipient-key precedence — published record present → used; absent + carried key present → carried used; both absent → **fail closed** (no credential). `cnf` is set on the SD-JWT when `holderJwk` is supplied.
- **C5**: with `NextActionId` populated, the mirror seeds `CurrentActionIds` with the next action; a submission against a mirror advances via `UpdateMirrorAsync` and does NOT throw the read-only guard; the authoritative projection emits `NextActionId` (regression test on `DocketBuildTriggerService`).

### Single-node integration

- Create an instance from a published-only blueprint and submit a starting action end-to-end on one node.
- Issue a credential to a recipient supplied **by public key** (no local wallet row) and confirm the on-register envelope is wrapped to that key and decryptable; confirm `cnf` binds the holder JWK.

**Gate (SC-005)**: all of the above green, and this `quickstart.md` + the AssuredIdentity scripted procedure committed.

## Tier 2 — cross-node live round-trip (separate machine)

Prerequisites (see the state doc `docs/superpowers/specs/2026-05-23-cross-node-federation-state-and-gaps.md`):
`genesis-validator-key.json` copied out-of-band; n1 SSH reachable (update `AllowSSH` NSG for this
machine's IP; n1 auto-shuts ~23:00 GMT); local stack via
`docker compose -f docker-compose.yml -f docker-compose.sync-from-n1.yml up -d`.

### Procedure (scripted via the AssuredIdentity walkthrough)

1. **Bring up** n1 (owner/validator, Auto seed) and local (SyncOnly replica). Confirm the AssuredIdentity register replicates and is queryable on local.
2. **C2 check** — subscribe the register *after* local's blueprint-service has booted; confirm its blueprint becomes usable within 30s with **no restart** (SC-003).
3. **Citizen submit (US1)** — sign in as the dev citizen on local (per the state doc's dev account), open the AssuredIdentity application, submit the starting action. The `sorcha-holder-key` field auto-fills (no manual entry). Confirm a sealed docket containing the tx appears on n1 and replicates back (SC-002, FR-005/006/007).
4. **Analyst approve (US2)** — as the verification-analyst on n1, submit the approval against the mirror instance (C5). Confirm it succeeds (no read-only-guard / not-current-action error).
5. **Credential issuance + delivery** — confirm the `AssuredIdentityCredential` is issued bound to the citizen's holder key, encrypted to their key, and **appears in the citizen's local PWA wallet** automatically (FR-013/014/015).
6. **Fail-closed check (FR-012)** — a variant where neither a published record nor carried keys resolve issues **no** credential.

**Gate (SC-001)**: steps 3-5 complete in a single run with **zero** manual service restarts and **zero** manual key entry.

### Notes

- Do NOT change validator keys; reuse `genesis-validator-key.json` (n1 = Auto owner, local = SyncOnly replica).
- The `BatchPublicKeyResolutionTests` CI red is a known env-flake (passes locally); claude-review + per-service Build jobs are the real gates.
- Branch + PR per change; merge on green.
