# Strathcarron Citizen Arc — Umbrella Design

**Date:** 2026-05-13
**Status:** Umbrella locked. Spec 1 detailed brainstorm pending.
**Scope:** Project-level sequencing and invariants for a multi-spec citizen UX arc in the Strathcarron Council demo universe. Each spec below has (or will have) its own detailed brainstorm and implementation plan.

## Purpose

Sorcha has the architectural pieces for a credible citizen identity story — Citizen Wallet PWA (Feature 114), Assured Identity workflow (Feature 107), Open Participants late binding (Feature 103), Token Status List 2024 revocation, per-org status-list signing, holder→device delegation. What it does not have is a **coherent end-to-end UX** that walks a real citizen from "I want something from my council" through device enrolment, credential receipt, re-presentation at a downstream service, and onward into the wallet as a lived surface.

This umbrella sequences the work to produce that UX. It is the document Specs 1–5 reference for shared decisions, cast, and invariants.

## The protagonist

**Sarah** is a Strathcarron resident. She is the single citizen protagonist across every spec in the arc. New characters introduce themselves to her; she does not introduce herself to new fictions. Her journey accumulates:

1. Signs up for a platform account, enrols her phone, receives her Assured Identity.
2. Uses Assured Identity to fast-track a second council service (e.g. Blue Badge).
3. Accumulates a small portfolio of council-issued credentials.
4. Presents council-issued credentials to council-contracted third parties (parking enforcement, refuse collection, concessionary travel operator) who verify but did not issue.

Sarah is the regression suite. If a change to any spec breaks her continuity (renames a claim, changes a credential type, fragments her account model), the arc has regressed.

## The universe

Re-uses the existing **Strathcarron** universe (`walkthroughs/council/`):

- **Strathcarron Council** — issuer of all citizen-facing credentials.
- **Heatherbank Environmental** — third-party environmental services contractor; verifier-not-issuer in the arc.
- **Caledonian Water** — utilities; verifier-not-issuer.
- **Stoniebridge Construction, Murchison Engineering** — pre-existing org cast; not in the citizen arc but available for cameo (e.g. third-party building inspector reviewing a citizen's domestic application).

New orgs added for the third-party-verifier leg as needed: a refuse collection contractor, a home-care provider, a concessionary travel operator. Naming convention follows the existing pattern (descriptive Scottish-flavoured names; no real-world brand collisions).

## Locked decisions

| # | Question | Answer | Implication |
|---|----------|--------|-------------|
| 1 | PWA vs Sorcha.UI.Web positioning | **Co-equal** citizen surfaces | Neither is a satellite. Web↔PWA handoff is a first-class recurring pattern, not a one-shot onboarding moment. |
| 2 | Account model | **Email/password is the durable anchor**; passkey and social are equivalence-class entry points to the same account; wallet devices sit on top | Recovery story is "sign in by any of your methods, revoke lost device, re-enrol new one" — no mnemonic for Sarah. Holder key is server-anchored at slot 108 and re-derives on enrolment. |
| 3 | Council-page-to-wallet entry mechanism | **Hybrid universal QR** — one URL, served as both a scannable QR (cross-device) and a tap-able link (same-device); copy-paste as a third fallback | OID4VP-aligned. Council pages render the QR unconditionally. Verifier code in `Sorcha.Verifier` is the reference. |
| 4 | Cross-device QR vs same-device deep-link default | **Subsumed by Q3** | One artifact with two resolution paths; no separate default to pick. |
| 5 | Cross-org scope | **Council-focused, with council-contracted third parties as verifier-not-issuer** | Sarah's experience stays brand-coherent (all "council services"); the verifier-isn't-issuer architecture is still exercised for real. Avoids the cold-start trust narrative of public/private cross-org demos. |

## Spec sequence

The order alternates content (concrete service, demoable on its own) with infrastructure (visual system, seam, surface). This keeps every milestone demoable while building toward a coherent whole.

### Phase 0 — Umbrella lock (this document)

No code. Output is this doc plus project memories. Cheap now, expensive to retrofit.

### Spec 1 — AssuredIdentity on the PWA

**Type:** content.
**Goal:** Sarah signs up, enrols her phone, receives her `AssuredIdentityCredential` in the PWA. Replaces the HAIP filesystem wallet target in the existing `AssuredIdentity` walkthrough with the real `SorchaLocalWallet` target.
**Owns:** PWA install prompt copy, empty-Home state, first-credential arrival moment, credential detail in `Issued` watermark state, swap of the AssuredIdentity walkthrough's wallet target. Brings the walkthrough end-to-end on the PWA.
**Why first:** smallest piece of net-new design; proves the whole stack; produces the foundational credential every later service gates on.

### Spec 2 — Wallet UX foundations

**Type:** infrastructure.
**Goal:** the PWA feels like a real product, not a prototype.
**Owns:** card-layout system (single → stack → multi), ConsentSheet copy + disclosure pattern, Settings IA, install prompt, clock-skew banner, "lost my phone" copy on Devices, the visual contract with the `x-review` id-card renderer (state-driven watermark: `Draft` / `Pending` / `Issued`).
**Why second, not first:** Spec 1 surfaces what the shells actually need to hold. Designing the shells without that grounding produces shells nobody asked for.

### Spec 3 — Enrolment inside a council application wizard

**Type:** infrastructure.
**Goal:** the web↔PWA handoff is a designed pattern, not an accident. Sarah-from-cold-start can apply for a council service and acquire a wallet as a side-effect.
**Owns:** the embedded enrol-as-step inside a council application form; the council page's hybrid-QR entry point UX (per Q3); the redirect/handoff UX between Sorcha.UI.Web and the PWA; the post-submit "watch your wallet" pattern.

### Spec 4 — Credential-gated second service

**Type:** content. Recommended subject: **Blue Badge**.
**Goal:** the architecture pays for itself. Sarah returns for a second service; the open starting action gates on her `AssuredIdentityCredential`; the form already knows who she is.
**Owns:** credential-gated blueprint authoring pattern for citizen-facing services; PWA picker + ConsentSheet in real use (designed in Spec 2, exercised here); the late-bind moment from the citizen's perspective; the issuance of `BlueBadgeCredential` to the PWA.

### Spec 5 — MyStrathcarron portal, multi-credential density, third-party verifiers

**Type:** infrastructure with content additions.
**Goal:** Sarah has a real relationship with her council. Multi-credential UX, activity log, recovery flow, and the first council-contracted third-party verifier interaction (e.g. presenting Blue Badge to a parking enforcement contractor).
**Owns:** multi-credential home layout, cross-service activity log, recovery flow, council portal IA, renewal-as-confidence-cue, the production `IIssuerKeyResolver` implementation that resolves `did:sorcha:org:*` via tenant register verification methods (this becomes a real requirement once verifiers are not the issuer).

Note: this absorbs the cross-org finale that was previously scoped as a separate Spec 6. The "verifier is not the issuer" demonstration happens here in council-contracted form.

## Cross-cutting invariants

Locked at umbrella level. Specs may not violate without amending this doc.

1. **One protagonist.** Sarah carries the arc. Every spec is "Sarah does X."
2. **One issuer per credential, one credential per service.** Strathcarron Council issues every citizen-facing credential in the arc. No multi-issuer credentials in scope.
3. **Generic claim names.** `dateOfBirth`, not `strathcarronDob`. Credential types use a Sorcha-neutral vocabulary. This is the discipline that makes the third-party-verifier leg honest and keeps the door open to fully external cross-org without rework.
4. **Wallet reuses the `x-review` id-card renderer** (`ReviewSummaryRenderer.razor` + `IdCardLayout.razor`) with state-driven watermark (`Draft` / `Pending` / `Issued`). One visual component, three contexts (form preview, reviewer pending, wallet detail). Shared-user-components library (Feature 122, currently parked) is the right eventual home for this; the arc's success makes 122's eventual landing more load-bearing.
5. **Hybrid universal QR is the only invocation mechanism.** No service-specific entry-point variations. Same artifact, three resolution paths (scan, tap, paste).
6. **Email/password is the account anchor.** Passkey and social are alternative entry methods, never substitutes. Holder key is server-anchored; recovery is account recovery, not seed-phrase recovery.
7. **PWA and Sorcha.UI.Web are co-equal.** No spec assumes Sarah is on one device or the other except as a per-screen affordance.

## What's out of scope

Explicitly excluded so we don't drift:

- **Fully external cross-org presentation** (citizen presents council credential to private gym, GP, bank). Architecturally supported; not in the demo arc. Re-evaluate after Spec 5.
- **Self-custodial wallet variant** (mnemonic-recovery, on-device-only key custody). Different product. Sorcha's holder-key-server-anchored model is a deliberate choice; the arc honours it.
- **Multi-council federation** (Strathcarron and a neighbouring council both issuing). Single-council suffices for the umbrella story.
- **Issuer-side workflow design for council-contracted third parties.** The arc demonstrates them as verifiers; designing how they issue their own credentials (e.g. a refuse contractor issuing a service-completion credential to a citizen) is outside scope and would be its own arc.
- **PWA on devices other than mobile** (tablet, desktop PWA). The PWA technically installs anywhere; the arc designs for mobile and treats other form factors as best-effort.

## What success looks like

The arc has succeeded when:

- A new viewer can be walked from "Sarah has never heard of Strathcarron Council" to "Sarah holds three credentials and just used one at a council-contracted parking enforcement contractor" in a single demo session, with no narrative seams.
- Each spec is independently demoable and individually adds visible value to the citizen experience.
- No spec required reworking an earlier spec's claim names, credential types, account model, or visual language. (If this happens, the umbrella failed.)
- The `IIssuerKeyResolver` production path is in use by the end of Spec 5 — the verifier-is-not-the-issuer story is cryptographically honest, not theatrical.

## Open items deferred to individual specs

Carried forward, not litigated here:

- Spec 1: copy and animation for "first credential arrival" moment; whether the install prompt is opportunistic or mandatory on first wallet load.
- Spec 2: card-stack vs carousel vs list for multi-credential home (decided in Spec 2, design-shaped not roadmap-shaped).
- Spec 3: where the council-page hybrid-QR sits in the form flow (preflight gate, inline mid-form, or post-submit handoff); copy for "you'll need a wallet for this."
- Spec 4: exact disclosure surface in ConsentSheet (per-claim toggles vs all-or-nothing); selection UX when Sarah holds multiple credentials that satisfy the same requirement.
- Spec 5: activity log granularity (per-presentation vs per-service); recovery UX entry point (Settings vs first-launch on a fresh device that detects orphaned account).

## References

- Feature 114 (Citizen Wallet PWA): `specs/114-citizen-wallet-pwa/`, `.claude/skills/sorcha-architecture/SKILL.md` § Citizen Wallet PWA.
- Feature 107 (Assured Identity v1): `specs/107-assured-identity-v1/`.
- Feature 103 (Open Participants & Late Binding): `specs/103-verified-citizen-v2/`, `.claude/skills/blueprint-builder/SKILL.md` § Open Participants.
- Feature 116 (Account linking / auth-method management): `specs/116-account-linking/`.
- Feature 118 (Notification hubs): `specs/118-notifications-architecture/`.
- Feature 122 (Shared user components, parked): `specs/122-shared-user-components/`.
- Existing universe: `walkthroughs/council/README.md`.
- Existing assured-identity walkthrough: `walkthroughs/AssuredIdentity/`.
