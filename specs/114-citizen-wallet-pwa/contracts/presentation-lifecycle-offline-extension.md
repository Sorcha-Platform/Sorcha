# Presentation Lifecycle — Offline-Mode Extension

**Feature**: 114-citizen-wallet-pwa
**Extends**: Feature 111 (Timebound Presentation Lifecycle)
**File scope**: Contract changes — new IPresentationConsumer + new config field. No changes to lifecycle event schema or write path.

---

## 1. New IPresentationConsumer

```csharp
namespace Sorcha.Blueprint.Service.Services.Implementation;

using Sorcha.PresentationLifecycle.Abstractions;

public sealed class OfflinePresentationConsumer : IPresentationConsumer
{
    public string ConsumerName => "offline-oid4vp";

    public Task<PresentationOutcome> VerifyAsync(
        PresentationConsumerContext ctx,
        PresentationConsumerPayload payload,
        CancellationToken ct);
}
```

Registered in `ServiceCollectionExtensions.AddCitizenWalletConsumers` (new) and resolved by name through the existing consumer registry. No changes to `IPresentationConsumer` itself.

## 2. PresentationConsumerPayload — offline-specific shape

The payload for `offline-oid4vp` carries:

- `presentationLogEntryId` — the wallet's `id` from its local log
- `credentialId`
- `credentialJwt` — the SD-JWT VC presented (with selective disclosures applied)
- `keyBindingJwt` — the citizen's KB-JWT proof (signed by device key)
- `delegationCredential` — the device delegation credential (signed by holder key)
- `verifierDid` — `did:sorcha:verifier:{orgId}` if the verifier identifies itself, else null
- `verifierLabel` — verifier-supplied display name (untrusted, displayed only)
- `disclosedClaims` — list of disclosed claim names (for the lifecycle record)
- `presentedAt` — wallet-supplied UTC timestamp
- `verifierConfirmedAt` — when the verifier reported back, if applicable; else null
- `outcome` — one of `Presented` (wallet only), `Acknowledged` (verifier confirmed acceptance), `VerifierRejected`, `DeclinedByCitizen`
- `originatingRegisterId` — the register on which the lifecycle events should be written; nullable for the wallet-only path

## 3. Lifecycle write semantics

The consumer writes the standard Feature 111 transactions to the originating register:

| Wallet outcome | Lifecycle events written |
|---|---|
| `Presented` (wallet-only path, no verifier callback) | `PresentationInitiated` (`source=offline`, timestamps preserved) + `PresentationOutcome` (`kind=success-unverified-by-platform`) |
| `Acknowledged` (verifier confirms acceptance) | `PresentationInitiated` + `PresentationOutcome` (`kind=success`) — both written together with offline timestamps preserved |
| `VerifierRejected` | `PresentationInitiated` + `PresentationOutcome` (`kind=decline`, reason from verifier) |
| `DeclinedByCitizen` | NO transactions written (citizen never released attributes) |

**Late-arrival policy**: If `presentedAt` is older than `Blueprint.PresentationConfig.AcceptOfflinePresentationsWithinSeconds` (default 600), the consumer writes the lifecycle events but tags `kind` with the suffix `-late` (e.g. `success-late`). Verifier-side fraud-detection (out of v1 scope) can use this to discount stale outcomes.

## 4. New config field

```csharp
namespace Sorcha.Blueprint.Models;

public sealed partial class PresentationConfig
{
    /// <summary>
    /// Maximum age (seconds) for an offline presentation to be reported back to the
    /// platform and treated as a fresh lifecycle event. Older outcomes are tagged
    /// with the `-late` suffix.
    /// </summary>
    public int AcceptOfflinePresentationsWithinSeconds { get; set; } = 600;
}
```

Field is optional and additive to the existing `PresentationConfig` shape from Feature 111. Existing blueprints continue to work unchanged.

## 5. Idempotency

The consumer guards against duplicate writes using the `presentationLogEntryId` as the dedupe key. Wallet may report the same log entry more than once (e.g. retry after network blip); only one set of lifecycle transactions is written per id. Implementation uses Redis SET NX with a 24h TTL on key `sorcha:wallet:presentation-log-dedupe:{logEntryId}`.

## 6. Authorization

Wallet → Wallet Service `/wallet/presentations/log` requires the citizen's JWT (audience `sorcha:citizen-wallet`).
Wallet Service → Blueprint Service uses the existing service-to-service auth (RequireService policy from Feature 111).
External verifier → Blueprint Service callback uses the existing verifier registration mechanism.

## 7. Out of scope for this consumer

- Independent verification of the verifier's signature on the request — the wallet does this client-side; the platform only sees the outcome.
- Storing the original verifier QR — privacy-positive default; only what the wallet chose to disclose is written to the register.
- Real-time push to the verifier organisation — verifier orgs query the standard register surface for their lifecycle events as today.
