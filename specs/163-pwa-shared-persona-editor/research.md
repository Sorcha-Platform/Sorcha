# Phase 0 Research: PWA Shared Persona/Profile Editor

All NEEDS CLARIFICATION items from Technical Context were resolved by inspecting the
existing codebase. No external/web research was required — this is a composition +
DI-wiring change over an already-built persona stack.

---

## Decision 1 — Where the shared editor lives

**Decision**: Add a new `PersonaEditor` component at
`src/Apps/Sorcha.UI/Sorcha.UI.Components.User/Components/Persona/PersonaEditor.razor`,
namespace `Sorcha.UI.Core.Components.Persona`.

**Rationale**: `Sorcha.UI.Components.User` is the canonical home for user-facing components
shared between the web app and the PWA (Feature 122). Its `RootNamespace` is `Sorcha.UI.Core`
(load-bearing — consumers `using Sorcha.UI.Core.*`). The PWA references this project directly;
`Sorcha.UI.Core` re-exports it via ProjectReference so the web host picks it up transparently.
Placing the editor here gives both hosts the *same* component (FR-004) with no fork.

**Alternatives considered**:
- *Component in `Sorcha.UI.Core`* — rejected: not visible to the PWA (PWA deliberately does **not**
  reference UI.Core to keep designer/admin code and `Blazor.Diagrams`/`YamlDotNet` out of its bundle).
- *Duplicate component per host* — rejected: violates companion-first / FR-004 and creates drift.

---

## Decision 2 — Reuse the existing service/client/model layer as-is

**Decision**: Do **not** create new persona services or models. Reuse:
- Models: `PersonaReadModelV1`, `PersonaAttributesV1`, `PersonaEmail`, `PersonaPhone`,
  `PersonaPhoneKind`, `PersonaAddress`, `PersonaAttribute<T>` (namespace `Sorcha.Tenant.Models.Persona`).
- Service: `IPersonaService` / `PersonaService` (cache + autofill pref).
- Client: `IPersonaClient` / `PersonaHttpClient` (`GET|PUT|DELETE /api/me/persona`).
- Exceptions: `PersonaValidationException` (400), `PersonaWalletNotProvisionedException` (409).

All already live in `Sorcha.UI.Components.User/Services/User/Persona/` (namespace
`Sorcha.UI.Core.Services.Persona`).

**Rationale**: The spec (Assumptions) states the server-side persona capability is unchanged and
the gap is purely that the PWA does not wire it up. These services are battle-tested in the web host.

**Alternatives considered**: *Rewrite a PWA-specific persona client* — rejected: spec explicitly
forbids a PWA fork; the existing client already throws the typed exceptions the editor needs.

---

## Decision 3 — Extract the form from `MyProfile.razor`, host pages go thin

**Decision**: Move the form markup + edit/save/delete logic out of
`Sorcha.UI.Web.Client/Pages/MyProfile.razor` into `PersonaEditor`. Reduce both
`MyProfile.razor` (web, `@page "/profile"`) and `Sorcha.Wallet.Pwa/Pages/Profile.razor`
(PWA, `@page "/profile"`) to thin hosts that render `<PersonaEditor/>` inside their own
`PageTitle` / layout / `[Authorize]` shell.

**Rationale**: Spec Assumption — post-#1037 the canonical existing editor is the web My Profile
page (the standalone `CompleteProfileStep` no longer exists). Extracting from the live page
honours "reuse the existing form, don't rewrite it" and guarantees field/validation parity (FR-004,
SC-003). Both pages already use the same route `/profile` within their respective hosts.

**Component surface**: `PersonaEditor` injects `IPersonaService` + `IInlineFeedback` + `ILogger`,
internally owns load (`OnInitializedAsync` → `GetAsync` + `HydrateFromRead`), the mutable form
fields, save (`UpdateAsync` + `SetAutofillEnabledAsync`), and delete. Feedback is surfaced via
`IInlineFeedback`, which renders through `InlineFeedbackHost` mounted in each host's layout
(present in both web and PWA). No host-specific behaviour leaks into the component.

**Alternatives considered**:
- *Pass services in as `[Parameter]`s* — rejected: both hosts resolve the same DI services; injection
  inside the component is simpler and matches existing patterns.
- *Keep per-context (Feature 125) persona in PWA* — rejected: spec FR-012 says "own profile in current
  context, consistent with existing web behaviour"; web uses the self persona via `IPersonaService`.
  Keep scope to that; do not introduce `IPerContextPersonaCache` here.

---

## Decision 4 — PWA DI registration (the actual defect fix)

**Decision**: In `Sorcha.Wallet.Pwa/Extensions/ServiceCollectionExtensions.cs`
(`AddCitizenWalletServices`), add:
1. `services.AddHttpClient<IPersonaClient, PersonaHttpClient>(c => c.BaseAddress = new Uri(gatewayBaseAddress))`
   `.AddHttpMessageHandler<BearerTokenHandler>().AddHttpMessageHandler<ServerClockHandler>();`
   — same authenticated chain as `ICitizenWalletClient`, so the consumer-tier JWT reaches `/api/me/persona`.
2. `services.AddScoped<IPersonaService, PersonaService>();`
3. `services.AddBlazoredLocalStorage();` **(critical — see Decision 5)**.

**Rationale**: The web host registers `IPersonaClient` (authenticated) + `IPersonaService`. The PWA
registers neither — this is exactly why PWA saves fail (FR-011). The PWA's established pattern for
authenticated typed clients is `AddHttpClient<TInterface, TImpl>` + `BearerTokenHandler` +
`ServerClockHandler`; `PersonaHttpClient`'s `(HttpClient, ILogger)` constructor fits typed-client
registration directly (no manual handler wiring needed, unlike the web host's hand-rolled factory).

**Alternatives considered**: *Replicate the web's manual `AuthenticatedHttpMessageHandler` factory* —
rejected: the PWA uses `BearerTokenHandler`/`ServerClockHandler` message handlers, not the web's
`AuthenticatedHttpMessageHandler`; using the PWA-native chain keeps clock-skew handling and token
injection consistent with every other PWA client.

---

## Decision 5 — The hidden transitive dependency: `ILocalStorageService`

**Decision**: Register `AddBlazoredLocalStorage()` in the PWA host.

**Rationale**: `PersonaService`'s constructor depends on `Blazored.LocalStorage.ILocalStorageService`
(used for the autofill preference). The web host registers Blazored; the **PWA does not** (verified:
no `Blazored`/`ILocalStorageService` reference anywhere under `src/Apps/Sorcha.Wallet.Pwa/`). Without
this, `IPersonaService` would fail to resolve at runtime in the PWA — a textbook "compiles, works on
web, throws on PWA" regression. This is precisely the failure class FR-014 / SC-005 require us to guard.

**Validation**: The PWA DI activation test (`PersonaDiActivationTests`) resolves `IPersonaService`
from a service provider configured exactly like the PWA host, failing if any dependency (client,
local storage, logger) is missing.

**Alternatives considered**: *Make `PersonaService`'s local-storage dependency optional* — rejected:
changes shared/web behaviour for a PWA-only gap; cleaner to register the dependency the service
already declares.

---

## Decision 6 — Error handling parity

**Decision**: The editor maps the three rejection classes to distinct inline messages (preserving
entered data):
- `PersonaValidationException` (400) → field-level inline error(s), non-auto-dismissing.
- `PersonaWalletNotProvisionedException` (409) → distinct provisioning message.
- Any other exception / network failure → "save did not complete, retry" message.

**Rationale**: Directly satisfies FR-007/008/009 and SC-004. This logic already exists in
`MyProfile.razor`'s `HandleSave`/`OnInitializedAsync`; it moves into the component verbatim so both
hosts inherit identical behaviour.

**Alternatives considered**: none — this is lift-and-shift of proven logic.

---

## Decision 7 — Test strategy

**Decision**:
- **Shared editor tests** → `tests/Sorcha.UI.Core.Tests/Components/Persona/PersonaEditorTests.cs`,
  using `ComponentTestFixture` (bUnit `BunitContext` + `AddMudServices` + `JSRuntimeMode.Loose`),
  mocking `IPersonaService` and `IInlineFeedback`. Cover: load populates fields; "no persona" → empty
  form (not error); edit + save calls `UpdateAsync` with the expected `PersonaAttributesV1`;
  validation rejection surfaces inline + preserves input; provisioning rejection surfaces distinct
  message.
- **PWA activation test** → `tests/Sorcha.Wallet.Pwa.Tests/Services/PersonaDiActivationTests.cs`:
  build a service collection with the PWA registrations and assert `IPersonaService` resolves and a
  `PersonaEditor` renders under that container (FR-014/SC-005).

**Rationale**: The component tests prove behaviour once for both hosts; the activation test is the
specific guard against the DI regression that motivated this feature.

**Alternatives considered**: *Playwright E2E only* — rejected: slower, doesn't isolate the DI-resolution
guarantee; bUnit + a focused activation test is the established pattern in this repo.
