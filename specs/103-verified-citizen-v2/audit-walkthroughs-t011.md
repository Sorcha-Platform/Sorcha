# T011 — Walkthrough Audit: Open-Participant Pre-Binding

**Status**: Completed 2026-04-13
**Related task**: T011 [US1] Audit remaining walkthroughs for pre-binding of open participants

## Scope

Identify every walkthrough that declares a `$walletMap` including a participant whose blueprint action is marked `isStartingAction: true`. For each match, classify:

1. **Genuinely open citizen-facing** → should be fixed to match the late-binding contract.
2. **B2B / internal role with a known wallet** → pre-binding is correct; leave as-is.
3. **Test-only fixture** → either works; leave as-is unless tightening the contract story.

## Results

Ten blueprint files found with `isStartingAction: true`. All starting-action senders audited below.

| Walkthrough | Starting sender | WalletMap pre-binds? | Classification | Recommendation |
|---|---|---|---|---|
| **HaipVerifiedCitizen** | `citizen` | **Was yes** | Genuinely open | ✅ Fixed in T009 |
| **HaipDrivingLicence** | `applicant` | **Was yes** | Genuinely open (credential-bootstrapped) | ✅ Fixed in T010 |
| **ConstructionPermit** | `contractor` | Yes (dynamic via `$wallets` dict) | B2G with pre-registered contractor | Acceptable — contractors are known, registered participants in practice. Not a foot-gun. |
| **SelfBuildHouse** (planning-permission) | `self-builder` | Yes (dynamic via `$wallets` dict) | **Citizen-facing** — a self-builder IS a member of the public | **Recommend fix post-Feature-103.** The walkthrough currently passes because the test runner signs in as the exact pre-baked wallet; it would break if a different public-org user tried to submit. Post-v2, this walkthrough should reuse the v2 PersonName/DateOfBirth/PostalAddress primitives and late-bind the self-builder. Flagged for a follow-up feature. |
| **SelfBuildHouse** (building-warrant) | `self-builder` | Same as above | Same as above | Same |
| **HealthDeclaration** | `patient` | Yes | **Citizen-facing** — a patient IS a citizen | **Recommend fix post-Feature-103** for the same reason as SelfBuildHouse. Currently passes by accident. |
| **FormCoverage** | `submitter` | Yes | Test-only (exercises the form renderer) | Acceptable — `submitter` is a test-fixture role, not a modelled real-world actor. |
| **PayloadTests** | `sender` | Yes | Test-only (exercises file-chunk transfer) | Acceptable — `sender` is a test-fixture role. |
| **TradeFinance** (invoice-finance) | `finance-director` | Yes (dynamic via `$wallets` dict) | B2B internal role | Acceptable — a finance director is not a walk-in public participant. |
| **TradeFinance** (procurement-to-pay) | `procurement-mgr` | Yes (dynamic via `$wallets` dict) | B2B internal role | Acceptable — a procurement manager is not a walk-in public participant. |

## Findings

- **2 walkthroughs fixed inline in this session**: HaipVerifiedCitizen (T009) and HaipDrivingLicence (T010). Both had the foot-gun that motivated this feature.
- **2 walkthroughs flagged for a follow-up feature**: SelfBuildHouse and HealthDeclaration both model citizen-facing interactions but currently pre-bind the citizen participant. They pass today because the walkthrough runner signs in as the exact pre-baked wallet, masking the contract violation. They would break for a real public user. Recommend opening a follow-up that converts each to late binding and adopts the v2 identity primitives once this feature ships. Not in scope for Feature 103.
- **1 walkthrough (ConstructionPermit) is defensible as-is**: contractors are pre-registered B2G participants in real government services, not walk-in citizens. Pre-binding the contractor wallet matches the domain model.
- **2 walkthroughs (FormCoverage, PayloadTests) are test fixtures**: their senders are not modelled real-world actors, and either shape works for the tests they drive.
- **2 walkthroughs (TradeFinance) are B2B**: all participants are known internal roles with specific wallets. Pre-binding is correct.

## Publish-time guardrail (VAL_BP_010) impact

Once T020 + T021 land the publish-time guardrail, the four currently-passing walkthroughs with pre-bound open-style participants will need to be updated before their blueprints can republish on any deployment that runs the guardrail:

- SelfBuildHouse (planning-permission, building-warrant) — two blueprints
- HealthDeclaration — one blueprint

This is a deliberate breaking change. The migration is the citizen's application form pattern (same shape as Verified Citizen v2) and is tracked as a **follow-up feature**. This audit produces the list; the feature that implements late binding for those walkthroughs consumes it.

ConstructionPermit, FormCoverage, PayloadTests, and both TradeFinance blueprints are unaffected — their senders either:
- have `walletAddress` legitimately pre-bound at publish time (the non-starting-action branch of the contract, which VAL_BP_010 does not touch), OR
- map to participants where pre-binding is the correct domain model and the blueprints can be updated to mark those senders as NOT starting actions if the guardrail accidentally catches them.

## Deliverable

This audit is the deliverable for T011. It is committed alongside the spec as `specs/103-verified-citizen-v2/audit-walkthroughs-t011.md` so that:

1. The PR reviewer can see the scope of the fixes in T009/T010 and confirm no other walkthroughs needed immediate changes.
2. The follow-up feature has a ready-made backlog of the walkthroughs that need late-binding conversion.
3. The VAL_BP_010 guardrail in T020/T021 can be written knowing exactly which walkthroughs will start failing on publish after it lands.
