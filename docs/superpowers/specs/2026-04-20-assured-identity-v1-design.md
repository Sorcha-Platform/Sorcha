# Assured Identity v1 — Design

**Date:** 2026-04-20
**Status:** Draft for review
**Author:** Stuart Fraser (with Claude)
**Supersedes:** Aspects of Feature 103 (Verified Citizen v2), Feature 104 (Credential Claim Action), Feature 106 (Register-Native Credentials), and the `HaipDrivingLicence` walkthrough.

## Why this exists

The platform's "verified person" story is split across two co-located blueprints in the same walkthrough:

| Blueprint | Credential | Delivery |
|---|---|---|
| `walkthroughs/HaipVerifiedCitizen/blueprints/verified-citizen.json` (v3) | `VerifiedCitizenCredential` | HAIP external wallet (OID4VCI + Wave 14b claim card) |
| `walkthroughs/HaipVerifiedCitizen/blueprints/assured-person.json` (v1) | `AssuredPersonCredential` | Register-native (Feature 106 sealed disclosure) |

Same shape, same seven claims, same open-citizen late binding — different delivery pipe and different credential name. Downstream, `walkthroughs/HaipDrivingLicence/` chains off `VerifiedCitizenCredential` via OID4VP presentation to issue a licence credential.

While the technical patterns each prove are useful, the duplication means:

1. There is no single canonical "person identity" credential type or workflow. Every conversation about citizen identity has to qualify which variation is meant.
2. The two delivery modes are presented as competing variations rather than as a single workflow with a holder-controlled choice at claim time.
3. The form rendered for the citizen is functional but unpolished — DOB does not block future dates client-side, no photo capture is wired in (a bare InputFile in legacy Web.Client is the only existing photo path), no review-before-submit step.
4. The downstream Driving Licence walkthrough is technically separate even though it tells the same story arc — get an identity, then use it to get a licence.

The goal of v1 Assured Identity is to consolidate all of this into one canonical credential, one canonical workflow, one polished citizen-facing form, and one walkthrough that proves the full lifecycle including a downstream consumer. The design must also leave a clean seam for future replacement of the human assessor by a real backend identity validator service, without requiring blueprint or platform changes when that arrives.

## Goal

Ship a single canonical citizen-identity workflow with three deliverables, all consumed by one consolidated walkthrough:

1. **`AssuredIdentityCredential`** — replaces `VerifiedCitizenCredential` and `AssuredPersonCredential`. Same claims as the existing v3 blueprints plus an optional `portrait` claim. Issued via either register-native delivery (default for Sorcha-local holders) or HAIP OID4VCI (for external HAIP wallets), holder chooses at claim time.
2. **`DrivingLicenceCredential` (re-issued as Phase 2)** — issued by DLA after OID4VP presentation of `AssuredIdentityCredential`, proving the full credential-chain lifecycle on day one.
3. **Renderer polish** — DOB future-block, photo capture wired into the core renderer dispatcher, and a new `x-review` schema extension with an `id-card` layout variant that renders a credential-shaped review card for both the citizen's pre-submit summary and the assessor's pending-review screen.

The existing `HaipVerifiedCitizen/` and `HaipDrivingLicence/` walkthroughs are deleted and fully replaced by `walkthroughs/AssuredIdentity/`.

## Design decisions (with alternatives we considered)

### Credential type name

**Decision:** `AssuredIdentityCredential` — a clean break from both prior names.

The existing names confused the audience: `VerifiedCitizenCredential` implies civic-status verification (which it does not perform); `AssuredPersonCredential` is closer but was always positioned as "the register-native variant" rather than a canonical name. "Assured Identity" is closer to the actual semantic — an identity record asserted by an issuer to be true within the issuer's assurance frame — and is generic enough to apply equally to passport-style government identity, employer-issued workforce identity, or any other public→issuer→credential pattern.

**Rejected alternatives:**
- Keep `VerifiedCitizenCredential` and add a `delivery` field — preserves the chain to the existing DLA walkthrough but bakes in a misleading name.
- Keep `AssuredPersonCredential` and rebrand internally — keeps the chain at the cost of clarity; "Person" is also less general than "Identity".

### Delivery mode

**Decision:** Both register-native and HAIP external wallet, with the holder choosing at claim time via the existing Wave 14b credential claim card.

Register-native delivery (Feature 106) is the simplest UX for Sorcha-native users — no QR scan, sealed disclosure replicates to the holder's wallet via the register's normal peer-replication path. HAIP delivery is required for any verifier outside Sorcha and is necessary to prove OID4VP/KB-JWT capability for the Phase 2 driving licence chain.

The Wave 14b claim card already supports both paths in code: clicking "Claim" redeems the OpenID4VCI pre-authorized code against the holder's local Sorcha wallet; clicking "Scan" reveals an embedded QR for an external HAIP wallet. No new platform or schema work is required to support the dual-path; only the walkthrough has to exercise both paths to count as proven.

**Rejected alternatives:**
- HAIP-external-only — proves OID4VP interop but loses the simpler holder UX and abandons the register-native investment from Feature 106.
- Register-native-only — simpler but cannot prove the OID4VP / KB-JWT chain that the Phase 2 licence requires.

### Photo as embedded claim, not evidence-only

**Decision:** Optional photo capture with two-image submission shape — full original kept on the register as evidence (visible to the assessor and a future validator API), and a 240×320 JPEG token-image embedded as a selectively-disclosable `portrait` claim in the credential.

This matches industry precedent. ICAO e-passport chips and ISO 18013-5 mDLs both embed the holder's portrait directly as a JPEG of approximately token-image size (15–30KB). They do not reference the photo by URL — offline verification matters for both standards. The selectively-disclosable framing means the holder withholds `portrait` for verifications where it is unnecessary (age-gates, basic identity proof) and reveals it for verifications where face-matching is the point (DLA roadside check, building access).

The full original on the register is what the assessor (today: human, or sorcha-agent in rules mode) reviews and what a future automated validator API would consume for liveness or biometric matching. The token embedded in the credential is the durable, holder-controlled view.

**Rejected alternatives:**
- Evidence-only (full image on the register, nothing in the credential) — closest to how a real ID assurance flow runs today, smallest credential size, but verifiers cannot visually confirm the holder later, which kills the DLA-style downstream.
- Both-as-claim (full image embedded as a claim plus token) — credential bloat for marginal benefit; the SD-JWT becomes 100KB+.

ICAO composition guidance is rendered as advisory tips in the capture UI, not enforced. Automated composition checking (face detection, background uniformity) is deferred to the future validator API; in v1 the assessor visually rejects bad photos.

### Form layout — GDS 5-page wizard

**Decision:** A 5-page wizard following the GDS "one-thing-per-page" pattern, rendered via the existing `x-pages` and `x-sections` layout extensions plus the new `x-review` extension.

| Page | Content |
|---|---|
| 1. About you | Name section (given, middle?, family, full derived) + DoB section in a single page with two `x-sections` |
| 2. Your address | Postcode-driven address lookup via `IAddressLookupClient`, with a manual-entry escape hatch |
| 3. Contact | Email only |
| 4. Photo (optional) | Camera capture with ICAO advisory tips + Skip-and-submit option |
| 5. Review & submit | `x-review` `id-card` layout — what they'll receive, with edit-jump buttons per section |

Every editable field carries an `x-persona` binding to the user's stored persona; persona-filled values render with the cream tint and "self" provenance label and flow through the existing persona-autofill pipeline into the issued credential's claims (verified clean — see `BuildClaimsFromMappingsTests.cs`).

**Rejected alternatives:**
- Single-page sectioned form — works for power users on desktop but feels overwhelming for the citizen-facing flow we want to optimise for.
- 3-page wizard (the original B option) — collapses Name+DoB+Address into one page; loses the GDS one-thing focus.
- 4-page wizard (no review) — misses the GDS-standard review-before-submit; review is too valuable to skip for a flow that produces a government-issued credential.

### Review screen as an ID card preview, not a tabular summary

**Decision:** Page 5 renders the credential-to-be as a stylised ID card (navy gradient, gold seal, holder photo on the left, name and details on the right, DRAFT watermark, "Issued by Government of Scotland" header) using a new `x-review` schema extension with `layout: "id-card"`.

This makes the review screen show the citizen exactly what they'll hold once the credential is issued. The same component renders the assessor's pending-review screen (PENDING watermark in amber, Approve/Reject footer instead of Edit/Submit) and the issued credential's MyCredentials detail view (no watermark, "Issued" state). One component, three states.

The DLA Phase 2 review reuses the same `x-review` + `id-card` machinery. The assessor screen renders **two stacked cards**: the presented `AssuredIdentityCredential` (navy, "✓ VERIFIED" badge, with withheld claims rendered as faded "— — —") and the licence-to-be (pink — UK driving-licence convention — with PENDING watermark and the licence number / class / dates filled in). Different colour theme is derived from the credential type; no per-credential bespoke component.

**Rejected alternatives:**
- Tabular summary — functional but does not help the citizen visualise what they'll carry. Misses the opportunity to make Page 5 the same UX as the issued credential's detail view.
- Bespoke Blazor component referenced via `x-component: "name"` — works for one flow but accumulates a custom component per credential type. Rejected in favour of a parameterised extension that scales to any credential preview.

### Validator hook architecture — agent, not API

**Decision:** The assessor participants (`gov-assessor`, `dla-officer`) are filled by `sorcha-agent` processes running in `rules` mode. Future automated identity validation plugs in either as a new `external` agent mode or as an HTTP call inside the agent's rules. Neither requires platform or blueprint changes.

This is materially cleaner than the alternatives. The blueprint's review action stays as a normal approval action with a decision schema; the agent is just an unattended human. When a real validator vendor arrives, the agent gains a step that calls out to the vendor's API and stamps the result; the blueprint is unchanged. The same agent can run in `ai` mode for non-deterministic contextual review (Claude vision examining the submitted photo against the form data) — a natural v1.1 enhancement.

**Rejected alternatives:**
- New gate type or hook field on the Action model — adds platform surface area for something the actor framework already handles.
- Blueprint-declared validator URL — couples the blueprint to a specific vendor; defeats the goal of pluggability.

### Cross-peer testing scope

**Decision:** Run a single multi-peer smoke test (`run-multi-peer.ps1`) per release cycle using a new `docker-compose.federation.yml`. Findings are documented to a known location; any surfaced bugs in peer replication or credential delivery become separate phases under whoever owns those subsystems. The smoke test is **non-blocking** — Assured Identity v1 ships even if cross-peer reveals issues, because the issues are not in this feature's scope.

Feature 106's design says credentials are sealed into peer-replicated disclosures and detected by an `InboundCredentialDetector` running on every peer holding the register. Unit tests prove the writer-reader contract on a single node. The actual cross-peer end-to-end has never been exercised — `MASTER-TASKS.md` Theme 6 ("Cross-node verification") and `DEFERRED-E2E.md` tasks T047/T048 are explicit about this. This is the largest unproven architectural assumption in the platform's credential story.

Bundling the smoke test into Assured Identity v1 retires that risk without coupling its ship date to peer-replication bug fixes.

**Rejected alternatives:**
- Block v1 on cross-peer correctness — couples ship date to a separate subsystem's reliability.
- Defer cross-peer entirely — leaves the largest architectural assumption unchecked indefinitely.

## Cluster A — Renderer additions

These land in `Sorcha.UI.Core` and benefit every blueprint, not just Assured Identity.

### A.1 — DOB future-block

`DateTimeRenderer.razor` currently renders a `MudDatePicker` and validates `minLength` / `maxLength` / `pattern` / `enum` for strings, but ignores `formatMinimum` and `formatMaximum`. The Sorcha date token vocabulary (`today`, `today-{N}{D|M|Y}`, `today+{N}{D|M|Y}`) is fully implemented in `SorchaDateTokenResolver.cs` for server-side use. The fix wires the same resolver into `DateTimeRenderer.razor` so client-side date selection is bounded — for DoB fields with `formatMaximum: "today"`, future dates are not pickable in the calendar.

Falls back gracefully when no token-eligible bounds are present. Must not break existing `DateTime` field renders.

### A.2 — Photo capture dispatch

`FileRenderer.razor` in `Sorcha.UI.Core` is the field type the form schema dispatches to for `string` fields with `format: "file-reference"`. It currently uses a plain `<InputFile>` without camera-capture support. The legacy `FileReferenceField.razor` in `Sorcha.UI.Web.Client` has `<InputFile capture="environment" accept="image/*">` for mobile camera access — that capability needs to be promoted into the core renderer.

For Assured Identity specifically, the photo field carries new schema extensions:

```jsonc
"portrait": {
  "type": "string",
  "format": "file-reference",
  "x-file": {
    "accept": ["image/jpeg", "image/png"],
    "maxSizePerFile": "5MB",
    "maxChunks": 1,
    "capture": "user",
    "embedAs": "image-token-jpeg-240x320"
  }
}
```

`x-file.capture: "user"` advises the renderer to default to the front-facing camera. `x-file.embedAs: "image-token-jpeg-240x320"` advises the renderer to client-side resize to a 240×320 JPEG before submission and to send both the original and the resized token. The submission shape carries a `chunkTransactionIds` for the original (existing chunked-file pattern) and a small `tokenImageBase64` for the credential claim. ICAO composition tips render as a sibling panel.

### A.3 — `x-review` schema extension and `ReviewSummaryRenderer`

A new schema extension marks a wizard page as a read-only summary:

```jsonc
{
  "x-review": {
    "layout": "id-card",
    "editable": true,
    "header": {
      "issuerName": "Government of Scotland",
      "credentialName": "Assured Identity"
    }
  }
}
```

The renderer treats a page with `x-review` differently from a normal page:

- It does not render any form fields. Instead, it iterates the action's prior pages and pulls submitted values from the bound form context.
- It hands rendering to `ReviewSummaryRenderer.razor`, which dispatches to the named layout variant (`id-card` is the v1 variant; `passport-page`, `tabular`, `receipt` are reserved for future variants).
- When `editable: true`, it generates Edit-X buttons next to each section, wired to navigate the wizard back to the originating page with all data preserved.
- When the same extension is rendered for an assessor-side action (decided by the action context, not the schema), it shows the same card with PENDING watermark and Approve/Reject actions derived from the action's routes.

The `id-card` variant is parameterised by a small palette config so the DLA driving-licence card can use a pink theme (UK convention) without a bespoke component.

## Cluster B — The blueprints

### B.1 — `assured-identity.json`

Three actions: open citizen submission (5-page wizard), assessor approval (x-review id-card), citizen claim (x-credential-offer claim card). Credential is `AssuredIdentityCredential` with claims `givenName`, `middleName?`, `familyName`, `fullName` (derived), `dateOfBirth`, `email`, `address` (structured), `portrait?`. All claims selectively disclosable. Core schema components referenced via `$ref`: `PersonName/v1`, `DateOfBirth/v1`, `EmailAddress/v1`, `PostalAddress/v1`. All scalar text fields carry `x-persona` bindings.

The citizen participant has no `walletAddress` at publish time (open / late-bound, per Feature 103). The gov-assessor participant has a known wallet at publish time.

### B.2 — `driving-licence.json` (Phase 2)

Four actions: open citizen submission of vehicle class (2-page wizard: Vehicle Class + Review), DLA OID4VP presentation request for `AssuredIdentityCredential`, DLA issuance of `DrivingLicenceCredential`, citizen claim. The presentation request asks for `givenName`, `familyName`, `dateOfBirth`, `portrait` — the citizen withholds `email` and `address`. The licence credential carries `licenceNumber`, `vehicleClass`, `issuedDate`, `expiryDate`, `holderName`, `holderDateOfBirth`, `portrait?` (carried forward from disclosure). 10-year expiry.

The DLA issuance action's review screen renders two stacked id-cards (presented identity above, licence-to-be below) — this is the genuinely new pattern in Phase 2 and the visual proof that `x-review` scales to credential-chain workflows.

## Cluster C — Validator hook

The `gov-assessor` and `dla-officer` participants are filled at runtime by `sorcha-agent` processes started by `run.ps1`. Each agent's config declares `mode: "rules"` with a single rule that approves the assessor's review action. AI-mode and external-API-mode are reserved for v1.1.

The agent reads the action payload (including the photo's full-resolution evidence URI) via the existing My Actions inbox. The blueprint's review action carries a normal decision schema (decision enum, optional reason). No special platform hook is required.

## Cluster D — Walkthrough

```
walkthroughs/AssuredIdentity/
├── README.md
├── setup.ps1                           # Government org, DLA org, citizen wallet, both blueprints
├── run.ps1                             # Phases 1+2 end-to-end (single peer, primary demo)
├── run-phase1-identity.ps1
├── run-phase2-licence.ps1
├── run-multi-peer.ps1                  # Smoke, non-blocking
├── blueprints/
│   ├── assured-identity.json
│   └── driving-licence.json
├── actors/
│   ├── citizen.json                    # Filesystem HAIP wallet-dir; receives + presents credentials
│   ├── gov-assessor.json               # Rules-mode, approves identity
│   └── dla-officer.json                # Rules-mode, approves licence
├── data/
│   └── sample-portrait.jpg             # ICAO-compliant test photo
```

Actors are stateless and span both phases — the citizen actor receives the AssuredIdentityCredential in Phase 1 and presents it in Phase 2 via OID4VP without script-level state ferrying.

### Which delivery path `run.ps1` exercises

The platform supports both register-native and HAIP external delivery (holder chooses at claim time via the Wave 14b card). The two walkthrough entry points exercise different paths deliberately, so together they prove both modes:

| Script | Delivery path | Why this path |
|---|---|---|
| `run.ps1` (single-peer, primary demo) | HAIP external wallet-dir | Phase 2's OID4VP presentation requires a HAIP-compatible filesystem wallet. `sorcha-agent haip present --wallet-dir <dir>` reads SD-JWT credentials from disk, which is where the Wave 14b "Scan with external wallet" path lands them. Using the HAIP path in `run.ps1` keeps Phase 2 working end-to-end with existing agent commands. |
| `run-multi-peer.ps1` (smoke) | Register-native (SorchaLocalWallet) | Cross-peer replication is specifically the register-native value proposition — HAIP external wallet-dir credentials do not replicate through the register. The smoke test must use register-native to actually exercise `InboundCredentialDetector` + `InstanceMirrorReconstructor` on peer B. |

The multi-peer script is Phase 1 only (register-native claim + MyCredentials PENDING assertion on node B). Phase 2 is not exercised multi-peer because Phase 2's value prop is the credential chain, not cross-peer replication — and mixing the two would obscure what the smoke test is actually measuring.

**Future work (not v1):** A bridge that exports a Sorcha-wallet-stored credential to a filesystem wallet-dir on demand would let Phase 2 consume a register-native-delivered credential directly. That's one of the natural v1.1 additions.

## Cross-peer smoke test

`run-multi-peer.ps1` brings up `docker-compose.federation.yml` (a new compose file with two Sorcha node stacks subscribed to the same register). It runs Phase 1 (register-native delivery) with the issuer (Government org) on node A and the citizen on node B. Asserts: the AssuredIdentityCredential lands in the citizen's Sorcha wallet on node B, surfaces in the PENDING tab of MyCredentials, and the citizen can Accept to transition it to Active — all within 30 seconds of issuance on node A. Findings (pass / fail / latency / any replication anomalies) are logged to `walkthroughs/AssuredIdentity/multi-peer-findings.md`. Failures do not block ship.

This subsumes T047 (cross-node delivery latency) and T048 (cross-node Accept signature verification) from `DEFERRED-E2E.md`.

## Testing strategy

| Layer | Coverage |
|---|---|
| Renderer unit | `ReviewSummaryRenderer` state and edit-jump; DOB token resolution against `SorchaDateTokenResolver`; photo client-side resize to 240×320 token; `x-review` extension parser |
| Engine unit | `BuildClaimsFromMappings` includes new `portrait` claim; core `$ref` resolution inlines `PersonName/v1` etc. correctly into the published blueprint |
| Walkthrough E2E | `run.ps1` is the primary functional proof for both phases, single peer |
| Cross-peer | `run-multi-peer.ps1` per release, findings documented |
| Playwright screenshots | Deferred to v1.1 (until needed) |

## Out of scope for v1 (explicitly deferred)

- Liveness detection on the selfie (face movement, blink challenge)
- Automated document verification (passport / existing ID upload + extraction)
- Real backend identity-validator API integration (vendor selection, integration plumbing)
- AI-mode agent (Claude vision reading the submitted photo)
- Additional `x-review` layout variants (`passport-page`, `receipt`, `tabular`)
- `nationality` and `phone` fields on the credential
- Issuer-org custom branding (per-org logos, palette, seal designs)
- Verified social profiles on the contact page
- Per-issuer credential template overrides (custom card layouts beyond colour theme)
- Bulk-issuance flows (employer-side workforce identity)

Each is a natural v1.1+ addition and none are precluded by the v1 design shape.

## What's being deleted

| File / folder | Action |
|---|---|
| `walkthroughs/HaipVerifiedCitizen/` | Delete entirely |
| `walkthroughs/HaipDrivingLicence/` | Delete entirely |
| `VerifiedCitizenCredential` (as a credential type) | Removed by walkthrough deletion — the type is schema-first and has no C# class |
| `AssuredPersonCredential` (as a credential type) | Removed by walkthrough deletion — same |

`walkthroughs/HaipIdentityAttestation/` is **kept** — it proves the bare `sorcha-agent haip receive` CLI and is not a blueprint walkthrough.

The historical specs `specs/103-verified-citizen-v2/`, `specs/104-credential-claim-action/`, and `specs/106-register-native-credentials/` are **kept** as historical context. This spec links to them.

## Implementation phasing (for the writing-plans handoff)

Suggested phase ordering (writing-plans will refine):

1. **Renderer additions (Cluster A)** — DOB future-block, photo capture dispatch, `x-review` extension and `ReviewSummaryRenderer`. Each is independent and benefits other blueprints.
2. **AssuredIdentity blueprint** — `assured-identity.json`, citizen-side actor, gov-assessor actor, Phase 1 walkthrough scripts.
3. **Driving Licence blueprint** — `driving-licence.json`, dla-officer actor, Phase 2 walkthrough scripts.
4. **Cross-peer smoke** — `docker-compose.federation.yml` + `run-multi-peer.ps1` + findings doc.
5. **Cleanup** — delete `HaipVerifiedCitizen/` and `HaipDrivingLicence/`, update `MASTER-TASKS.md`, update walkthrough README index.

## Acceptance criteria

The v1 ships when:

- A new public-org user can sign up, log in, run the Assured Identity wizard end-to-end, see the ID-card preview on Page 5, submit, watch the sorcha-agent approve, and claim the credential into their local Sorcha wallet via the Wave 14b claim card.
- The same user can then run the Driving Licence Phase 2, present `AssuredIdentityCredential` via OID4VP (selectively disclosing only `givenName`, `familyName`, `dateOfBirth`, `portrait`), and claim a `DrivingLicenceCredential` into the same wallet.
- The DOB picker on Page 1 will not allow a future date to be selected.
- The optional photo on Page 4 captures via mobile camera, resizes client-side to 240×320 token, and lands as `portrait` in the issued credential's SD-JWT.
- The persona-filled values from a logged-in user's stored persona land identically in the issued credential's claims (verified — see existing test coverage).
- `run-multi-peer.ps1` produces a findings document, regardless of pass / fail.
- `walkthroughs/HaipVerifiedCitizen/` and `walkthroughs/HaipDrivingLicence/` no longer exist.

## References

- Feature 103 (Verified Citizen v2): `specs/103-verified-citizen-v2/`, `docs/superpowers/specs/2026-04-13-verified-citizen-v2-design.md`
- Feature 104 (Credential Claim Action / Wave 14b): `specs/104-credential-claim-action/`
- Feature 106 (Register-Native Credentials): `specs/106-register-native-credentials/`, `docs/superpowers/specs/2026-04-14-register-native-credential-delivery-design.md`
- ISO/IEC 19794-5 (biometric portrait token image)
- ISO 18013-5 (mobile Driving Licence — `portrait` attribute precedent)
- ICAO Doc 9303 (passport photo composition)
- UK GDS Service Manual (one-thing-per-page wizard pattern)
