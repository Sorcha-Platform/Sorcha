# Implementation Plan: Open Verifier PWA

**Branch**: `155-open-verifier-pwa` | **Date**: 2026-06-17 | **Spec**: [spec.md](./spec.md)

**Input**: Feature specification from `specs/155-open-verifier-pwa/spec.md`; approved design `docs/superpowers/specs/2026-06-17-open-verifier-pwa-design.md`

## Summary

Evolve `src/Apps/Sorcha.Verifier` (Blazor Server) into an installable PWA that performs
present-then-cross-check verification and renders a verdict with a four-layer, progressively-disclosed
validation trail. The presentation transport (OID4VP QR / `direct_post`) is reused unchanged. Three
things get built: (1) the verifier engine `VerificationOutcome` is **enriched with per-layer results**
(live presentation, issuer signature, revocation, register anchor) so the UI can render the trail and
distinguish "failed" from "unverified"; (2) a **new public anchor-read endpoint** on the Register
Service locates a credential's issuance transaction by its identifiers and returns the F079 inclusion
proof, enabling the open (config-free) register cross-check; (3) the **operator-facing screens** are
redesigned (question presets → QR → verdict + trail, wallet look) and the app is made installable
(manifest + service worker + install prompt, scoped under the `/verify/` mount). The AssuredIdentity
demo credential gains an `age_over_18` boolean disclosable claim and a register-anchor claim.

## Technical Context

**Language/Version**: C# 14 / .NET 10

**Primary Dependencies**: Blazor Server (InteractiveServer), MudBlazor, QRCoder, `Sorcha.Verifier.Engine`
(`IVerifiablePresentationValidator`, `IIssuerKeyResolver`, `IStatusListCache`), `Sorcha.ServiceClients.Http`
(Register Service client), existing F079 verification endpoints, F120 DID-backed issuer resolution.

**Storage**: None new in the verifier (in-memory `IVerifierSessionStore` retained). Register Service
reads existing sealed transactions; no new persistence.

**Testing**: xUnit unit tests (`Sorcha.Verifier.Tests`, engine tests, Register Service tests) + Playwright
E2E against Docker (`Sorcha.UI.E2E.Tests`, per the `sorcha-ui` skill).

**Target Platform**: Web (installable PWA) behind the API Gateway `/verify/` mount; verifier consults
public data online.

**Project Type**: Web app (Blazor Server) + portable engine library + a public read endpoint on an
existing microservice.

**Performance Goals**: Verdict displayed < 60 s from app open (SC-001), dominated by the human scan +
approve step; verification + anchor read complete in < ~2 s server-side.

**Constraints**: Reuse the existing OID4VP transport unchanged; trust runs **fully open**
(resolve-and-verify, no allowlist) with `requireIssuerSignature: true`; no offline/WASM; register +
status-list reads must reach the public gateway addresses from the verifier (issue-#808 networking class).

**Scale/Scope**: Demo-grade. ~3 redesigned screens, ~1 enriched engine outcome, 1 new public endpoint,
1 verifier-side anchor client, blueprint/walkthrough edits, PWA shell assets.

## Constitution Check

*GATE: Must pass before Phase 0 research. Re-check after Phase 1 design.*

- **I. Microservices-First** — PASS. The new anchor-read endpoint lives on the Register Service (owner of
  register data); the verifier consumes it via an HTTP client. No upward dependencies; engine stays
  dependency-free (no HttpClient) — the anchor read is performed by a verifier-app service, not the engine.
- **II. Security First** — PASS with note. The new endpoint is **intentionally public** (the "open" posture)
  but exposes only already-public register data (a credential-issuance transaction's existence + Merkle
  inclusion proof). Inputs validated (registerId/credentialId well-formed); credentialIds are high-entropy
  so enumeration risk is low; documented as an accepted, scoped exposure. No secrets. Fail-closed per layer.
- **III. API Documentation** — PASS. New endpoint gets .NET 10 OpenAPI + `.WithSummary()`/`.WithDescription()`
  + XML docs; surfaced via Scalar.
- **IV. Testing** — PASS. Unit tests for the enriched outcome mapping, the anchor client, and the new
  endpoint; Playwright E2E for the three screens + trail. Target >85% on new code.
- **V. Code Quality** — PASS. Nullable on, async I/O, DI, no Release warnings.
- **VI. Blueprint Standards** — PASS. AssuredIdentity changes are JSON edits to `credentialIssuanceConfig`.
- **VII. DDD / ubiquitous language** — PASS. Uses Credential, Disclosure, Register, Issuer, Participant.
- **VIII. Observability** — PASS. Reuse the `Sorcha.Verifier` meter; add counters for the anchor cross-check
  outcome and per-layer results. Structured logging only.

No violations → Complexity Tracking omitted.

## Project Structure

### Documentation (this feature)

```text
specs/155-open-verifier-pwa/
├── plan.md              # This file
├── research.md          # Phase 0 output
├── data-model.md        # Phase 1 output
├── quickstart.md        # Phase 1 output
├── contracts/           # Phase 1 output
│   ├── register-anchor-endpoint.openapi.yaml
│   └── verification-outcome.md
├── checklists/
│   └── requirements.md
└── tasks.md             # Phase 2 output (/speckit.tasks)
```

### Source Code (repository root)

```text
src/Common/Sorcha.Verifier.Engine/
├── Models/VerifierSession.cs            # ENRICH VerificationOutcome with per-layer results
├── VerifiablePresentationValidator.cs   # populate per-layer results (presentation, issuer, revocation)
└── (no anchor logic here — engine stays HttpClient-free)

src/Apps/Sorcha.Verifier/
├── Components/Pages/Index.razor         # REDESIGN: question presets (Age over 18?, Confirm identity, Custom)
├── Components/Pages/Outcome.razor       # REDESIGN: IdCardLayout header + four-layer validation trail
├── Components/Pages/VerifierSession.razor # minor: copy/visual alignment to wallet look
├── Services/
│   ├── QuestionPresets.cs               # NEW: preset → (vct, required/optional claims)
│   ├── IRegisterAnchorClient.cs + impl  # NEW: calls the public anchor-read endpoint, verifies proof
│   └── VerdictViewModel(s)              # NEW: maps enriched outcome → trail rows
├── Endpoints/PresentationResponseEndpoints.cs # surface enriched per-layer outcome in /status
├── Extensions/ServiceCollectionExtensions.cs  # register anchor client + ensure DID-backed issuer resolver
├── wwwroot/
│   ├── manifest.webmanifest             # NEW
│   ├── icons/ (192,512,maskable)        # NEW
│   ├── service-worker.js                # NEW (shell + offline fallback)
│   └── js/pwa-install.js                # NEW (beforeinstallprompt)
└── Components/App.razor                 # manifest link + meta + SW registration

src/Services/Sorcha.Register.Service/
├── Endpoints/VerificationEndpoints.cs   # ADD public GET anchor-by-credentialId (+ inclusion proof)
src/Core/Sorcha.Register.Core/Storage/
├── IReadOnlyRegisterRepository.cs       # ADD GetCredentialIssuanceTransactionAsync(registerId, credentialId)
└── (EF + Mongo impls)

walkthroughs/AssuredIdentity/
├── blueprints/assured-identity.json     # ADD age_over_18 claim + disclosable; anchor claim (registerId)
└── run-phase1-identity.ps1              # ensure org master key set; supply age + portrait inputs

tests/
├── Sorcha.Verifier.Tests/               # outcome mapping, anchor client, preset mapping
├── Sorcha.Register.Service.Tests/       # anchor-by-credentialId endpoint + repo method
└── Sorcha.UI.E2E.Tests/Docker/          # three-screen + trail E2E (Category: Verifier)
```

**Structure Decision**: Extend the existing verifier app + portable engine + Register Service in place;
no new project. The engine carries the per-layer verification *results*; the verifier app carries the
*anchor HTTP read* and *UI*; the Register Service owns the *public anchor data*. This keeps the engine
WASM-friendly (no HttpClient) and respects the microservice ownership boundary.
