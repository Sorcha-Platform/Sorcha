# Contract: Shared verify DI extension (FR-005, FR-006)

The load-bearing correction of this relaunch: a **single** registration call wires a concrete
implementation for every seam the three components inject, so each component activates under default DI
and is bUnit-testable. The default transport is overridable by a host.

## Entry point

```csharp
// Sorcha.UI.Components.User/Extensions/Shared/ServiceCollectionExtensions.cs
public static IServiceCollection AddSorchaUserComponents(
    this IServiceCollection services,
    IConfiguration configuration);
```

## Registration contract

| Seam | Default implementation | Mechanism | Override behaviour |
|------|------------------------|-----------|--------------------|
| `IVerificationPresetCatalogue` | `DefaultPresetCatalogue` | `services.Configure<VerifierPresetsOptions>(configuration.GetSection(VerifierPresetsOptions.SectionName))` + `services.TryAddSingleton<IVerificationPresetCatalogue, DefaultPresetCatalogue>()` | host `Add`/`TryAdd` before this call keeps its own |
| `IVerificationTransport` | `NotConfiguredVerificationTransport` | `services.TryAddSingleton<IVerificationTransport, NotConfiguredVerificationTransport>()` | host registration (B3 HAIP transport) wins (FR-006) |
| `IRegisterAnchorClient` | `RegisterAnchorClient` | `services.AddHttpClient<IRegisterAnchorClient, RegisterAnchorClient>()`, guarded (e.g. only if not already registered) | host registration wins |

## Resolution guarantees (SC-002, US4)

Given a fresh `ServiceCollection` with **only** `AddSorchaUserComponents(config)` applied and the provider
built:

1. `GetRequiredService<IVerificationPresetCatalogue>()` → `DefaultPresetCatalogue`.
2. `GetRequiredService<IVerificationTransport>()` → `NotConfiguredVerificationTransport`.
3. `GetRequiredService<IRegisterAnchorClient>()` → `RegisterAnchorClient`.
4. Mounting each of `QuestionSelectionPanel`, `VerificationSessionQr`, `VerdictTrailPanel` through that
   provider activates without a missing-service exception.
5. **Override**: if a host registers its own `IVerificationTransport` (before or after the shared call),
   the provider resolves the host implementation, not the stub.

## Verification (test contract — `SharedVerifyRegistrationTests`)

- A test calls the **real** `AddSorchaUserComponents` (not a hand-built collection), builds the provider,
  and asserts guarantees 1–3 by concrete type.
- A test asserts guarantee 5 by registering a fake `IVerificationTransport` and resolving it back.
- Component tests (C1–C3) mount through this provider to prove guarantee 4 (FR-014 / SC-001).
