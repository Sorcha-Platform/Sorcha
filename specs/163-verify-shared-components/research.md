# Phase 0 Research: Shared verify components (B2-components relaunch)

All NEEDS CLARIFICATION from Technical Context resolved below. Decisions are numbered R-000..R-008.

---

## R-000 — Branch base must include the #1045 foundation (BLOCKING)

**Decision**: Integrate `origin/master` (which contains `b97088d5`, PR #1045) into this branch **before**
component work begins. Preferred mechanism: merge `origin/master` into `163-verify-shared-components`.

**Rationale**: Verified facts — HEAD is `00facdbd`, cut from `8a75eb4b` (#1028). `git merge-base
--is-ancestor b97088d5 HEAD` returns false; `grep -r "interface IVerificationTransport" src/` and
`grep -r "class DefaultPresetCatalogue" src/` return nothing. The seams the spec calls "already shipped"
(`IVerificationTransport`, `IVerificationPresetCatalogue`, `DefaultPresetCatalogue`, `VerificationPreset`,
plus the `tests/Sorcha.UI.Core.Tests/Verification/DefaultPresetCatalogueTests.cs`) are physically absent
here. Building components against absent seams is impossible; re-creating them would duplicate #1045 and
violate FR-013's "no duplicate type definitions" intent.

**Alternatives considered**:
- *Re-author the seams in this wave* — rejected: contradicts the spec ("Builds on PR B2-foundation #1045")
  and creates duplicate/divergent contracts to reconcile at merge.
- *Cherry-pick only the four seam files* — rejected: brittle (misses the catalogue config + tests +
  `_ViewImports`/namespace wiring), and a later full master merge would conflict.

**Verification gate**: after integration, `grep -rl "interface IVerificationTransport" src/` returns the
foundation file and the solution builds. This is task gate #1 in tasks.md.

---

## R-001 — Verdict computed client-side: replace the `VerifierSession` input with question + outcome

**Decision**: Relocate `VerdictViewModel` to `Sorcha.UI.Components.User/Models/Verification/`, and change
its factory from `From(VerifierSession session, VerificationOutcome outcome)` to a form whose inputs are
the **chosen question** (the `VerificationPreset`'s `RequiredVct` + `RequiredClaims` + `KnownCredentialClaims`)
and the `VerificationOutcome` — not a desk-only server `VerifierSession`. Keep the existing claim-parsing
logic (`portrait`, `age_over_18`, `registerAnchor`/`RegisterAnchorId`, `jti`/`CredentialId`) and the
withheld-claims diff, sourcing "known credential claims" from the preset rather than the static
`QuestionPresets.All`.

**Rationale**: FR-011/SC-003 require the verdict be buildable in a WASM host from a presentation validated
by `IVerifiablePresentationValidator`, with no dependency on a server session store. The current `From`
only reads three things off `session` — `RequiredVct`, `RequiredClaims`, and (indirectly via
`QuestionPresets.All`) the known-claims set — all of which the shared `VerificationPreset` already carries.
`VerificationOutcome` is already WASM-safe (lives in `Sorcha.Verifier.Engine`, BouncyCastle-backed).

**Alternatives considered**:
- *Keep `From(VerifierSession, …)` and have hosts synthesize a `VerifierSession`* — rejected: drags a
  desk-only concept into the shared verdict and couples WASM hosts to a server session type.
- *Move `QuestionPresets` too* — unnecessary: the #1045 `IVerificationPresetCatalogue`/`VerificationPreset`
  already supersede the static presets; the view model reads known-claims from the chosen preset.

---

## R-002 — Default stub transport returns an explicit not-configured state (never throws, never fakes pass)

**Decision**: `NotConfiguredVerificationTransport : IVerificationTransport`. `StartSessionAsync` returns a
`VerificationSessionStarted` carrying a sentinel (empty `SessionId` and empty `QrDeepLink`) that the
component recognizes as "not wired"; `PollSessionAsync` returns `new VerificationSessionPoll(IsComplete:
false, VpToken: null, PresentationSubmission: null)`. The component checks for the sentinel and renders
the not-configured state **without** entering the poll loop.

**Rationale**: FR-004 + Edge Case "No transport wired" require an explicit non-error not-configured outcome.
A null/empty `QrDeepLink` from start is the cleanest in-band signal given the fixed #1045 record shape
(`VerificationSessionStarted(SessionId, QrDeepLink, Purpose, RequiredVct)`) — no contract change needed.
Returning (not throwing) keeps the component activatable under default DI (the central relaunch fix).

**Alternatives considered**:
- *Throw `NotSupportedException` from the stub* — rejected: re-creates the activation failure that parked
  the prior attempt (FR-007/US2/US4 demand activation succeeds).
- *Add an `IsConfigured`/status flag to the transport interface* — rejected: changes the #1045 seam
  contract, which this wave consumes but does not redesign (Assumptions). Sentinel-on-start is sufficient.

---

## R-003 — Poll lifecycle, cancellation, and `IAsyncDisposable`

**Decision**: `VerificationSessionQr` owns a linked `CancellationTokenSource` (linked to a
`[Parameter] CancellationToken`, default `none`). On "start", it begins an `async` poll loop using a
`PeriodicTimer`-driven (or `Task.Delay`-driven) cooperative wait, calling `PollSessionAsync` until
`IsComplete` or cancellation/terminal error. It implements `IAsyncDisposable`: dispose cancels the CTS,
awaits the stored poll `Task` (swallowing `OperationCanceledException`), and disposes the CTS. Renders
guard on a disposed flag so there is no post-disposal `StateHasChanged`.

**Rationale**: FR-007 + US2 scenario 5 + Edge Case "disposed mid-poll" require clean cancellation, awaited
loop completion, and no post-disposal render or unobserved exception. A linked CTS lets both an external
host token and disposal cancel the loop. Awaiting the stored task in `DisposeAsync` is what makes the
bUnit "dispose mid-poll" assertion deterministic.

**Alternatives considered**:
- *Fire-and-forget poll `Task` with only `IDisposable`* — rejected: cannot await the loop on disposal →
  unobserved exceptions and post-disposal renders (the exact anti-pattern the edge case forbids).
- *`System.Threading.Timer`* — rejected: callback-on-threadpool complicates `StateHasChanged`/render
  ordering in Blazor; an `async` loop on the renderer's sync context is simpler and testable.

---

## R-004 — Register-anchor client relocation target and DI shape

**Decision**: Move `IRegisterAnchorClient` + `RegisterAnchorClient` + `RegisterAnchorResult` into
`Sorcha.Verifier.Engine` (Common). Register it in the shared `AddSorchaUserComponents` extension via
`services.AddHttpClient<IRegisterAnchorClient, RegisterAnchorClient>()` (its existing registration shape).
It keeps reading `RegisterService:PublicBaseUrl` from `IConfiguration` and its existing logging.

**Rationale**: FR-009/FR-010 — the engine is the shared Common home both hosts already reference, and it
is WASM-safe (BouncyCastle, no P/Invoke). The `VerificationOutcome` doc already states "the verifier app
appends the RegisterAnchor layer after the anchor read (the engine performs no network I/O)" — the *client*
is the app-layer piece, and relocating it into the engine lets either host run layer-4 client-side without
coupling back to the desk app. `RegisterAnchorResult` carries `LayerStatus` (Pass/Fail/Unverified) already
matching the engine's enum.

**Alternatives considered**:
- *Relocate into `Sorcha.UI.Components.User`* — rejected: FR-009 specifies `Sorcha.Verifier.Engine`, and a
  Common engine is the more reusable home (the Blueprint service and PWA already reference the engine).
- *Leave it in `Sorcha.Verifier`* — rejected: WASM hosts could not run layer-4, defeating SC-003.

---

## R-005 — Single DI extension with override-friendly registration

**Decision**: Extend the existing (currently-stub) `AddSorchaUserComponents(IServiceCollection,
IConfiguration)` to register all three seams with `TryAdd*` semantics:
`TryAddSingleton<IVerificationPresetCatalogue, DefaultPresetCatalogue>()` (+ `Configure<VerifierPresetsOptions>`
bound to the `VerifierPresets` section), `TryAddSingleton<IVerificationTransport,
NotConfiguredVerificationTransport>()`, and `AddHttpClient<IRegisterAnchorClient, RegisterAnchorClient>()`
guarded so a host override wins.

**Rationale**: FR-005 (one call registers everything) + FR-006/US4 scenario 3 (host override wins).
`TryAdd*` is the idiomatic .NET "default unless the host already registered one" mechanism, so a host that
calls `AddSorchaUserComponents` then registers the B3 HAIP transport — or registers it first — keeps its
own. The catalogue and transport are stateless → singleton.

**Alternatives considered**:
- *Plain `Add*`* — rejected: a host override would double-register; last-wins is fragile and order-dependent.
- *Separate `AddVerifyComponents` extension* — rejected: FR-005 explicitly wants a **single** registration
  call; folding into the existing `AddSorchaUserComponents` keeps one entry point for the library.

---

## R-006 — bUnit activation through the real DI extension (not a hand-built collection)

**Decision**: Component tests build a `Services` collection, call the real `AddSorchaUserComponents` (plus
MudBlazor test services), and render through that provider — asserting activation. For the transport-driven
scenarios, the test **replaces** `IVerificationTransport` with a deterministic fake (proving overridability
too) that returns a known `VerificationSessionStarted` then a `pending → complete` poll sequence.

**Rationale**: US1–US4 "Independent Test" sections all require activation *through the shared DI
registration*, because resolving from a hand-built collection is exactly what masked the prior parked
defect. Using the real extension is the regression guard for FR-005/SC-002.

**Alternatives considered**:
- *Hand-register services per test* — rejected: would not prove the shipped extension resolves (the parked
  failure was a missing registration, not a missing type).

---

## R-007 — QR rendering reuses QRCoder (already referenced)

**Decision**: `VerificationSessionQr` renders the `QrDeepLink` as a QR using QRCoder (already a
`PackageReference` of `Sorcha.UI.Components.User`) plus the deep-link text/anchor for cross-device scan.

**Rationale**: No new dependency; QRCoder is present and is the established QR path in the library.

---

## R-008 — Test home: `tests/Sorcha.UI.Core.Tests`

**Decision**: Place the new component + DI tests under `tests/Sorcha.UI.Core.Tests/Verification/`.

**Rationale**: That project already references `Sorcha.UI.Components.User` (its `RootNamespace` is
`Sorcha.UI.Core`), already hosts the #1045 `DefaultPresetCatalogueTests.cs` under `/Verification`, and is
wired for bUnit (`Microsoft.NET.Sdk.Razor`, xUnit.v3, MudBlazor, the `Sorcha.UI.Testing` support library).
The relocation regression guard (US5) runs the **existing** `tests/Sorcha.Verifier.Tests` unchanged.
