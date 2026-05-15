# Phase 0 — Research

**Feature**: F127 / credential-gated second council service (Blue Badge)
**Date**: 2026-05-15
**Status**: Complete. No unresolved clarifications.

Most of the load-bearing research for Spec 4 was done in the design brainstorm (`docs/superpowers/specs/2026-05-15-spec-4-credential-gated-second-service-design.md` §10 Q1–Q6) and the boundary brainstorm (`docs/superpowers/specs/2026-05-15-platform-consumer-boundary-design.md`). This document records:

1. **Locked decisions** (re-stated here so plan-phase has a single Phase 0 reference).
2. **Tactical research** still open after the design brainstorms — resolved here.

## §1 — Locked decisions

### Decision: Consent surface is all-or-nothing in v1

- **Source**: design Q1.
- **Rationale**: Keeps the citizen on familiar consent-prompt ground. Per-claim toggles introduce a "request can't be granted" failure surface that Spec 4's use case doesn't justify.
- **Alternatives considered**: per-claim disclosure toggles (rejected for v1; revisited when a real verifier-isn't-issuer use case appears in Spec 5).

### Decision: Picker hides at 1 match, force-selects at ≥2

- **Source**: design Q2.
- **Rationale**: Common-path frictionless when the citizen has exactly one matching credential; honest about the multi-match case.
- **Alternatives considered**: always-render with auto-select (rejected — intrusive in common path), always-force-tap (rejected — adds friction with no value when only one option exists).

### Decision: SignalR primary, 3 s polling fallback, 60 s manual recovery

- **Source**: design Q3.
- **Rationale**: Spec 3's hybrid pattern proven; one-line variant for the presentation case.
- **Alternatives considered**: server-set cookie + browser redirect (deferred to Spec 5 with the verifier-isn't-issuer story); plain 2 s polling (rejected — wasteful when SignalR is available).

### Decision: PWA-side confirmation dialog before signing

- **Source**: design Q4.
- **Rationale**: Mirrors F126 friend-scans mitigation. Citizen sees both the council and the credential type before any signature happens.
- **Alternatives considered**: server-set cookie binding the presentation request to the citizen (deferred to Spec 5).

### Decision: Walkthrough chains off Spec 3 `state.json`

- **Source**: design Q5.
- **Rationale**: Established phase-2-on-phase-1 pattern in `walkthroughs/`.
- **Alternatives considered**: duplicate the issuance step (rejected — copy-paste blueprint provisioning), chain Spec 3 setup as a prerequisite step (kept as fallback for fresh environments).

### Decision: Linear gate composition — `EnrolGate` wraps `CredentialGate` wraps the form

- **Source**: design Q6.
- **Rationale**: Returning Tier 1 citizens glide through both gates with two transient screens at most. Cold-start citizens hit Spec 3's preflight first; no-credential citizens hit the FR-018 error state.
- **Alternatives considered**: credential gate wraps enrol gate (rejected — can't present from an unpaired wallet); sibling pattern (rejected — composition order matters).

### Decision: `samples/` folder build topology

- **Source**: boundary doc §3.
- **Rationale**: Build-topology enforcement (own container image, CI grep gate) is more durable than convention.
- **Alternatives considered**: separate repository (rejected — too much overhead for the demo arc); pragmatic in-tree carve-out for Strathcarron (rejected — carve-outs are forever).

### Decision: No n1 deployment in Spec 4

- **Source**: boundary doc §6.
- **Rationale**: Operator-owned domain / services work is separate. Spec 4 ships local-stack only.
- **Alternatives considered**: deploy `strathcarron-portal` under the existing `n1.sorcha.dev` hostname at a sub-path (rejected — would re-encode the boundary error the rule is meant to prevent).

## §2 — Tactical research

### Q-T1: docker-compose entry for the sample portal

- **Decision**: New service `strathcarron-portal` in `docker-compose.yml`. Internal port 80, exposed locally on `5300`. Depends on `gateway` (Sorcha's YARP front door). Reachable at `http://localhost:5300/` for local dev. Does NOT route through the gateway — it's a peer of the platform, not a hosted-by-platform surface.
- **Rationale**: Matches the boundary doc's intent — the sample is a separate deployable that *calls* Sorcha. Routing through the platform's own gateway would muddy that demonstration.
- **Alternatives considered**: hosting the sample behind `gateway` at a sub-path (rejected — encodes the wrong narrative); reverse-proxy-fronted with a `strathcarron.localhost` hostname (deferred to operator's domain prep, follow-up after Spec 5).

### Q-T2: OID4VP presentation-request shape

- **Decision**: Reuse the request URI shape already in `Sorcha.Verifier` for the F125 verifier-desk flow. Fields: `client_id` (council DID), `response_type=vp_token`, `presentation_definition` (built from the gate's `credentialType` + `issuerAllowlist` + `requiredClaims`), `nonce` (16-byte URL-safe base64), `response_uri` (`POST /api/blueprint/presentation-responses`). Default expiry: 5 minutes (TTL on the `IAtomicDistributedCache` stash entry — same as F126 enrol session).
- **Rationale**: OID4VP alignment was a Spec 1 umbrella decision. Reusing the verifier-desk shape means `Sorcha.Verifier.Engine` server-side validation works without modification.
- **Alternatives considered**: custom Sorcha shape (rejected — Spec 5 cross-org verifier story needs OID4VP-compatible artifacts); shorter expiry (rejected — 5 min matches Spec 3's already-tuned operator-friendly default).

### Q-T3: Council chrome — minimal viable IA

- **Decision**: PR-A delivers: `CouncilHeader` (logotype + "Strathcarron Council" wordmark + thin top-nav: Services / About / Contact us / My account), `CouncilFooter` (postal address + accessibility statement link + privacy notice link), neutral light-mode page background, link colour distinct from Sorcha's primary. Services landing page (`Pages/Index.razor`) is a card grid with two cards: Driving Licence and Blue Badge (Blue Badge card lands in PR-C; PR-A ships it as a "coming soon" placeholder).
- **Rationale**: Just enough to read as a real council site on first glance (Spec 4 success criterion SC-008). Anything more is Spec 5's MyStrathcarron IA work.
- **Alternatives considered**: full council homepage with news strip, weather, multi-service catalogue (rejected — out of Spec 4 scope); bare MudBlazor defaults (rejected — would read as "Sorcha admin UI" not "council site").

### Q-T4: Sample's auth model for calling Sorcha

- **Decision**: PR-A preserves F126's existing auth flow unchanged. The sample's pages compose `EnrolGateComponent` (which calls `/whoami` + `/me/devices`) over the gateway, with cookie-based session auth — exactly as the page worked when hosted in `Sorcha.UI.Web.Client`. The third-party-integrator OIDC / API-key path the boundary doc §6 flags is **NOT** introduced in Spec 4. It lands in Spec 5 alongside the verifier-isn't-issuer cryptographic story.
- **Rationale**: Spec 4 is "credential-gated second service," not "third-party integrator auth." Scoping discipline. The boundary doc's §6 flags this as Spec 5's call; honoured here.
- **Alternatives considered**: introduce the OIDC path in Spec 4 PR-A (rejected — bundles two specs' worth of work, breaks Spec 4 scope, defers Blue Badge content); pure API-key (rejected — wrong shape for citizen-facing pages).

### Q-T5: Existing tests / artifacts that touch the moved F126 page

- **Decision**: After PR-A moves `CouncilApplicationDrivingLicence.razor`:
  - Update `walkthroughs/Strathcarron/setup-cold-start-demo.ps1` to point operators at `http://localhost:5300/services/driving-licence` (was `http://localhost/strathcarron/services/driving-licence`).
  - Update `state.json` schema field `councilPage` accordingly.
  - Update any Playwright tests touching `data-testid="driving-licence-submit"` to land on the new URL (those test selectors already use the testid, so only the navigation target changes).
  - Update `Sorcha.UI.Web.Client/Sorcha.UI.Web.Client.csproj` — remove the moved file from compilation (handled automatically by `git mv` + Razor's wildcard `<Content>` includes).
  - Update F126 docs (skill files, MASTER-TASKS notes about `/app/strathcarron/services/driving-licence` URL).
- **Rationale**: SC-006 mandates F124 / F125 / F126 demo journeys remain green. PR-A's success bar is "F126 cold-start walkthrough still works end-to-end after the page moves."
- **Alternatives considered**: leave the F126 walkthrough docs referring to old URL with redirect (rejected — operator confusion). Add a redirect in `Sorcha.UI.Web.Client` from the old path to the new sample (rejected — re-encodes the boundary error and creates two pages serving the same content).

## §3 — Open items deferred to /speckit.tasks

These are tactical implementation choices that don't need a Phase 0 decision but are flagged for the task breakdown:

- Exact CSS strategy for the council sample (plain CSS vs CSS modules vs MudBlazor with a custom theme). Likely: plain CSS in `wwwroot/css/council.css` to keep the sample free of MudBlazor's bulk; library components consumed from `Sorcha.UI.Components.User` will already carry their MudBlazor flavour for the gate surfaces — that's fine, the gate is the part that should look like Sorcha is doing something.
- Whether `BlueBadge.razor` shares form-state plumbing with `DrivingLicence.razor` via a shared base component, or duplicates. Default: duplicate; refactor only if a third sample page appears.
- Specific port for `strathcarron-portal` in docker-compose (5300 reserved here; reconfirm against the existing `docs/getting-started/PORT-CONFIGURATION.md`).
- Whether the CI grep gate runs as a separate workflow step or piggybacks on an existing CI check.
