# Phase 0 Research: Validator Exemption Authority

**Feature**: 196 · **Date**: 2026-08-28 · **Read at**: `cfc2e48aa`

All findings below were established by reading the code at the commit above. Where something was
*not* established by execution, it says so.

---

## R1 — Where the genesis trust anchor lives, and how the validator reaches it

**Decision**: Relocate `INodeTrustAnchor` from `Sorcha.Register.Service/Provenance/` into the shared
`Sorcha.Register.Core` library. Leave the concrete `NodeTrustAnchor` loader in the Register Service;
move only the abstraction and register a Validator-side binding from the same configured/embedded
genesis.

**Rationale**: The Validator Service already depends on `Sorcha.Register.Core` —
`RightsEnforcementService` imports both `Sorcha.Register.Core.Services` (`IGovernanceRosterService`)
and `Sorcha.Register.Core.Storage` (`IReadOnlyRegisterRepository`). So the dependency edge exists and
flows the right way under the constitution's "dependencies flow downward only". No new coupling is
introduced; an interface moves down into a library both services already sit above.

**Alternatives considered**:
- *Give the Validator its own anchor configuration.* Rejected — creates a second source of truth for
  the network root of trust, which is precisely the class of defect this feature exists to remove.
  Two anchors that can disagree is worse than one anchor in an awkward place.
- *Call the Register Service over HTTP to ask.* Rejected — puts a network hop on the per-transaction
  validation path, and makes a security decision depend on a service being reachable.

**Interface is already sufficient**: `IsKnown`, `NetworkId`, `GenesisPublicKeyFingerprint`,
`GenesisPayloadHash`. FR-002 needs `GenesisPublicKeyFingerprint`; the fingerprint function is
`GenesisFileLoader.ComputeFingerprint(publicKeyBytes)`, already shared.

---

## R2 — What proves blueprint-publication authority ✅ DECIDED 2026-08-28

> **DECISION (maintainer, 2026-08-28): Option A(i).** Publication authority is proved against the
> **register's validator roster**, matched on the **existing `sorcha:register-control` derivation
> context**. Moving publication onto the dedicated `sorcha:blueprint-publish` context is *not* part
> of this feature and is to be filed separately.
>
> This unblocks Phase 6 (US2). FR-003's wording — "the register's own control key" — is superseded by
> this decision; the authority is the register's validator roster, because the publisher is a node,
> not an organisation.

**The question was larger than the spec assumed. The reasoning is retained below.**

The spec (FR-003) says "the register's own control key". The code says something different, and the
difference matters:

- **Slot 100 — the organisation key.** Recorded in genesis attestations, and therefore what the
  governance roster is built from. `GovernanceSigningService` exists precisely because governance
  must sign here; signing governance with the node key was refused "submitter not found in roster"
  on every register whose genesis had sealed (observed live on n1).
- **Slot 101 — `sorcha:register-control`, the NODE's system wallet.** This is what actually signs a
  blueprint publication (`Program.cs:2093`). It is correct for the node's own duties — ingesting
  genesis, sealing dockets, publishing — and it is **not on the governance roster by design**.

So **the roster cannot answer FR-003**. The publisher is a node, not an organisation.

The problem this creates is replica-side. A node re-validates *unsealed* transactions it receives
from peers (the peer submission path hands them to the local validator), so a replica must be able
to decide whether *some other node's* system wallet was entitled to publish to a given register. It
cannot answer that from its own configuration.

**Options:**

| | Approach | Assessment |
|---|---|---|
| **A** | Accept the *register's validator roster* as the publishing authority — `ValidatorRoster` already carries per-node purpose-derived public keys with a `derivationContext` and a `status`, per register, updatable through governance. Match the publish signature against entries whose context authorises publication. | **Recommended.** The right shape already exists and is already replicated; publication is a node duty exactly like docket signing, which this roster already governs. Note the roster today carries docket-signing keys, so this needs an entry for the publishing context. |
| **B** | Record the publishing key in the register control record at creation and match against it. | Workable but adds a second registry of node keys per register alongside the one that exists, and needs a migration story for existing registers. |
| **C** | Match against the validator's own configured `SystemWalletAddress`. | **Rejected — it is wrong, not merely limited.** It would accept any publish on the node that made it and refuse the same publish on every replica, which silently partitions the network. |

**Consequential sub-question inside A**: a dedicated derivation context
`sorcha:blueprint-publish` **already exists** and `SystemWalletSigningOptions` already permits it,
but the publish path signs with `sorcha:register-control` instead. Whether to (i) match publication
against the register-control context as it stands, or (ii) move publication onto its own context and
match that, is a real fork:

- (i) is a smaller change and needs nothing new signed, but conflates "the node's general control
  authority" with "may publish definitions", so a compromise of one is a compromise of both.
- (ii) is the cleaner separation the derivation constants were evidently designed for, but it changes
  the signing key for publications, which means **existing sealed publications were signed with the
  old context** and FR-011 requires they still validate. That implies accepting both contexts for a
  transition, which is a ratchet with no defined end while pre-release.

**Decided: (i)** — match the existing context; file (ii) separately. Changing which key signs a
ledger-visible administrative transaction is its own change with its own compatibility surface, and
bundling it here would make the security fix hostage to a key-migration.

**Consequences to carry into implementation:**

- The roster today carries **docket-signing** entries. Matching publication requires roster entries
  whose `derivationContext` authorises publication under `sorcha:register-control`. Since the estate
  may be wiped (2026-08-28), this is a **forward requirement, not a migration**: register creation and
  the genesis ceremony must emit such an entry. Verified by a clean re-genesis, not by inspecting
  what current registers happen to hold.
- Because publication keeps signing with `sorcha:register-control`, the node's general control
  authority and its publishing authority remain the same key. That is accepted for now and is the
  reason (ii) is worth filing: separating them is a real hardening, just not this feature's.

**Follow-up to file**: move blueprint publication onto the existing `sorcha:blueprint-publish`
derivation context, with a dual-accept transition for already-sealed publications.

---

## R3 — Coupling the governance exemption to its roster check

**Decision**: Keep both call sites but make the exemption *derive from* the roster outcome rather
than from an independent string comparison — a single resolved "authority decision" value computed
once per transaction and consumed by both the exemption and the enforcement path.

**Rationale**: The two checks today read the same value in two files and are correct only because
they happen to agree. `RightsEnforcementService.IsGovernanceTransaction` and
`TransactionTypeClassifier.IsGenesisOrControlTransaction` are each individually defensible and
jointly fragile. A single computed decision cannot half-apply.

**Alternatives considered**: *Leave it — no exploitable gap exists.* Rejected on the record: this is
the exact shape the other two values had before they became exploitable, and the compensating check
already exists here, so coupling is cheap now and expensive later.

---

## R4 — Cost of authority resolution on the per-transaction path

**Finding**: Authority resolution is per-register, not per-transaction, and administrative
transactions are rare relative to action traffic. The roster is already resolved on this path today
for governance transactions via `IGovernanceRosterService`, so the seam and its cost already exist.

**Decision**: Resolve per register with caching keyed on the register's last control transaction, so
the cache invalidates naturally when governance changes the roster.

**Watch item**: the review already recorded that Tier-3 sender resolution is O(n) per transaction
(issue #1224). Do **not** add a second O(n) walk. If roster resolution turns out to walk the control
chain per call, cache it rather than accepting the cost.

---

## R5 — Which paths re-validate, and which must stay untouched

Established by reading:

- **Peer submission of unsealed transactions** → deserialised verbatim and handed to the local
  validator. **This is the path the new checks must cover.**
- **Pulling an already-sealed docket** → the receiving node verifies the docket's validator signature
  and chain, *not* the transaction rules. Explicitly documented in `TransactionTypeClassifier`'s
  genesis-freshness remark as the reason late-joining replicas are unaffected by the genesis age
  window. **FR-012 requires this stays as it is.**
- **Bootstrap genesis ingestion** → `GenesisIngestionService` verifies structure, fingerprint and
  signature against the anchor *before* submitting to the validator. After this feature the validator
  performs its own equivalent check, so the anchor is enforced on both the local and the peer route
  rather than only the local one.

**Not established by execution**: whether any path re-runs the full engine over already-sealed
transactions (mempool replay after restart is the suspected candidate). **This must be determined by
execution during implementation, not assumed** — it is the difference between FR-011 being free and
FR-011 being the hardest requirement in the feature.

---

## R6 — The legacy publication era

**Finding**: Publications made before the dedicated label existed were written as `Control` and are
distinguished by a secondary field; `RightsEnforcementService.IsGovernanceTransaction` already
carries an explicit guard for this so a bootstrap publish of the governance blueprint is not treated
as a governance operation (#917). Both eras coexist on live registers "forever" per the code comment.

**Decision**: The authority check must key on the *effective* transaction kind after that existing
guard, not on the raw label — otherwise a legacy publication is judged against governance-roster
authority it never had, and old registers stop validating.

---

## R7 — Test construction constraints

- **Do not stub the hashing layer.** #1587's tests mocked `IHashProvider` to return `byte[32]`, so
  every hash compared equal by construction and the defect under test was invisible.
- **Each guard needs its counterfactual in the same run** — the existing probe pattern used to
  confirm #1591 (control assertion that the unauthorised wallet is refused *without* the claim, then
  the claim is shown not to rescue it).
- **`ValidationEngineChainBindingTests`** already provides a fixture on which the exemption behaviour
  was demonstrated, and is the natural home for the new guards.

---

## Open items carried into planning

1. ~~**R2 sub-question (i) vs (ii)**~~ — **RESOLVED 2026-08-28**: (i), match the existing
   `sorcha:register-control` context. (ii) filed as a follow-up.
2. ~~**FR-007 fail-closed**~~ — **RESOLVED 2026-08-28**: fail closed in every environment, no
   environment gate and no bypass flag.
3. **R5 re-validation paths** — still open by design; resolve **by execution** during implementation
   (task T004). Not a decision to be made on paper.
4. ~~**Do existing registers already carry a publication-authorising roster entry?**~~ —
   **DISSOLVED 2026-08-28.** The estate may be wiped and re-genesised, so the question is not "what
   do existing registers contain" but "what must register creation and genesis produce from now on".
   That converts a discovery-and-migration problem into a straightforward forward requirement:
   register creation and the genesis ceremony must emit a roster entry authorising publication under
   `sorcha:register-control`. Implemented in Phase 6, verified by a clean re-genesis.
