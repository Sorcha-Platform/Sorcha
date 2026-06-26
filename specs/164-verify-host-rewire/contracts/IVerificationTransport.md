# Contract: `IVerificationTransport` (consumed seam) + `HaipVerificationTransport` (new impl)

`IVerificationTransport` is the B2 seam (owned by `Sorcha.UI.Components.User`). B3 supplies the **live
implementation** `HaipVerificationTransport` and registers it on both hosts in place of the B2 default stub
`NotConfiguredVerificationTransport`. This contract pins the behaviour the shared `VerificationSessionQr`
component relies on, so the live impl is a drop-in for the stub.

> The exact C# signatures below mirror the B2 seam; if the merged B2 seam differs, conform to it — the
> **behavioural** contract (states, vp_token timing, cancellation) is the binding part.

## Interface (shape)

```csharp
public interface IVerificationTransport
{
    /// <summary>Create a presentation request for the chosen question and return a scannable session.</summary>
    Task<VerificationSession> StartAsync(VerificationPreset question, CancellationToken ct);

    /// <summary>Poll the session; returns the current state and, when complete, the raw vp_token.</summary>
    Task<VerificationSession> PollAsync(string sessionId, CancellationToken ct);
}
```

## Behavioural contract

| # | Given | When | Then |
|---|---|---|---|
| C1 | A host configured for B3 | the container resolves `IVerificationTransport` | it resolves `HaipVerificationTransport`, **never** `NotConfiguredVerificationTransport` (FR-002, SC-002). |
| C2 | The live transport | `StartAsync(question)` | returns a `VerificationSession` with non-empty `SessionId` and a scannable `QrDeepLink` — no "not configured" sentinel (FR-001, AS-US1-2). |
| C3 | An open session, holder not yet responded | `PollAsync` | returns `State == Pending`, `VpToken == null` (FR-001, AS-US1-3). |
| C4 | An open session, holder completed `direct-post` | `PollAsync` | returns `State == Complete`, non-null `VpToken` (+ `Delegation` when present) (FR-001, AS-US1-4). |
| C5 | The verifier's token tier (consumer PWA / org desk) | `StartAsync` and `PollAsync` | both accepted — not rejected on audience/tier (FR-008, AS-US1-5). |
| C6 | A transport/network fault | `PollAsync` | returns `State == Error` with detail — never hangs, never silently completes (FR-013, AS-US1-6). |
| C7 | Session TTL elapsed before holder responds | `PollAsync` | returns `State == Expired` (edge case "session expires"). |
| C8 | A cancellation requested (navigate-away) | the `CancellationToken` is cancelled | the in-flight `PollAsync`/`StartAsync` observes cancellation and stops; no leaked timer (FR-012, SC-006). |

## Identity injection

`HaipVerificationTransport` does **not** hard-code the verifier identity. It consumes a per-host identity
provider (ephemeral P-256 in the PWA via `IEphemeralVerifierIdentityService`; stable
`did:sorcha:verifier:{orgId}` in the desk app) and embeds it in the create-request so the holder sees the
correct requester (FR-005). Per-host wiring is the **only** difference between the two registrations.

## DI registration (per host)

```csharp
// After the B2 library extension (which registers NotConfiguredVerificationTransport as the default):
services.AddScoped<IVerificationTransport, HaipVerificationTransport>();   // overrides the stub
// PWA: ephemeral identity provider already registered (IEphemeralVerifierIdentityService)
// Desk: register the stable-org identity provider the transport consumes
```

A host-level test MUST assert C1 directly against the built container (not only via an end-to-end flow).
