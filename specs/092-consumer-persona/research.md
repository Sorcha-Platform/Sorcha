# Phase 0 — Research and Design Decisions

**Feature**: 092 Consumer Persona and Nav Tidy
**Branch**: `092-consumer-persona`
**Source**: Brainstorming session captured in `docs/superpowers/specs/2026-04-08-consumer-persona-and-nav-tidy-design.md`, clarifications captured in `spec.md`.

All `NEEDS CLARIFICATION` items from the Technical Context were resolved during `/speckit.clarify` (see spec.md → Clarifications → Session 2026-04-08). This document records the design decisions and rejected alternatives surfaced during the earlier brainstorming session so reviewers and downstream task authors do not need to reconstruct them from conversation history.

---

## Decision 1 — Hybrid self-asserted + VC-backed persona model

**Decision**: Ship self-asserted attributes now; design data shapes so verified-credential-backed attributes slot in later without a contract change. Every read-side attribute is wrapped in `PersonaAttribute<T>` with a `Source` discriminator (`SelfAsserted` | `VerifiedCredential`) and an optional `VerifiedBy` issuer DID field.

**Rationale**: Getting government-grade verified identity data is a goal, not a given. A VC-first design would block the feature behind an ecosystem that does not yet exist. A self-asserted-only design would later require a breaking change to the read contract once VCs arrive. The hybrid captures provenance from day one at no extra cost.

**Alternatives considered**:
- **A. Self-asserted only, flat string values** — Rejected. Would require a breaking change to add provenance later. Every form field binding and DTO would need to move.
- **B. VC-first** — Rejected. Blocks consumer value on a non-existent trust ecosystem.
- **C. Hybrid with provenance-carrying DTOs** — **Chosen.** Ships immediately and absorbs VCs as a pure value-space change on the same wire shape.

**User decision captured**: brainstorm Q1, answer "C".

---

## Decision 2 — Persona scope: identity essentials only, growth path via freeform bag later

**Decision**: v1 persona contains 12 typed identity essentials (given name, family name, full name fallback, date of birth, emails, phones, addresses, nationalities). No jurisdiction-specific extras in v1. A freeform "remembered answers" bag is a tracked follow-up.

**Rationale**: A typed schema gives us reliable field matching for `SorchaFormRenderer`. Extras like "emergency contact" or "tax ID" open jurisdiction and locale debates that would delay the first release. The freeform bag is the right growth path once we see which fields actually repeat in real consumer workflows.

**Alternatives considered**:
- **A. Identity essentials only** — **Chosen.**
- **B. Essentials + structured "extras" (GP, employer, tax ID, emergency contact)** — Rejected for v1. Each extra category opens jurisdiction-specific debates that slow release.
- **C. Freeform key/value bag** — Deferred as the v2 growth path, not as the v1 model.

**User decision captured**: brainstorm Q2, answer "A with C as growth model".

---

## Decision 3 — Persona ownership and storage location: Tenant Service

**Decision**: Persona is attached to `PlatformUser` and stored in Tenant Service. The ciphertext lives in the Tenant database; the encryption key lives in the Wallet Service.

**Rationale**: Identity belongs to a person, not a wallet. In a future Power of Attorney flow, a delegate signs with their own wallet but fills a form using the **principal's** persona. If the persona were keyed to a wallet instead of a PlatformUser, every delegation grant would have to smuggle persona data out of the principal's wallet scope. Keying the persona to `PlatformUser` (the existing cross-org identity anchor) makes delegation a pure additive change: the principal's persona row has its `PersonaContentKey` wrapped for the delegate's wallet as part of the delegation grant.

**Alternatives considered**:
- **A. Wallet Service, both ciphertext and key material** — Rejected. Breaks the "key and ciphertext never co-located" rule and blocks the PoA use case.
- **B. Client-side only (browser IndexedDB)** — Rejected for v1. No cross-device sync, no recovery story.
- **C. Tenant Service stores ciphertext, Wallet Service owns the key** — **Chosen.** Clean separation, compatible with future self-custody decryption path without a contract change.

**User decision captured**: brainstorm Q3, answer "Tenant Service, with key held by Wallet Service".

---

## Decision 4 — Key derivation: new `sorcha:persona-vault` purpose

**Decision**: Add a new derivation purpose constant `sorcha:persona-vault` to `Sorcha.Cryptography.DerivationContexts`. Derivation uses the existing per-user system wallet seed. The derived `PersonaContentKey` is a symmetric key fed into XChaCha20-Poly1305 AEAD (same primitive used by Feature 085 file chunks).

**Rationale**: Purpose-based derivation already protects the platform's other derived keys (`sorcha:docket-signing`, `sorcha:register-control`). Adding a distinct purpose means a compromise of any existing derived key does not leak persona data, and vice versa. XChaCha20-Poly1305 is already vetted and integrated; no new primitives are introduced.

**Alternatives considered**:
- **A. Reuse `sorcha:docket-signing` key** — Rejected. Violates single-purpose-per-derivation principle and conflates unrelated scopes.
- **B. AES-256-GCM** — Rejected for consistency only. The platform already uses XChaCha20-Poly1305 for file chunks; reusing the same primitive avoids a second review cycle.
- **C. New `sorcha:persona-vault` purpose with XChaCha20-Poly1305** — **Chosen.**

---

## Decision 5 — Schema matching: explicit-wins hybrid

**Decision**: `SorchaFormRenderer` matches form fields to persona attributes using a hybrid strategy. Explicit `x-persona` extensions win. Where no explicit tag exists, a conservative inference allowlist applies — limited to unambiguous cases: `format: "email"`, `format: "tel"`, field names exactly one of `dateOfBirth`/`dob`/`birthDate`, and recognised postal address schema types. An explicit `x-persona: false` blocks inference on a field.

**Rationale**: Pure inference is fragile (a "Next of kin email" field is not the user's email). Pure explicit tagging would require every existing blueprint to be updated before autofill worked anywhere. The hybrid gives existing blueprints immediate value on the obvious cases and gives blueprint authors precise control where it matters.

**Alternatives considered**:
- **A. Explicit `x-persona` extension only** — Rejected. Zero value for existing blueprints until each is hand-updated.
- **B. Inference only** — Rejected. Fragile and cannot express "never autofill this field" intent.
- **C. Hybrid: explicit wins, inference fallback** — **Chosen.**

**User decision captured**: brainstorm Q6, answer "C".

---

## Decision 6 — Autofill UX: silent apply with strong visual distinction

**Decision**: On form load with autofill enabled, persona values are applied silently to matching fields. Each autofilled field is rendered with a cream-tinted background and a visible `self` provenance tick. A compact one-line summary above the form states *"{n} fields filled from your profile"* with Review and Clear all actions. Editing an autofilled field immediately removes the tint and tick — even if the user retypes the exact persona value. A global user preference (ON by default) controls automatic application; when OFF, the same fill logic is available via a "Fill from profile" button at the top of the form.

**Rationale**: Banner-prompt designs add a second confirmation click to every form. Per-field pill designs make the form visually noisy. Silent apply with strong visual distinction is the fastest path for the common case while still making disclosure visible and reversible. "Edit removes the claim" is the honesty rule — provenance is a statement about who typed, not what was typed.

**Alternatives considered**:
- **A. Banner prompt with explicit "Apply all" action** — Rejected. Adds friction to every form load for marginal benefit; the summary line captures the same information post-fill.
- **B. Silent apply without visual distinction** — Rejected. Dangerously invisible; users could submit disclosed data without realising it.
- **C. Per-field inline "use profile" pills** — Rejected. Chatty and doesn't match the one-click consumer intent.
- **D. Silent apply with cream tint + self tick + summary line** — **Chosen.**

**User decision captured**: brainstorm Q5, answer "refined B" (silent apply with strong visual cues).

---

## Decision 7 — Multi-value attribute shape

**Decision**: Emails, phones, addresses, and nationalities are lists (0..n entries) with exactly one entry marked as the default when the list is non-empty. Lists are hard-capped at 5 entries each (clarification Q1). Each entry carries an optional human label (e.g. "Work", "Home"). Autofill always uses the default entry in v1; per-form alternate picking is a tracked follow-up.

**Rationale**: Plural is the honest shape (users have multiple emails). A single default keeps the autofill logic simple. The cap bounds the UI to a single un-scrolled view and limits write-payload abuse surface.

**Alternatives considered**:
- **A. Singular fields only** — Rejected. Doesn't match reality; users would have to re-edit their profile constantly.
- **B. Lists with no default, prompt each time** — Rejected. Defeats the "one-click fill" value proposition.
- **C. Lists with an invariant default** — **Chosen.**

---

## Decision 8 — Account delete cascades to persona

**Decision**: When a `PlatformUser` is deleted, the `PlatformUserPersona` row is hard-deleted atomically as part of the same operation via an EF cascade rule on the foreign key.

**Rationale**: GDPR right-to-erasure expectations and the principle that persona is meaningless without the user. Soft-delete would leave dangling encrypted identity data in the Tenant DB after an explicit erasure request — a compliance and trust issue. Orphan-allowed would require recovery workflows and retention policies that do not fit the v1 scope.

**Alternatives considered**:
- **A. Cascade delete (hard)** — **Chosen.**
- **B. Soft delete with audit retention** — Rejected. Adds retention policy scope and conflicts with erasure semantics.
- **C. Orphan allowed, reclaimable if account restored** — Rejected. Out of scope for v1 and requires a recovery story.
- **D. Require explicit persona delete before account delete** — Rejected. Surfaces a compliance-adjacent gotcha that users will miss.

**User decision captured**: clarification Q2, answer "A".

---

## Decision 9 — Form render does not block on persona fetch

**Decision**: Forms render immediately and become interactive without waiting for the persona to load. When the persona arrives, the resolver runs and autofill is applied to eligible fields — **except** any field the user has already started typing in (any field with non-empty user-entered content, or any field that currently holds focus). User activity wins; the system never overwrites a field the user has touched.

**Rationale**: Blocking the render until the network round-trip completes adds visible latency to every form open. Applying fills unconditionally on arrival creates the "form rewriting under my cursor" anti-pattern. User-activity precedence is the safest rule: fields the user has not interacted with get filled; fields the user has interacted with are respected.

**Alternatives considered**:
- **A. Block form render until persona loads** — Rejected. Delays time-to-interactive.
- **B. Render immediately, apply fills unconditionally when persona arrives** — Rejected. Overwrites the user's in-progress typing.
- **C. Render immediately, apply fills except to user-touched fields** — **Chosen.**
- **D. Render a loading indicator for up to 500ms** — Rejected. Neither one thing nor the other, extra UI noise.

**User decision captured**: clarification Q3, answer "C".

---

## Decision 10 — Accessibility: per-field announcement plus accessible summary

**Decision**: Each autofilled field exposes an accessible description that announces to assistive technology that the value was filled from the user's profile. The visible summary line above the form has an equivalent accessible representation (count + available actions). A non-visual user learns provenance as they navigate field-by-field, matching the visual design where every field carries its own tick.

**Rationale**: A global summary alone is easy to miss and breaks the field-by-field reading flow that screen-reader users rely on. ARIA-state-only designs (no spoken announcement) do not convey the provenance reliably across assistive technologies. Per-field description on the accessibility tree plus an accessible summary is the most direct and WCAG 1.3.1-aligned model.

**Alternatives considered**:
- **A. Single accessible summary line only** — Rejected. Per-field provenance is lost once the user enters the field-navigation flow.
- **B. Per-field accessible description + accessible summary** — **Chosen.**
- **C. Both plus field-level announcements** — Rejected as verbose; equivalent to B in practice for well-authored AT.
- **D. ARIA state only, no spoken text** — Rejected. Unreliable across screen readers.

**User decision captured**: clarification Q4, answer "B".

---

## Decision 11 — 500 ms cold-load autofill latency target

**Decision**: On a cold form load (no session cache), persona autofill must be applied to eligible fields within 500 ms of the form becoming interactive, measured at p95 across representative consumer forms. On a warm load, autofill is applied within the first render frame with no visible "fields getting filled" moment.

**Rationale**: 500 ms sits at Nielsen's 1-second perception threshold lower bound and is achievable by prefetching the persona once per session on first dashboard load (so the vast majority of form opens are warm). Tighter targets (200 ms) require aggressive infrastructure work and prefetch-on-every-navigation, which is disproportionate. Looser targets (1 s+) allow a visible fill moment.

**Alternatives considered**:
- **A. 200 ms** — Rejected. Disproportionate infrastructure cost.
- **B. 500 ms p95 cold / first-frame warm** — **Chosen.**
- **C. 1 second** — Rejected. Allows visible fill moment.
- **D. No target, measure later** — Rejected. Removes a testable success criterion.

**User decision captured**: clarification Q5, answer "B".

---

## Open questions / deferred

None for v1. All tracked follow-ups (delegation, VC-backed attributes, client-side decryption, per-form override, per-form alternate picker, freeform bag) are out of scope and recorded in `spec.md` → Out of scope and in the brainstorming task list.
