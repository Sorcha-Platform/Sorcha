# Contract: SignalR `BlueprintHub.PresentationOutcomeReady`

**Service**: `Sorcha.Blueprint.Service`
**Hub**: `BlueprintHub` (existing)
**Feature**: F127 (small SignalR addition to F111's surface)
**Direction**: platform → council page subscriber (via `BlueprintHubConnection`)
**Reconciliation note**: Renamed from the pre-amendment `PresentationReceived(nonce)`. F111 ships polling on `GET /api/presentations/{id}/status`; this SignalR event is the lower-latency primary signal for the council page.

## Purpose

Thin signal that F111 has just written `presentation-outcome` (success or decline) for an outstanding `presentationRequestId`. The council page reacts by:

- On **success**: calling `GET /api/presentations/{requestId}/disclosed-claims?token={ClaimsFetchToken}` to retrieve the disclosed claims and autofill the second action's form.
- On **decline / abandonment**: calling `GET /api/presentations/{requestId}/status` to learn the lifecycle state and surface the corresponding error UX.

Follows Feature 118's thin-signal contract: **opaque ID only, no domain payload.** The disclosed claims travel only over the authenticated/token-gated REST channel.

## Typed-client method

```csharp
public interface IBlueprintHubClient
{
    // … existing F111-era methods …

    /// <summary>
    /// Fired when F111 writes a presentation-outcome record (success OR decline)
    /// for the given presentation. Council pages subscribed to
    /// <see cref="BlueprintHubGroups.PresentationNonce"/> should:
    ///   - call <c>GET /api/presentations/{id}/status</c> to learn the outcome kind, and
    ///   - on success, call <c>GET /api/presentations/{id}/disclosed-claims?token=…</c>
    ///     to retrieve the disclosed claims in plaintext for autofill.
    /// Carries opaque ID and timestamp only; no claim content crosses this wire.
    /// </summary>
    Task PresentationOutcomeReady(string presentationRequestId);
}
```

## Group semantics

Council pages subscribe to the group `BlueprintHubGroups.PresentationNonce(presentationRequestId)` immediately after submitting the `verify-identity` action and receiving the `presentationRequestId` back from F111's `InitiateAsync`. Group string built only via the `BlueprintHubGroups` builder class per the Feature 118 convention (CI grep gate enforces).

```csharp
namespace Sorcha.Blueprint.Service.Hubs;

public static class BlueprintHubGroups
{
    // … existing builders: Wallet, Instance, Org …

    /// <summary>Per-presentation group. Hosts the PresentationOutcomeReady signal so the council page that initiated the flow can react with low latency.</summary>
    public static string PresentationNonce(Guid presentationRequestId) => $"presentation:{presentationRequestId:N}";
}
```

Publish path (inside F111's `HandleOutcomeAsync`, on the success or decline write):

```csharp
await _hubContext.Clients
    .Group(BlueprintHubGroups.PresentationNonce(presentationRequestId))
    .PresentationOutcomeReady(presentationRequestId.ToString("N"));
```

## Polling fallback

If the council page cannot establish a SignalR connection within 2 s of submitting `verify-identity`, `IPresentationSignal` falls back to polling F111's existing endpoint `GET /api/presentations/{requestId}/status` on a 3 s cadence. Same shape as F126's `IEnrolPairingSignal`.

After 60 s with no signal (neither SignalR nor a successful status poll), the manual-recovery affordance fires on the council page ("Couldn't reach your wallet — let's try again").

## Lifecycle (F111-substrate)

1. Council page submits `verify-identity` action → F111 `InitiateAsync` returns `(presentationRequestId, claimsFetchToken, authorizationRequestUri)`.
2. Council page subscribes to `BlueprintHubGroups.PresentationNonce(presentationRequestId)`.
3. Council page renders `HybridQrAffordance` with the `authorizationRequestUri`.
4. Citizen's wallet scans / taps → posts signed VP to `POST /api/presentations/callbacks/sorcha-wallet/{presentationRequestId}`.
5. F111 dispatches to `SorchaWalletPresentationConsumer.VerifyAsync` → `PresentationOutcome` returned.
6. F111 writes `presentation-outcome` to the register.
7. F111 publishes `PresentationOutcomeReady(presentationRequestId)` to the group.
8. Council page receives the event, fetches lifecycle state + (on success) disclosed claims, transitions to the second action.

## Authorisation

Group subscription is **public** — knowledge of the `presentationRequestId` is sufficient. The presentationRequestId is high-entropy + short-lived; an attacker who observed it could subscribe to the group and learn the outcome timing, but the disclosed claims require the `ClaimsFetchToken` (returned only to the originator of `InitiateAsync`) to retrieve.

## Observability

- Hub-level OTel instrumentation (existing via Feature 118).
- New histogram `blueprint.presentation_outcome_signal.latency_ms` measuring time from `HandleOutcomeAsync` enter to `PresentationOutcomeReady` event dispatch. Primary SC-004 verification mechanism.
- Counter on `blueprint.presentation_outcome_signal.published{kind=success|decline|abandoned}`.
