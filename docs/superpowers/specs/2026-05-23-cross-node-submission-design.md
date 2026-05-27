# Cross-node submission round-trip ("Stage 5") — design (2026-05-23)

Design for the cross-node citizen-submission → credential round-trip. Companion to the
state/gap analysis at `docs/superpowers/specs/2026-05-23-cross-node-federation-state-and-gaps.md`
(read that first for the verified read-path and the empirical gap probe).

**Status:** design agreed (brainstorm). Next: `/speckit.specify` → `/speckit.plan` → `/speckit.tasks`.

---

## Goal

A citizen submits on the **local** node → the submission reaches **n1** → n1 validates/seals →
the verification-analyst agent on n1 approves → an `AssuredIdentityCredential` is issued → it is
delivered back to the requesting citizen's **local** wallet.

The read/replication path (system register + regular registers, n1→local) is already solid
(PR #828, PR #829). This design closes the **write/issue round-trip**, which the empirical probe
showed is blocked at the first hop.

---

## 1. Trust model & the two planes (foundation, decided first)

Federation is **across separate installations, bridged at the ledger plane** — *not* one
installation spanning many nodes. Sorcha already runs two independent trust planes, and we keep
them independent:

- **Identity plane (F136 JWT).** Per-installation. `n1.sorcha.dev` and `local` each keep their own
  issuer / audiences / signing key. Tokens are **never** portable across nodes. This design does
  not touch it.
- **Ledger plane.** Cross-node. Trust is *cryptographic*: a docket is trusted because its signature
  matches a key in the **genesis-anchored validator roster** (F086); a transaction is trusted
  because its signature verifies and it obeys chain/route rules. The peer transport tolerates
  anonymous peers (FR-014) precisely because it is **not** the trust boundary — the roster is.
  (Verified: `PeerAuthInterceptor` validates with the shared signing key, `ValidateAudience=false`,
  and falls back to a lower-trust "anonymous" flag rather than rejecting cross-installation calls.)

### Minimal "network" definition

A set of installations, each its own identity domain, that share one or more registers via peer
subscription, with cross-node trust anchored **solely** by the genesis validator roster +
transaction/docket signatures. No shared user directory, no cross-installation JWT, no online
callback to the originating node.

### Why this is the right model

The ledger plane *already* federates across installations and works (read path verified). Trying to
merge the nodes into one JWT trust domain would fight F136, couple the nodes' auth, and break the
n1(`Auto` owner)/local(`SyncOnly` replica) divergence we rely on. Everything cross-node must reduce
to ledger-native crypto facts (a signature verifies; a key is published on a replicated register).
That constraint is healthy: it means n1 may resolve the local citizen's **keys** without ever
resolving their **identity**.

---

## 2. What already exists (do NOT rebuild)

The cross-node *workflow* and *trust* machinery is largely present. The remaining gaps are small and
well-bounded, not a missing subsystem.

- **Cross-node trust** — genesis validator roster (F086), anonymous-tolerant peer transport,
  byte-faithful replication (PR #828/#829).
- **Cross-node instance materialisation** — `InstanceMirrorReconstructor` (F106 Wave D) subscribes
  to `docket:confirmed`, walks each sealed docket's transactions, and upserts a **read-only mirror**
  Instance row whenever a participant wallet is local to that node. The register is the source of
  truth; instances are derived per-node. The shared `instanceId` travels in the tx metadata, so both
  nodes key the same instance.
- **Register-as-source-of-truth execution** — `ActionExecutionService` loads a local Instance *row*
  but reconstructs **accumulated state from the register** via
  `StateReconstructionService.ReconstructAsync`. So n1 needs only the mirror row + replicated
  transactions to let the analyst submit the next action.
- **Ownership-agnostic submission (F108)** — `ActionExecutionService` already fans a submission out
  to both the local validator mempool and `IPeerServiceClient.DistributeTransactionAsync`; the
  receiving peer hands it to its local validator, which seals iff it is on the roster.
- **Credential delivery to a local wallet (F114)** — `InboundCredentialDetector` decrypts the
  AEAD envelope with the recipient wallet's X25519 key, `CitizenInboxProjector` pushes
  `WalletHub.CredentialAvailable`, the PWA shows it.

---

## 3. The gaps (what this design closes)

| # | Gap | Plane |
|---|-----|-------|
| 1 | Instance creation resolves the blueprint via the **draft** store (`IBlueprintStore`); replicas only have the **published** store → `400 "Blueprint not found"`. **The wall.** | local |
| 2 | Blueprint recovery is **one-shot at boot** (`BlueprintRecoveryService.ExecuteAsync`); a register subscribed-to *after* boot never materialises its blueprints until restart. | local |
| 3 | `CreateInstance` calls `PublishBlueprintToRegisterAsync` — wrong on a non-owned replicated register. | local |
| 4 | n1 cannot resolve an **open/public-user** recipient's delivery keys (holder JWK + X25519) → cannot bind/wrap the credential. | ledger/identity |
| 5 | F108 fan-out `IPeerServiceClient` had a `BaseAddress must be set` warning — config, not architecture. | transport |

---

## 4. Approach: targeted root-cause (Approach B)

Fix the *causes*, not the symptoms, without a speculative rewrite. (Approach A — minimal point-fixes
— was rejected as the very pattern we're stepping back from; Approach C — register-native execution
refactor — was rejected as over-scoped/risky for proving Stage 5.)

### Component-by-component plan

| Component | Change | Source touch-points |
|-----------|--------|---------------------|
| **C1 — Unified blueprint resolution** | Instance/action creation resolves the blueprint **published-store-aware** (draft → published fallback), so replicas and owners share one path. Gate `PublishBlueprintToRegisterAsync` on **node-is-owner** (derived F108 relationship) so replicas never re-publish. | `Blueprint.Service/Program.cs` CreateInstance (~1873, ~1890); `IBlueprintStore` / `IPublishedBlueprintStore`; F108 `IRegisterLocalRelationshipService` |
| **C2 — Event-driven recovery** | `BlueprintRecoveryService` subscribes to the register-replication/creation signal and recovers a newly-synced register's published blueprints **immediately**; periodic re-scan kept as a safety net. | `BlueprintRecoveryService`; the create-on-sync path from PR #829; Redis pub/sub channel (exact channel resolved in research) |
| **C3 — Open-participant key delivery** | New **client-autofilled derived public-key field** (JSON-Schema extension, F085/F092 idiom) carrying holder JWK (slot 108) + wallet X25519 pubkey. n1 resolves recipient delivery keys by **precedence: published participant record → carried key → fail closed**. Wallet-Service issuance accepts a recipient by *supplied public key* (not requiring a local wallet row). | new schema field type + renderer/PWA autofill; `ActionExecutionService` (~1972 recipient resolution); `Wallet.Service/CredentialEndpoints` issuance path |
| **C4 — Fan-out config** | Set `IPeerServiceClient` BaseAddress on the submitting node so F108 `DistributeTransactionAsync` reaches the peer → n1. | Blueprint.Service config / service-client wiring |
| **C5 — Cross-node validation confirmation** | Confirm n1 seals a local-origin starting-action tx (F103 wallet-check skip + signature verifiability) and that the **n1 mirror instance** lets the analyst submit action 2. Add carve-outs only if integration testing exposes a real gap. | `Validator.Service/ValidationEngine`; `InstanceMirrorReconstructor`; `StateReconstructionService` |

---

## 5. The round-trip (data flow)

1. **Citizen authenticates on LOCAL** — `local:consumer` JWT, local HD wallet.
2. **Blueprint resolves on LOCAL** from the published store (replicated from n1), materialised by
   event-driven recovery when the register synced — **C1/C2**.
3. **Citizen submits the starting action on LOCAL.** The form carries the public-key field,
   client-autofilled with derived holder JWK + X25519 — **C3**. The wallet signs (self-describing
   signature). The citizen is late-bound as `applicant` (F103).
4. **F108 fan-out** — local validator (not on the roster → no seal) **+**
   `IPeerServiceClient.DistributeTransactionAsync` → n1 peer-service `SubmitTransaction` — **C4**.
5. **n1 validates & seals** — on the roster; starting-action wallet-check skipped; signature
   verifies cryptographically → docket sealed — **C5**.
6. **Instances materialise both sides** — `docket:confirmed` on n1 → `InstanceMirrorReconstructor`
   upserts the mirror (analyst wallet is n1-local); docket replicates → LOCAL mirrors too (citizen
   wallet local). Shared `instanceId` rides the tx.
7. **Analyst approves on n1** (action 2) — n1 reconstructs accumulated state from the register
   (incl. the citizen payload + carried keys), routes, seals.
8. **Credential issued on n1** (action 3) — recipient keys resolved by precedence (open citizen →
   carried key). SD-JWT bound to the holder JWK (`cnf`); AEAD envelope encrypted to X25519;
   `targetAudience: SorchaLocalWallet` tx sealed → replicates — **C3**.
9. **Credential lands on LOCAL** — `docket:confirmed` → `InboundCredentialDetector` decrypts with
   the citizen's local X25519 key → `CitizenInboxProjector` → `WalletHub.CredentialAvailable` →
   the PWA shows it. **Round-trip complete.**

---

## 6. The one new wire contract — the public-key field

Following the F085 `x-file` / F092 `x-persona` idiom: a blueprint-declared field the renderer
recognises and the client autofills.

```jsonc
// In a starting action's schema
"holderKeys": {
  "type": "object",
  "format": "sorcha-holder-key",        // renderer recognises → client autofills, read-only to the user
  "x-holder-key": { "required": true }
}
```

**Value shape** (client-autofilled; lands in the action payload → replicated register state):

```jsonc
{
  "holderJwk": { "kty": "EC", "crv": "P-256", "x": "…", "y": "…" },  // slot 108, for the SD-JWT cnf binding
  "encryptionPublicKey": "…"                                          // wallet X25519, for the AEAD envelope
}
```

- **Client-autofilled, derived, public-only.** The PWA/renderer reads the *public* halves of keys
  the wallet already derives (F114 derives the slot-108 holder key today). Private keys never leave
  the device/local wallet.
- **Trust-on-submission, not proof-of-possession (v1).** Because the open-participant submitter *is*
  the recipient (late-bound applicant = starting-action sender), there is no adversarial incentive:
  a wrong holder key → an unpresentable credential; a wrong X25519 key → a credential the submitter
  cannot decrypt. Both are self-defeating. The identity collapse in F103's late-binding is what
  removes the attacker. PoP (a key-binding proof) is a hardening backlog item, paired with the
  participant-record promotion (which weakens that guarantee by letting a different actor vouch for
  keys).
- **Research note.** If Sorcha derives the wallet's X25519 key deterministically from its ED25519
  signing key, n1 could derive `encryptionPublicKey` from the tx signature and the field could
  shrink to `holderJwk` only. Confirm during research; this design assumes both are carried.

---

## 7. n1 recipient-key resolution precedence

At credential issuance, `ActionExecutionService` resolves the recipient's delivery keys in order:

1. **Published participant record** on the register (`/participants/by-address/{addr}/public-key`) —
   replicated cross-node, **authoritative**. Covers org field agents (already published) with no
   new plumbing.
2. **Carried key field** from reconstructed instance state — the open/public-user fallback.
3. **Neither** → **fail closed**: do not issue an unbindable/unencryptable credential; surface a
   clear error.

If both 1 and 2 are present, **published wins** (and on conflict, log + use published). This is the
same precedence the backlogged promotion will use when a citizen later becomes published — the
carried key is simply superseded.

---

## 8. Error handling & edge cases

- **Blueprint not yet recovered when the citizen arrives** → C2 makes this rare; on miss, return a
  typed "register still syncing" state (not a bare 400), and the client retries.
- **Replica attempts to seal** → already prevented (F108: subscribers never seal); C1 also stops
  replica re-publish.
- **Fan-out to n1 fails / n1 unreachable** → the tx remains in the local mempool; submission UX
  shows "submitted, awaiting validation"; define bounded retry + an operator-visible signal rather
  than silent loss.
- **Carried key missing/malformed** → issuance fails closed (§7.3).
- **Late outcome / seal ordering** → F119 seal-aware ordering already governs the outcome/advance
  chain; cross-node sealing latency is just a longer wait, handled by the same coordinator.

---

## 9. Verification strategy (two machines)

The code is written **here**; the live n1↔local cross-node test runs on a **different machine** (the
one holding `genesis-validator-key.json`, n1 SSH access, and the `docker-compose.sync-from-n1.yml`
split).

**Buildable & verifiable here (this machine):**

- Unit: blueprint-resolver draft→published precedence; replica-creation gating; key-field autofill +
  payload extraction; n1 recipient-key precedence (published → carried → fail-closed); event-driven
  recovery firing on a simulated register-sync event.
- Single-node integration: instance creation against a published-only blueprint; credential issuance
  to a recipient supplied *by public key* (no local wallet row).

**Deferred to the cross-node machine:**

- The live n1↔local round-trip = the real success criterion (Tier 2 below).
- **Deliverable from this work:** a *scripted, reproducible* verification procedure (extend the
  AssuredIdentity walkthrough + a probe script) so the cross-node run is turn-key when we are on
  that machine — not an ad-hoc manual probe.

---

## 10. Scope, success criteria, backlog, risks

### Success criteria (two tiers)

- **Tier 1 (gate, here):** code complete; all unit + single-node integration green; the scripted
  cross-node verification procedure committed.
- **Tier 2 (cross-node machine):** a citizen on LOCAL completes the AssuredIdentity application →
  the analyst on n1 approves → the `AssuredIdentityCredential` appears in the citizen's LOCAL PWA
  wallet — with **no manual blueprint-service restart** and **no manual key entry**.

### Out of scope / backlog

- **Participant-record promotion** (durable cross-node identity; field-agent use-or-supersede) —
  **paired with PoP hardening** on the key field. *Motivation: org field agents are likely already
  PublishedParticipants in real deployments, so those records must be used / superseded.*
- Proof-of-possession on the key field (until promotion lands).
- mdoc / non-SD-JWT formats cross-node; multi-register; >2 nodes (the design must not *preclude*
  these, but will not test them).
- MCP register-control/sync tools (the deferred MCP-101/102/103 backlog — the admin slice is
  observational only today).

### Risks (verify early in build)

1. **Mirror-instance sufficiency** — does the analyst actually submit action 2 against the n1 mirror
   + register reconstruction? Highest-value early integration check.
2. **Recovery event channel** — pick the right signal (reuse PR #829's create-on-sync path vs. a
   dedicated channel).
3. **Wallet-Service issuance to a non-local recipient by supplied key** — may need a distinct
   issuance code path (today it does `GetByAddressAsync`).
4. **Holder-JWK format compatibility** — the carried JWK must match what `InboundCredentialDetector`
   / the PWA expect for `cnf`.

---

## Decisions log

| # | Decision | Rationale |
|---|----------|-----------|
| D1 | Two-plane trust model; federation = separate installations bridged at the ledger plane | The ledger plane already federates cross-installation and works; merging into one JWT domain fights F136 and couples node auth |
| D2 | Open-participant delivery keys carried in the submission via a client-autofilled, derived public-key field | Fits the F085/F092 schema-extension idiom; no participant record needed; HD-derived public keys, private keys never leave the device |
| D3 | v1 = carry-in-submission only; participant-record promotion backlogged | Smallest path that proves Stage 5 end-to-end; promotion is designed-for via the reusable field type |
| D4 | n1 recipient-key resolution precedence: published record → carried key → fail closed | Org field agents (published) work with no new plumbing; open users fall back to the carried key; never issue an unusable credential |
| D5 | Trust-on-submission (no PoP) for v1 | F103 late-binding collapses submitter and recipient into one wallet, removing the attacker |
| D6 | Approach B (targeted root-cause) over A (point-fixes) or C (register-native rewrite) | Fixes causes (blueprint resolution, recovery, replica semantics) without a speculative rewrite |
| D7 | Code written here; cross-node round-trip verified on a separate machine, via a committed scripted procedure | The genesis key + n1 access + sync-split live on a different machine |

---

## C3 implementation reconciliation (Stage 5, 2026-05-24)

Two research findings (`specs/137-cross-node-submission/research.md` § "Design corrections") expanded C3 beyond the original sketch; both are now implemented:

1. **`cnf` binding was a pre-existing hole.** Credentials were issued *unbound*. C3 adds `IssueCredentialRequest.HolderJwk` → `SdJwtService.CreateTokenAsync(holderJwk:)`, threaded through `IWalletServiceClient.IssueCredentialAsync` and `CredentialIssuanceConfig.HolderKeySourceField`. Recipient-key precedence (published participant record → carried `holderKeys` field → fail-closed) is enforced in `ActionExecutionService` **before minting** (SC-004: zero credentials when neither resolves; error codes `VAL_RUNTIME_CRED_004`/`005`). The carried `encryptionPublicKey` is injected into the existing `ExternalRecipientKeys` path only when the register lookup misses ("published wins").

2. **Client autofill is a server round-trip; the PWA submit surface was a stub.** C3 adds `GET /api/v1/wallet/holder-keys` (consumer-tier) backed by `IHolderKeyService.GetDeliveryKeysAsync`, a `ControlTypes.HolderKey` field + `HolderKeyRenderer` (autofills the three sibling pointers), and wires `SorchaFormRenderer` into `Sorcha.Wallet.Pwa/Pages/ApplicationInstance.razor` (submit → `FormPayloadBuilder.BuildNested` → `/execute`, server-signed). A defensive `x-*` strip was added to the engine `SchemaValidator` so `x-holder-key` is tolerated on both validation paths.

No change to the agreed trust model, precedence, or v1 scope. The cross-node round-trip is unit + single-node-integration covered (SC-005 Tier-1); the live n1↔local run remains Tier-2 on the genesis-key machine.
