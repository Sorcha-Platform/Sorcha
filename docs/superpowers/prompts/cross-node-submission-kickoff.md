# Kickoff prompt — design the cross-node submission round-trip ("Stage 5")

Paste the block below into a fresh session (from the repo root). It is **brainstorm-first**: settle
the correct architecture before any code, so we specify and build once instead of applying point-fixes.

Portable context lives in source control:
- State & gap analysis: `docs/superpowers/specs/2026-05-23-cross-node-federation-state-and-gaps.md`
- This prompt: `docs/superpowers/prompts/cross-node-submission-kickoff.md`

> **Before you start on a new machine:** copy `genesis-validator-key.json` to the repo root
> out-of-band (it is gitignored — holds the validator mnemonic), and update the n1 `AllowSSH` NSG
> rule for this machine's public IP. The local node's installation name comes from `INSTALLATION_NAME`
> (default `localhost`) — it will differ from the previous box; that's fine, the design question is
> structural (local install ≠ n1 install), not the literal name.

---

````
# Design the cross-node submission round-trip ("Stage 5") — brainstorm FIRST, then spec, then build

## Step 0 — load skills before doing anything (Skill tool), in this order
1. `superpowers:brainstorming` — **the point of this session. Do NOT write code or apply point-fixes. Brainstorm and fully discuss the correct design first.**
2. `sorcha-architecture` (F108 ownership-agnostic submission, F099 genesis, register replication, F114 credential delivery), `jwt` (F136 tiered audiences — installation = trust boundary), `network-bootstrap`, `n1-deploy`, `blueprint-builder`, `walkthrough-builder`.
3. Keep `superpowers:systematic-debugging` + `grpc`/`signalr`/`mongodb`/`redis` for later.
For "properly specify and build" use spec-kit (`/speckit.specify` → `/speckit.plan` → `/speckit.tasks`, producing `specs/{id}-{slug}/`), and ONLY after the design is agreed.

Then read `docs/superpowers/specs/2026-05-23-cross-node-federation-state-and-gaps.md` — the full state, the verified read-path, the precise Stage-5 gaps, IDs, creds, and operational notes.

## What already works (verified live n1↔local — do NOT re-debug)
- Topology: n1.sorcha.dev = Auto seed + register OWNER/validator; local Docker = SyncOnly REPLICA via `docker-compose.sync-from-n1.yml`. Installation names differ per node (trust boundary — see below).
- PR #828 (merged): system-register genesis federates byte-for-byte. PR #829 (merged): create-on-sync for regular registers (assured-identity `deccbf4dc9ad4edebe5d6a3651da80b9` replicates n1→local, queryable). The READ/replication path is solid.

## The goal (Stage 5, NOT working)
Citizen submits on LOCAL → reaches n1 → n1 validates/seals → verification-analyst agent on n1 approves → AssuredIdentityCredential issued → returns to the citizen.

## Precisely where it breaks (from the last probe — see the state doc for detail)
1. blueprint-service recovery is one-shot at startup (`BlueprintRecoveryService.ExecuteAsync`) — a node subscribing after boot doesn't materialise the register's blueprints until restart.
2. THE wall: `Sorcha.Blueprint.Service/Program.cs` CreateInstance (~1873) resolves the blueprint via `IBlueprintStore` (draft store); recovery only loads `IPublishedBlueprintStore`. Replicas have only the published store → `POST /instances` 400 "Blueprint not found". Citizen can't start the workflow on local.
3. Untested behind the wall: F108 peer fan-out on submit (`ActionExecutionService` → `IPeerServiceClient.DistributeTransactionAsync`), n1 sealing a local-origin tx, cross-node participant late-binding/identity, credential delivery to a local citizen wallet.

## The task: brainstorm the CORRECT design before building
Stop applying point-fixes; establish the right architecture so we specify and build once. Challenge assumptions; resolve at least:
- **Trust-domain model (foundational, decide first):** F136 made *installation* the JWT trust boundary, yet our two nodes are different installations. Is federation meant to be MULTIPLE NODES WITHIN ONE installation/trust domain (shared issuer, distinct node identities) or ACROSS separate installations (register-level trust via control-record attestations)? This reshapes everything below.
- **Identity & participant resolution across nodes:** how does a citizen who authenticated on local become a valid late-bound participant on n1's register — whose keys/issuer, how does n1 verify the signature and bind?
- **Blueprint availability on replicas:** unify blueprint resolution so instance/action creation is published-store-aware; make recovery event-driven on register replication rather than one-shot.
- **Submission fan-out (F108):** confirm the intended path and what must hold for n1 to accept a local-origin tx.
- **Credential issuance delivery back to a local citizen wallet** (SorchaLocalWallet/HAIP) across nodes.
- **Minimal correct "network" definition:** which existing pieces (F099/F108/F086 validator roster/F136) already cover parts vs what's genuinely missing.

Produce a design doc at `docs/superpowers/specs/<date>-cross-node-submission-design.md` (decisions, chosen trust model, component-by-component plan, open risks). After I confirm it, drive `/speckit.specify` → `/speckit.plan` → `/speckit.tasks`. Do NOT implement until the design is confirmed.

## Guardrails
- Do NOT change validator keys; reuse `genesis-validator-key.json` (gitignored — must be copied to this machine out-of-band). n1 = Auto owner, local = SyncOnly replica.
- Branch + PR per change (never push master); merge on green (claude-review pass; the `BatchPublicKeyResolutionTests` CI red is a known env-flake — passes locally 308/0).
- `az` CLI has a TLS issue from this shell — use the SSH manual path for n1 (see n1-deploy skill). n1 auto-shuts ~23:00 GMT; update its `AllowSSH` NSG rule for this machine's IP.
- Dev creds + service-token + subscribe procedure are in the state doc. Local install name = `INSTALLATION_NAME` (derive the actual value from a local token).
- Brainstorm-first. Resist "just fix instance creation" — that's the point-fix pattern we're stepping back from.
````
