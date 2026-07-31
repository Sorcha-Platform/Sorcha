# Sorcha.UI.Components.User

Shared user-facing Razor component library consumed by both the Sorcha.UI web app family and the `Sorcha.Wallet.Pwa` PWA.

## Why this library exists

The PWA cannot afford to ship admin / designer / explorer code. Before Feature 122, `Sorcha.UI.Core` was the only Razor library and bundled `Z.Blazor.Diagrams`, `YamlDotNet`, and ~3.26 MB of designer-flavoured assembly into anything that referenced it. This library carries only what user-facing components need so the PWA's bundle stays clean.

The web app family transitively picks up this library via `Sorcha.UI.Core`'s ProjectReference — no host-app csproj changes were needed when the extraction landed.

## What lives here

| Surface | Examples |
|---|---|
| Components | `Forms/` (full schema-driven form renderer), `Credentials/` (13 cards / dialogs), `Wallet/` (transaction detail, receipt proof, lifecycle ticks), `Participants/` (6 components), `Shared/` (`ConfirmDialog`, `EmptyState`, `JsonTreeView`, etc.) |
| Services | `Services/User/*` (AddressLookup, Credentials, Forms, Participants, Persona, Wallet), `Services/Shared/*` (Authentication, Blueprints, Configuration, Encryption, Http, Identity, Navigation, Organization) |
| Models | `Models/User/*` and `Models/Shared/*` |
| Helpers | `Extensions/Shared/JsonDefaults`, `Utilities/Shared/{MimeTypeIconHelper, UrlValidator}` |

## What deliberately does NOT live here

- Admin / Designer / Explorer / Configuration / Encryption admin pages and their services — these stay in `Sorcha.UI.Core` because they justify UI.Core's continued existence.
- `Z.Blazor.Diagrams` and `YamlDotNet` — designer-only transitive deps. The csproj **must not** add these even if a moved file happens to compile-time reference them; that signals a misclassification.
- `BreadcrumbNav`, `UserProfileMenu`, `LogoutConfirmDialog` — web-chrome with auth assumptions the PWA does not share.

## Namespace policy (load-bearing)

The csproj sets `<RootNamespace>Sorcha.UI.Core</RootNamespace>` so every file moved from `Sorcha.UI.Core.*` namespaces keeps its declarations verbatim. The audience signal lives in the **folder**, not in the namespace. Consumer `using` directives across the six web host apps stay valid.

When you add a new file here, declare the same `Sorcha.UI.Core.{Components,Services,Models}.{Foo}` namespace you would have declared in UI.Core; place the file under the audience subfolder it belongs to (`User/`, `Shared/`).

## Audience-tag convention (Feature 123)

Inherited from `Sorcha.UI.Core/README.md`. Recap:

- `Services/User/` — services consumed only by user-facing components/pages.
- `Services/Shared/` — services consumed by both audiences (both web admin code and PWA user code).
- `Services/Admin/` — admin-only services. Stays in `Sorcha.UI.Core`.

Same partition for `Models/`, `Extensions/`, `Utilities/`.

### Bi-modal smell detector

If you find yourself writing a service interface that mixes a user-read operation with an admin governance operation, **split it**. The 2026-05-11 attempt at Feature 122 Phase 2 collapsed under exactly this pattern with `IRegisterService`; the recovery (Feature 123) split it into `IRegisterReadService` + `IRegisterGovernanceService`. The same smell test applies to new interfaces.

## Bundle hygiene

The PWA bundle must not contain `Blazor.Diagrams*`, `YamlDotNet*`, or `Sorcha.UI.Core*` assemblies. CI asserts this — see `scripts/check-pwa-bundle.ps1`. When you add a new PackageReference here, ask: does the PWA need this? If no, the dependency belongs in `Sorcha.UI.Core` not here.

## InternalsVisibleTo

This library grants `InternalsVisibleTo` to:

- `Sorcha.UI.Components.User.Tests` — for component-test access
- `Sorcha.UI.Core` — for cross-library internal helpers like `ReviewSummaryDataSource.PointerFromFieldName` that UI.Core still calls
- `Sorcha.UI.Core.Tests` — for legacy test access during the migration

The UI.Core grant is intentional: UI.Core is privileged here because it shipped as Components.User's sibling and its consumers transitively expect the same access surface they had pre-extraction.

## Where to add new code

| You're adding... | Put it here |
|---|---|
| A user-facing Razor component | `Components/{Forms,Credentials,Wallet,Participants,Shared}/` |
| A service consumed only by user-facing code | `Services/User/{subject}/` |
| A service consumed by both audiences | `Services/Shared/{subject}/` |
| A service used only by admin/designer/explorer | `Sorcha.UI.Core/Services/Admin/{subject}/` |
| A view-model record used by user-facing components | `Models/User/{subject}/` |
| A protocol or domain model shared by both audiences | `Models/Shared/{subject}/` |

## Verification presets

`Services/User/Verification/DefaultPresetCatalogue.cs` is the catalogue **every** verify surface
drives — the standalone `Sorcha.Verifier` kiosk, the web `/app` client, and the wallet PWA. A
credential type absent from it **cannot be requested by any surface**, however well its issuance
works.

The catalogue is config-driven: a `VerifierPresets` section overrides the builtin set. Builtin
presets today:

| Key | Credential | Required | Optional |
|---|---|---|---|
| `age-over-18` | Assured Identity | `age_over_18`, `portrait` | — |
| `confirm-identity` | Assured Identity | `fullName`, `portrait` | `dateOfBirth` |
| `cyber-level` | Cyber Level (AIAS M3) | `level`, `portrait` | `givenName`, `familyName` |

> **New credential types belong in `Builtin`, not in per-deployment config.** Two of the three
> surfaces (the web `/app` client and the wallet PWA) are Blazor **WebAssembly**, so their
> configuration is a `wwwroot/appsettings.json` baked into the image — editing it means rewriting a
> static file inside a container, not turning a deployment knob. The kiosk sets no `VerifierPresets`
> either, so in practice every surface runs on the builtin set. Config remains the right mechanism
> for a *deployment* that wants to replace the question set wholesale.

**A preset must only request claims the credential actually carries.** Ask for one it doesn't and
the request is unsatisfiable — the holder is told "none of your credentials match" for a credential
they genuinely hold. `AssuredIdentityPresetRegressionTests` and `CyberLevelPresetRegressionTests`
(both in `Sorcha.Wallet.Pwa.Tests`) guard this by running each preset through the real
`DcqlRequestBuilder` → `PresentationEngine.MatchQuery` path rather than asserting the catalogue
merely contains an entry — which would not catch wrong claims or a subtly wrong `vct`.

**One preset names exactly one credential type.** `VerificationPreset.RequiredVct` is a single
string, so an operator must pick the exact credential up front and a holder who has A but was asked
for B simply fails. Widening this to alternatives is
[#1351](https://github.com/Sorcha-Platform/Sorcha/issues/1351) / TRUST-011 — the engine underneath
already supports it (`credential_sets`), and note that a *truly* generic "any credential" ask is not
legal under OpenID4VP 1.0.

## EnrolGate (Feature 126)

`Components/EnrolGate/` houses the council-page cold-start onboarding gate from Spec 3 of the Strathcarron citizen arc. Drop-in component that any council page wraps its application form in — handles all three citizen tiers (cold-start / mini-gate / fast-path) transparently.

### Consumer-side API

```razor
@using Sorcha.UI.Core.Components.EnrolGate

<EnrolGateComponent CouncilName="Strathcarron Council"
                    ServiceLabel="driving licence application"
                    OnReady="@HandleCitizenReadyAsync">
    <!-- the application form goes here; renders only after the gate clears -->
    <DrivingLicenceForm />
</EnrolGateComponent>

@code {
    private Task HandleCitizenReadyAsync(Guid? platformUserId)
    {
        // Optional: hydrate any form state from sessionStorage,
        // record the platformUserId for the submission payload.
        return Task.CompletedTask;
    }
}
```

`OnReady` fires once with the resolved `platformUserId` when the citizen reaches Tier 1 (signed in + at least one wallet device). The `ChildContent` block only renders after that point — until then the gate owns the visible surface.

### Sub-components

- `PreflightSignupSurface` — Tier 3 explainer + "Sign in or create your account" CTA carrying `?returnUrl=<currentUrl>`. No QR rendered at this stage (FR-004).
- `WalletPairingSurface` — Tier 2 mini-gate AND Tier 3 post-signup. Calls `POST /api/auth/enrol-session` at init, renders `HybridQrAffordance`, subscribes to `IEnrolPairingSignal.OnDeviceEnrolled`. The `Mode` parameter (`TierMode.MiniGate` vs `TierMode.PostSignup`) selects copy. Surfaces a regenerate affordance when the session token expires without a pairing signal.
- `HybridQrAffordance` — QR + tap-link + copy-link. The `Layout` parameter (`HybridQrLayout.Auto` / `QrFirst` / `LinkFirst`) controls prominence. `Auto` emits a CSS class so `@media (max-width: 600px)` can swap ordering for mobile users without an IJSRuntime probe.

### Services

- `ITierProbeService` / `HttpTierProbeService` — parallel `/whoami` + `/me/devices` probes with 200 ms timeout. Returns `CitizenTier.ColdStart` / `MiniGate` / `FastPath`.
- `IEnrolPairingSignal` / `EnrolPairingSignal` — composes `TenantHubConnection.OnDeviceEnrolled` (SignalR, primary) with a 3 s `/me/devices` polling fallback. Manual-recovery affordance fires after 60 s of no signal.

Both register through the consuming web shell's `Program.cs`:

```csharp
builder.Services.AddHttpClient<ITierProbeService, HttpTierProbeService>(client =>
{
    client.BaseAddress = new Uri(builder.HostEnvironment.BaseAddress);
});
builder.Services.AddHttpClient<IEnrolPairingSignal, EnrolPairingSignal>(client =>
{
    client.BaseAddress = new Uri(builder.HostEnvironment.BaseAddress);
});
```

### PWA-side counterpart

The PWA owns `Pages/Enrol.razor` (the `?session=<token>` redeem entry) and the `EnrolmentRedeemConfirmDialog` that surfaces the bound user's email + display name before any device-pairing call runs — the friend-scans-by-mistake mitigation per FR-010 / FR-011.
