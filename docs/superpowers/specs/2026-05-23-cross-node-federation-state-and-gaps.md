# Cross-node federation — state & gap analysis (2026-05-23)

Source-controlled record of the n1↔local register-federation work, so it survives across
machines and fresh sessions (the equivalent notes previously lived only in machine-local
Claude memory). Companion kickoff prompt: `docs/superpowers/prompts/cross-node-submission-kickoff.md`.

## Topology

- **n1.sorcha.dev** — Azure VM, `Auto` seed, holds the genesis, is the **owner + validator** of
  registers created on it. Installation name `n1.sorcha.dev` → JWT issuer `urn:sorcha:n1.sorcha.dev`,
  audiences `n1.sorcha.dev:{consumer|platform|service|enrol-session}`.
- **local Docker** — `SyncOnly` **replica** brought up with
  `docker compose -f docker-compose.yml -f docker-compose.sync-from-n1.yml up -d`. It runs its OWN
  tenant bootstrap (own admin + orgs). Only the **register layer** replicates from n1.
- **Installation names differ by node.** `docker-compose.yml` sets
  `JwtSettings__InstallationName: ${INSTALLATION_NAME:-localhost}` — the local node's install name is
  whatever `INSTALLATION_NAME` resolves to on that host (it was `phaethon` on the original dev box;
  it will be different on another machine). **This divergence is the crux of the Stage-5 design
  question** — post-F136, *installation* is the JWT trust boundary, so the two nodes are currently
  separate trust domains.

## What works — verified live (do NOT re-debug)

The **read / replication path is solid**:

- **System register** federates n1→local, byte-for-byte (id `aebf26362e079087571ac0932d4db973`,
  matching genesis-docket hash + height). Fixed in **PR #828**.
- **Regular registers** (e.g. assured-identity `deccbf4dc9ad4edebe5d6a3651da80b9`) replicate n1→local,
  queryable, matching dockets/height; the register row is created on the replica from the synced
  genesis. Fixed in **PR #829**.

### PR #828 — system-register genesis federation (merged)
Two validator-side bugs broke the genesis trust anchor on a SyncOnly node (transport itself was
fine):
1. `VAL_TIME_002` (transaction-freshness, max age 1h) rejected the **pre-signed genesis transaction**
   (a ceremony artifact with a fixed timestamp, ingested days later) → `GenesisManager` sealed the
   genesis docket **empty** → the trust anchor lived only in the seed's local
   `Register.InitialControlRecord` row field, which peer-sync does not transfer. Fix:
   `TransactionTypeClassifier.IsGenesisTransaction` exempts the genesis tx from `VAL_TIME_002`.
2. The genesis payload was `\u`-re-escaped in the Redis mempool (`RedisVerifiedTransactionQueue`,
   `MemPoolManager` used the default JSON encoder; an em-dash in the control record became `—`),
   so `SHA256(sealed payload) ≠ signed payloadHash` and the sync verifier rejected it. Fix: both use
   `UnsafeRelaxedJsonEscaping` (matching `TransactionPoolPoller`, which already did).

### PR #829 — create-on-sync for regular registers (merged)
The WriteDocket handler (`Sorcha.Register.Service/Program.cs`) auto-create-on-genesis was hardcoded
to the system-register id, so any regular register's genesis docket 404'd on a replica. Fix:
generalised to any genesis docket; for non-system registers, derive name/description from the genesis
Control transaction via `GenesisControlRecordExtractor` and create as full-replica + advertised.

## The Stage-5 goal (NOT working — design target)

Citizen submits on the **local** node → submission reaches n1 → n1 validates/seals → the
verification-analyst agent on n1 approves → an AssuredIdentityCredential is issued → it gets back to
the requesting citizen.

## Precise gaps found by empirical probe (2026-05-23)

The **write/issue round-trip is blocked at the first hop**. A local citizen (local identity + wallet)
could not even create a workflow instance on the replica:

1. **blueprint-service recovery is one-shot at startup.** `BlueprintRecoveryService.ExecuteAsync` runs
   `RunRecoveryAsync` once. `DiscoverRegistersAsync` → `IRegisterServiceClient.GetInternalRegistersAsync`
   does enumerate all local registers (so it WOULD find a replicated register), but a node that
   subscribes *after* boot won't materialise the register's blueprints until blueprint-service
   restarts. (Workaround used in the probe: restart blueprint-service → it logged
   "Recovered 2 published blueprints from register deccbf4d… (Assured Identity Register)".)
2. **Instance creation can't see replicated blueprints — THE wall.** `Program.cs` `CreateInstance`
   (~line 1873) resolves the blueprint via `IBlueprintStore.GetAsync` (the **draft/editable** store).
   Recovery loads replicated blueprints into `IPublishedBlueprintStore` (the **published** store). On
   the owner (n1) the blueprint is in both; on a **replica only the published store** has it →
   `POST /api/instances/` returns **400 "Blueprint not found"**. The citizen cannot start the
   workflow on local. (Also `CreateInstance` ~line 1890 calls `PublishBlueprintToRegisterAsync` on the
   register — questionable on a non-owned replicated register.)
3. **Untested, blocked behind #2** — F108 peer fan-out on submit
   (`ActionExecutionService` → `IPeerServiceClient.DistributeTransactionAsync`; a
   `BaseAddress must be set` warning was seen single-node); n1 validating/sealing a **local-origin**
   transaction; cross-node participant late-binding / identity resolution across installations;
   credential issuance delivery back to a **local** citizen wallet.

**Verdict:** these are fixable, not fundamental — but they are genuinely new cross-node plumbing, and
the trust-domain model (one installation vs many) must be settled before building, or we will keep
applying point-fixes. That is the purpose of the kickoff prompt.

## Operational notes (for reproducing on a new machine)

- **`genesis-validator-key.json` is gitignored (it holds the validator mnemonic).** It will NOT
  transfer via git — copy it to the new machine out-of-band before any n1 reset. Reuse it; never
  regenerate (it matches the committed embedded genesis roster:
  `ws11qzamquj62vk5…` / pubkey `u7ByW…` / fingerprint `6e6ec9f0…`).
- **n1 access:** SSH `<ssh-user>@<n1-host>` (NSG is IP-restricted — update `AllowSSH` source for the
  new machine's public IP). The `az` CLI has a TLS-trust issue from these shells, so use the SSH
  manual path for n1 resets (see the `n1-deploy` / `network-bootstrap` skills). n1 auto-shuts ~23:00 GMT.
- **n1 genesis-clean reset (SSH path):** scp `genesis-validator-key.json`→`/tmp/gvk.json`; `down -v`;
  `docker volume create sorcha_wallet-encryption-keys` + chown `1654:1654`; `up -d wallet-service`
  alone; wait healthy; one-shot `curlimages/curl` POST the mnemonic to
  `/api/v1/wallets/system/recover` (`validatorId: local-validator`, `algorithm: ED25519`); shred;
  `up -d` the rest; then `bootstrap`.
- **Credentials (dev):** n1 admin `admin@sorcha.local` / `Dev_Pass_2025!`; local admin
  `admin@local.node` / `Dev_Pass_2025!`. Peer-service `RequireService` calls need a service token —
  `client_credentials` grant at `/api/service-auth/token` with `register-service` /
  `register-service-secret` (form-encoded: `grant_type`, `client_id`, `client_secret`, `scope`).
- **Subscribe a replica to a register** (peer-service, `RequireService`):
  `POST {peer}/api/registers/{id}/subscribe` `{"mode":"full-replica"}`; poll
  `{peer}/api/registers/subscriptions` for `FullyReplicated`. A persisted subscription resumes
  *live-only* after restart — to force a fresh full pull you must **unsubscribe + resubscribe**
  (`DELETE` then `POST`), which resets `LastSyncedDocketVersion` to -1.
- **CI:** `build-and-test` runs on PRs; the `Sorcha.Register.Service.Tests.BatchPublicKeyResolutionTests`
  failures are a CI-env flake (`SystemWalletSigning:ValidatorId configuration is required`) — they
  pass locally (308/0). claude-review + the per-service Build jobs are the real gates.

## Stage-5 implementation progress (2026-05-24)

The gaps above were turned into Feature 137 (`specs/137-cross-node-submission/`, design at
`docs/superpowers/specs/2026-05-23-cross-node-submission-design.md`). Trust model decided:
**separate installations bridged only at the ledger plane** (genesis validator roster + tx/docket
signatures); the F136 JWT installation boundary is untouched. **Four of five components are merged
to master:**

- **C5** (PR #831) — cross-node mirror submission. The analyst can act on a register-owning node
  against a read-only mirror. `nextActionId` is carried in submission metadata and projected onto
  `TransactionMetaData.NextActionId`; the mirror seeds `CurrentActionIds`; mirror advances go
  through `UpdateMirrorAsync`.
- **C1 + C4** (PR #832, US1) — `CreateInstance` is published-store-aware (replicas resolve the
  replicated blueprint), publish-to-register gated on `IsOwner`, typed 409 when still syncing;
  `docker-compose` blueprint-service gained `ServiceClients__PeerService__{Address,HttpAddress}` so
  F108 fan-out reaches the peer.
- **C2** (PR #833, US3) — `BlueprintRecoveryService` subscribes to `register:created` for immediate
  per-register blueprint recovery (no restart); periodic loop is the safety net.

**Remaining: C3 / US2 — credential delivery** (bind the issued credential to the citizen's holder
key + deliver to the local wallet). The SD-JWT `cnf` binding is a pre-existing hole; the PWA
application-submission surface is a stub; X25519 is derivable from the Ed25519 signing key and the
AEAD `ExternalRecipientKeys` "supply explicitly" path already exists. Execute via the kickoff prompt
`docs/superpowers/prompts/137-c3-credential-delivery-kickoff.md` (one big PR is fine). The live
n1↔local round-trip (SC-001) is the Tier-2 gate on the genesis-key machine.

## Deferred / backlog already captured

- `.specify/tasks/deferred-tasks.md` → **MCP-101/102/103**: review MCP capabilities — the admin slice
  is observational only (no register-control/sync tools), so an agent can watch federation but not
  drive it.
