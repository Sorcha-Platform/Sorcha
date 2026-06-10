# F137 — DevMode-in-genesis + federation hardening: redeploy & verify (continuation)

You are continuing Feature 137 cross-node credential delivery work after a context clear.
Load the **`sorcha-architecture`**, **`n1-deploy`**, and **`network-bootstrap`** skills, and read the
memory file `project_feature_137_c3_credential_delivery.md` (it has the full diagnosis + design).

## Where things stand

All code is committed on branch **`fix/137-cross-node-reconstruction-decrypt`**, **PR #840** (open).
It is being merged to master to trigger the **Docker Publish** CI (builds `sorchadev/*:latest`).
If the merge hasn't happened yet: confirm PR #840 `build-and-test` + `claude-review` are green, then
`gh pr merge 840 --squash --delete-branch`, and wait for Docker Publish to finish
(`gh run list --json name,status,conclusion` → the "Docker Publish" run).

### What this PR delivers (all unit-tested, build clean)
1. **Reconstruction decrypts disclosure-group payloads** — `StateReconstructionService.DecryptTransactionPayloadAsync` now decrypts the production `encryptedPayloads[]`/`wrappedKeys` envelope (unwrap via `IWalletServiceClient.DecryptWithDelegationAsync`, then `ISymmetricCrypto`), mirroring `InboundCredentialDetector`. This lets the OWNER read the cross-node citizen's action-1 payload (claims + holder keys) → credential issues. General fix for any cross-node multi-action flow.
2. **Replication: incremental re-pull + seed-node source fallback** — `RegisterSyncBackgroundService` FullyReplicated case re-pulls each periodic pass; `RegisterReplicationService` falls back to `PeerListManager.GetSeedNodes()` when no peer advertised the register. Live-validated local 9→18.
3. **DevMode in genesis `CryptoPolicy.DevMode`** — replicates to every node; replica auto-create reads `controlRecord.CryptoPolicy?.DevMode`. (NOT a top-level field, NOT the `EnforcementMode` rename — that's the algorithm allowlist.)
4. **Genesis 1-hour freshness window** — `ValidationEngineConfiguration.GenesisMaxAge` (default 1h). `ValidateTiming` applies it to genesis (was exempt/"anytime"). A regenerated system register must be minted→embedded→deployed→bootstrapped within the hour. Guards the ingest-and-seal path; sealed-docket pulls verify the sealed docket, not tx age.
5. **Validator one-way DevMode** — `ValidateCryptoPolicyUpdate` rejects any crypto-policy update with `DevMode=true`. Genesis sets it once; updates only promote DevMode→Normal.
6. **Promote-to-Normal replicates** — `/disable-dev-mode` emits a `CryptoPolicyUpdate` control tx; WriteDocket projects a sealed CryptoPolicyUpdate's DevMode→false onto the register record on every node.

Changed services to rebuild/redeploy: **register, validator, blueprint, peer** (wallet unchanged this phase).

### Deployment state at handoff
- **n1** (`<ssh-user>@<n1-host>`, install `n1.sorcha.dev`) and **local** (SyncOnly replica, install `phaethon`) are both still on the PRE-fix images. They MUST be reseeded with the new images.
- The existing AssuredIdentity register `deccbf4dc9ad4edebe5d6a3651da80b9` predates DevMode-in-genesis and has the old genesis — it will be recreated by the reseed.

## The reseed plan (this machine has all the scripts)

> ⚠️ Genesis is now time-boxed to **1 hour** — mint → embed → deploy → bootstrap n1 PROMPTLY.

1. **n1 first** — use the **network-bootstrap** / **n1-deploy** skills + the local scripts to:
   - Wait for Docker Publish `:latest` to be ready.
   - Regenerate the system-register genesis (CLI `sorcha system-register create`), embed/deploy, hard-reset n1 (`n1-reset.ps1` or the manual SSH path), import the genesis validator key BEFORE register/validator start, bootstrap. All within the hour.
   - Recreate the AssuredIdentity register (setup.ps1) — its genesis now carries DevMode. Decide DevMode vs Normal per test.
2. **Then local** — the user will (or you can, with confirmation):
   `docker compose -f docker-compose.yml -f docker-compose.sync-from-n1.yml down -v` → `docker compose ... pull` (or `docker save|ssh load` if testing branch images) → `up -d` → bootstrap local tenant → subscribe to the n1 registers.
   - **Confirm**: local's replicated register record shows the SAME DevMode as n1 (proves #21). Check `GET /api/registers/{id}` → `devMode`.
   - **Confirm**: a late-joining SyncOnly node PULLS the sealed genesis docket (does not re-ingest the embedded genesis through its own validator) — else the 1-hour window would lock it out. If it re-ingests and rejects on age, that's a real issue to fix (the window should guard ingest-and-seal only).

## Remaining tasks
- **#24 (verify)** — the redeploy IS this. Run the cross-node round-trip both ways:
  - DevMode register: credential delivered PLAINTEXT; local replica (now DevMode=true via replication) extracts it via `InboundCredentialDetector`'s plaintext path.
  - Normal register: credential ENCRYPTED to citizen via carried key; local decrypts via fix #1.
  - Helpers: `walkthroughs/AssuredIdentity/run-crossnode-local.ps1 -CitizenEmail <fresh>` (fresh wallet avoids `VAL_REPLAY_002`), `approve-crossnode-n1.ps1`. Analyst on n1: `verification-admin@assured-identity.local` / `Dev_Pass_2025!`, org `c30df28f…`.
- **#26 (skill updates)** — update `network-bootstrap` (+ `n1-deploy`) skills with: the **genesis 1-hour window** requirement (mint→deploy→bootstrap within the hour) and the **DevMode-in-CryptoPolicy** genesis model + one-way transition. The OLD network-bootstrap note that genesis is accepted "anytime" / the VAL_TIME_002 exemption is now WRONG — fix it.
- **resync intermittency** (separate follow-up) — the incremental re-pull doesn't fire every cycle (seed gRPC channel reconnects async on peer restart; if the first ProcessSubscriptions pass beats it, waits a full 5-min cycle). Fix = lazy seed-channel dial OR fold the pull into the 60s relay-poll cadence.

## Known good test creds / IDs
- n1 admin: `admin@sorcha.local` / `Dev_Pass_2025!`. local admin: `admin@local.node` / `Dev_Pass_2025!`.
- local service token (no scope): SP `register-service` / `register-service-secret`.
- DevMode toggle endpoint (authed): `PUT /api/registers/{id}/devmode {"enabled":true}` (manual; the proper path is the control-tx via `/disable-dev-mode`).
