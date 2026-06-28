# Phase 1 Data Model: Shared verify components (B2-components relaunch)

This wave adds two types and relocates two; it does **not** change the #1045 seam contracts or the
`Sorcha.Verifier.Engine` outcome/layer models. Existing types are listed for context with their source
of truth.

---

## Consumed (existing — from #1045 / Verifier.Engine; UNCHANGED)

### `VerificationPreset` (record) — `Sorcha.UI.Components.User/Models/Verification/`
`Key, Label, Purpose, RequiredVct, IReadOnlyList<string> RequiredClaims, OptionalClaims, KnownCredentialClaims`.
The chosen-question payload raised by `QuestionSelectionPanel` and consumed by `VerificationSessionQr` and
the verdict factory.

### `IVerificationTransport` (seam) — `Sorcha.UI.Components.User/Services/User/Verification/`
- `Task<VerificationSessionStarted> StartSessionAsync(VerificationPreset question, CancellationToken ct = default)`
- `Task<VerificationSessionPoll> PollSessionAsync(string sessionId, CancellationToken ct = default)`
- `record VerificationSessionStarted(string SessionId, string QrDeepLink, string Purpose, string RequiredVct)`
- `record VerificationSessionPoll(bool IsComplete, string? VpToken, string? PresentationSubmission)`

### `IVerificationPresetCatalogue` (seam) — same folder
`GetAll()`, `GetByKey(string?)`, `BuildCustom(purpose, requiredVct, requiredClaims, optionalClaims)`.
Backed by `DefaultPresetCatalogue` + `VerifierPresetsOptions` (config section `VerifierPresets`).

### `VerificationOutcome` / `ValidationLayerResult` / `ValidationLayer` / `LayerStatus` / `IssuerSignatureStatus` — `Sorcha.Verifier.Engine/Models/VerifierSession.cs`
- `VerificationOutcome`: `Accepted: bool`, `DisclosedClaims: IReadOnlyDictionary<string, object?>`,
  `Errors: IReadOnlyList<string>`, `CompletedAt`, `IssuerSignature`, `Layers: IReadOnlyList<ValidationLayerResult>`.
- `ValidationLayerResult`: `Layer`, `Status`, `Headline`, `Detail: IReadOnlyDictionary<string,string>` (no secrets).
- `ValidationLayer`: `LivePresentation | IssuerSignature | Revocation | RegisterAnchor`.
- `LayerStatus`: `Pass | Fail | Unverified`.

### `IVerifiablePresentationValidator` — `Sorcha.Verifier.Engine`
`Task<VerificationOutcome> ValidateAsync(VerifierSession, string vpToken, string? delegationCredential, CancellationToken)`.
Produces the outcome the verdict view model is built from (client-side, R-001).

---

## New

### `NotConfiguredVerificationTransport` — `Sorcha.UI.Components.User/Services/User/Verification/`
Default `IVerificationTransport`. Not-configured sentinel (R-002):
- `StartSessionAsync` → `new VerificationSessionStarted(SessionId: "", QrDeepLink: "", Purpose: question.Purpose, RequiredVct: question.RequiredVct)`.
- `PollSessionAsync` → `new VerificationSessionPoll(IsComplete: false, VpToken: null, PresentationSubmission: null)`.
- Never throws; holds no session. The component treats empty `SessionId`/`QrDeepLink` as "not wired" and
  renders the not-configured state without polling.

### Shared verify DI registration — `AddSorchaUserComponents(IServiceCollection, IConfiguration)` (extended)
Registers (override-friendly, R-005):
| Seam | Default impl | Lifetime / mechanism |
|------|--------------|----------------------|
| `IVerificationPresetCatalogue` | `DefaultPresetCatalogue` | `TryAddSingleton` (+ `Configure<VerifierPresetsOptions>` from `VerifierPresets`) |
| `IVerificationTransport` | `NotConfiguredVerificationTransport` | `TryAddSingleton` |
| `IRegisterAnchorClient` | `RegisterAnchorClient` | `AddHttpClient<IRegisterAnchorClient, RegisterAnchorClient>()` (guarded so host override wins) |

---

## Relocated

### `VerdictViewModel` (class) — FROM `Sorcha.Verifier/Services/` → `Sorcha.UI.Components.User/Models/Verification/` (FR-008, R-001)
Properties unchanged: `OverallPass`, `Headline`, `IssuerDid`, `IssuerDisplayName`, `PortraitBase64`,
`AgeOver18`, `Disclosed: IReadOnlyList<KeyValuePair<string,string>>`, `Withheld: IReadOnlyList<string>`,
`Layers: List<ValidationLayerResult>` (mutable — page appends the layer-4 result), `Errors`,
`RegisterAnchorId`, `CredentialId`.

**Factory change** (R-001): replace `From(VerifierSession session, VerificationOutcome outcome)` with a
form keyed on the chosen preset + outcome, e.g.
`From(VerificationPreset question, VerificationOutcome outcome)` — `RequiredVct`/`RequiredClaims` come from
`question`, `KnownCredentialClaims` (for the withheld diff) come from `question.KnownCredentialClaims`
instead of static `QuestionPresets.All`. All other parsing (`portrait`, `age_over_18`, `registerAnchor`,
`jti`) is preserved.

**State transition (verdict trail)**: `Layers` starts with the three offline layers from
`outcome.Layers` (LivePresentation, IssuerSignature, Revocation). The RegisterAnchor layer is **appended/
replaced** on demand when the operator triggers the layer-4 check (see component contract). An
`Unverified` layer-4 never flips `OverallPass` (Edge Cases; `LayerStatus.Unverified` is non-vetoing).

### `IRegisterAnchorClient` + `RegisterAnchorClient` + `RegisterAnchorResult` — FROM `Sorcha.Verifier/Services/` → `Sorcha.Verifier.Engine/` (FR-009, R-004)
- `IRegisterAnchorClient.CheckAsync(string registerId, string credentialId, CancellationToken ct = default) : Task<RegisterAnchorResult>`.
- `RegisterAnchorClient(HttpClient, IConfiguration, ILogger<RegisterAnchorClient>)` — reads
  `RegisterService:PublicBaseUrl`; GET anchor + POST verify-inclusion-proof (re-verifies, does not trust
  the read). Behaviour unchanged.
- `RegisterAnchorResult` (record): `Anchored: bool`, `Status: LayerStatus`, `TxId?`, `DocketNumber: ulong?`,
  `SealedAt: DateTimeOffset?`, `LifecycleStatus?`, `BundleJson?`, `Note?`. Maps to the layer-4
  `ValidationLayerResult` (Pass=anchored, Fail=proof invalid, Unverified=not found/unreachable).

---

## Component parameter / event surface (UI "entities")

| Component | Key `[Parameter]` inputs | Key event callbacks | Lifecycle |
|-----------|--------------------------|---------------------|-----------|
| `QuestionSelectionPanel` | (catalogue injected) | `EventCallback<VerificationPreset> OnQuestionSelected` | stateless |
| `VerificationSessionQr` | `VerificationPreset Question`, `CancellationToken CancellationToken = default` | `EventCallback<string> OnCompleted` (presentation token / vp_token) | `IAsyncDisposable`; owns linked CTS + poll loop (R-003) |
| `VerdictTrailPanel` | `VerdictViewModel Verdict` | (optional) `EventCallback OnAnchorChecked` | triggers injected `IRegisterAnchorClient` for layer-4 on demand |

Full parameter/event contracts: [contracts/components.md](./contracts/components.md). DI contract:
[contracts/di-extension.md](./contracts/di-extension.md).
