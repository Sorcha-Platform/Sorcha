# Quickstart / Validation Guide: Shared verify components (B2-components relaunch)

Run-and-observe steps that prove the feature works end-to-end. Implementation detail lives in
[data-model.md](./data-model.md) and [contracts/](./contracts/); this is the validation guide.

## Prerequisites

- .NET 10 SDK.
- **Branch base includes #1045** (R-000). Verify first:
  ```bash
  git merge-base --is-ancestor b97088d5 HEAD && echo "OK: #1045 present" || echo "MISSING: merge origin/master first"
  grep -rl "interface IVerificationTransport" src/   # must print the foundation seam file
  ```
  If missing, merge `origin/master` into `163-verify-shared-components` before proceeding.

## Build

```bash
dotnet restore
dotnet build -warnaserror   # FR-015: zero new XML-doc warnings on the touched projects
```

Expected: solution builds; no broken references after the relocations (FR-013); no duplicate
`VerdictViewModel` / `IRegisterAnchorClient` definitions remain in `Sorcha.Verifier`.

## Validate — relocation keeps existing hosts green (US5 / SC-004)

```bash
dotnet test tests/Sorcha.Verifier.Tests
```

Expected: the existing desk-verifier suite passes unchanged — the relocated `VerdictViewModel` and
`IRegisterAnchorClient` resolve from their new shared homes and the desk app still consumes them.

## Validate — components activate under shared DI (US4 / SC-001 / SC-002)

```bash
dotnet test tests/Sorcha.UI.Core.Tests --filter "FullyQualifiedName~Verification"
```

Expected to pass:
- `SharedVerifyRegistrationTests` — from a collection with only `AddSorchaUserComponents`, all three seams
  resolve to their concrete defaults (`DefaultPresetCatalogue`, `NotConfiguredVerificationTransport`,
  `RegisterAnchorClient`), and a host-registered transport overrides the stub.
- `QuestionSelectionPanelTests` — presets render as selectable options + custom affordance; selection
  raises `OnQuestionSelected` with the chosen `VerificationPreset` (US1).
- `VerificationSessionQrTests`:
  - mounts under default DI and renders the **not-configured** state without throwing or polling (US2-1);
  - with a fake transport, renders the QR/deep-link, polls pending→complete, raises `OnCompleted(vpToken)`
    (US2-2..4);
  - disposed mid-poll, cancels the loop, completes `IAsyncDisposable`, no post-disposal render / unobserved
    exception (US2-5).
- `VerdictTrailPanelTests` — headline + disclosed/withheld split + first three layers render with no
  network call; the on-demand layer-4 affordance invokes the registered `IRegisterAnchorClient` and renders
  the returned anchor status (US3).

## Manual smoke (optional, no host rewiring)

Because FR-012 forbids rewiring host pages in this wave, there is no `/wallet/verify` change to click
through. The components are exercised only via bUnit until B3 mounts them in a host. To eyeball the
not-configured render, a throwaway `.razor` test page in a scratch host that calls
`AddSorchaUserComponents` and drops `<VerificationSessionQr Question="@preset" />` will show the
"not yet wired" state — **do not commit** such a page (scope boundary to B3).

## Scope guard (SC-005)

```bash
git diff --name-only origin/master...HEAD | grep -E "wallet/[Vv]erify|Sorcha.Verifier/(Pages|Components)" \
  && echo "WARNING: host page touched — out of scope for B2" || echo "OK: no host page rewired"
```

Expected: no host page (`/wallet/verify`, desk verifier pages) modified; no legacy `VerifyFlow` /
`PresentationRequestBuilder` / `InMemoryVerifierSessionStore` removed.
