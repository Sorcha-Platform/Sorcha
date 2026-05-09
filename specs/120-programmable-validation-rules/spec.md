# Feature Specification: Programmable Validation Rule Set (Genesis-Embedded, Governance-Updateable)

**Feature Branch**: `120-programmable-validation-rules`
**Created**: 2026-05-09
**Status**: Exploration / not scheduled
**Source**: View 2 of the post-Feature 119 validator audit (2026-05-09 conversation, Saturday-afternoon thinking session)
**Deep technical research**: `C:\Users\stuart\OneDrive\Documents\Claude\Projects\Project Sorcha\Validator2\2026-05-09-programmable-validation-thesis.md` (Cowork)
**Depends on**: post-Feature 119 validator cleanup (TransactionTypeClassifier + dead-code removal — see PR landing this spec entry).

---

## What this is (and what it isn't)

This is a **future-direction marker**, not a scheduled feature. Its purpose is to give the project tree a single discoverable pointer at the idea so anyone reading the spec list later can see it exists, see its dependencies, and find the deep doc.

It is NOT a P-prioritised user story, has no plan/tasks/contracts, and is not on the active milestone roadmap. It will be picked up — if at all — after a deliberate decision to invest in it as a defining Sorcha capability rather than as a near-term feature.

## One-paragraph thesis

Sorcha's validator currently runs one fixed rule set, hardcoded into the validator binary, applied uniformly to every register. This works for the platform's current scale but bakes in a tradeoff: every register must accept the same workflow-validation policy, and changes to that policy require coordinated binary upgrades across all federated validators. A more powerful design embeds a **rule set in the genesis control record** of each register, makes it **governance-updateable** by the register's admin quorum (using the existing `VAL_PERM_*` quorum mechanism), and binds the **active rule-set version** into every docket so verifiers can replay the chain deterministically years later. Two-tier architecture: protocol invariants (chain integrity, signatures, replay protection) stay hardcoded; workflow policy (action reachability, file size limits, lifecycle exemptions) becomes register-specific data. Cleanly composable with three existing precedents in the codebase: `CryptoPolicy` from genesis, `ValidatorRoster` from genesis, and the quorum-gated governance ops for adding/removing/rotating validators.

## Why this is interesting

**Strategic positioning.** Programmable on-chain validation policy is rare — Hyperledger Fabric has chaincode endorsement policies, Ethereum has nothing equivalent for protocol rules, traditional ledger products have none of it. If Sorcha adopts this as a defining capability, it becomes the only register platform where workflow policy is on-chain, governance-mutable, and auditable across the chain history. That's a thesis-level differentiator, not a feature.

**Technical anchoring.** The pattern already exists in the codebase three times over. `CryptoPolicy` (which signature algorithms are acceptable) is read from genesis. `ValidatorRoster` (who can seal dockets) is read from genesis and updated via `AddValidator` / `RemoveValidator` / `RotateValidatorKey` governance ops with quorum signatures. `VAL_PERM_005/006` enforces quorum thresholds. Generalising these patterns from "narrow concerns" to "rule set" is incremental, not from-scratch.

## Why this is not scheduled

**Cost is large.** Estimated 3-6 months of focused work. New types, new governance ops, new control-record handlers, new validator cache, new docket schema, multi-version replay test infrastructure, spec writing, walkthrough validation, n1 deployment of two versions side-by-side to test soft-fork resistance.

**Timing is wrong.** Sorcha is at ~30% production readiness. Near-term value lies in UX and real-world adoption — getting people *using* Sorcha Data Rails before generalising the validator. The thesis-level differentiator only matters if there are users to differentiate for. Validator theory as USP only pays off downstream of adoption.

**Cleanup must come first.** Doing the programmable rules work without first cleaning up the existing rule base would be building new mechanism on top of existing structural smells (carve-outs scattered across nine inline call sites, mixed protocol-invariant and workflow-policy rules under the same prefix taxonomy, persistence-projection asymmetry between Blueprint and Validator services). The post-Feature 119 cleanup PR (this spec's dependency) closes ~80% of the F119-class structural risk for ~5% of the cost; the thesis can build on a healthier foundation when its time comes.

## Two-tier architecture summary

**Tier 1 — Protocol invariants. Hardcoded. Non-disableable.**
`VAL_STRUCT_*`, `VAL_SIG_*`, `VAL_HASH_*`, `VAL_GENESIS_*`, `VAL_CHAIN_001/002/003/004`, `VAL_CHAIN_FORK`, `VAL_REPLAY_*`, `VAL_INTERNAL`. Disabling any of these breaks the chain itself or makes verification non-deterministic for future readers.

**Tier 2 — Workflow policy. Genesis-embedded. Quorum-updateable. Per-register.**
`VAL_BP_*`, `VAL_FILE_002/003/004/005`, `VAL_TIME_002/003`, `VAL_POLICY_001/002` (already governable in narrow form), `VAL_PARTICIPANT_001` (debatable). Disabling any of these violates *this register's intended workflow* but doesn't break chain integrity.

**The split test:** if disabling this rule lets through a transaction that subsequently corrupts the chain or makes verification non-deterministic for any future reader, it's Tier 1. Otherwise it's Tier 2.

## Sequencing

1. **Now (post-Feature 119 cleanup PR):** classifier centralised, dead `BuiltTransaction.ToTransactionModel()` removed. Foundation laid.
2. **Later (only on adoption signal):** invest in the thesis. New milestone, dedicated owner, 3-6 month timeline. Until then, this spec stays at "exploration / not scheduled."

## Open questions for the next round of thinking

These are recorded in full in the Cowork doc. Headlines:

- **Granularity of governance ops** — per-rule (`EnableRule(VAL_X)` / `DisableRule(VAL_X)` / `SetRuleParameter(VAL_X, k, v)`) versus coarse (`AdoptRuleSetVersion(7)` from a curated catalogue). The latter is dramatically safer.
- **Soft-fork mitigation** — `minimumValidatorBinaryVersion` field, `effectiveAtBlockHeight` semantics, every-docket `validationPolicyVersion + hash` binding.
- **What never goes in the rule set** — Tier 1 is enforced by the binary refusing to honour disable ops against protocol-invariant rule IDs. Quorum-capture cannot disable chain integrity.
- **DSL or config-as-data** — strong recommendation: stay config-as-data forever; the moment a rule needs DSL expressiveness, it's protocol-level and belongs in the binary.

## Success criteria for "this spec was worth filing"

- A future reader unfamiliar with the 2026-05-09 conversation can locate this spec, read it in under 5 minutes, decide whether to dig into the Cowork doc, and know what's been thought through versus what's open.
- The Cowork doc is self-contained enough that the next thinking-round doesn't have to start from scratch.

## What this spec does NOT include

- A plan, tasks, contracts, data model, research artefacts, or quickstart. Future scheduling work would add those.
- Any commitment to deliver. This is a directional marker only.

---

**Maintenance:** if the thesis is ever picked up as scheduled work, this file gets superseded by a real spec.md (with status: Active) and the dependency direction reverses — the Cowork research doc becomes a historical reference cited by the new spec.
