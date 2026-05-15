# Contract: SignalR `BlueprintHub.PresentationReceived`

**Service**: `Sorcha.Blueprint.Service`
**Hub**: `BlueprintHub` (existing)
**Feature**: F127
**Direction**: platform → council page (subscriber via `BlueprintHubConnection`)

## Purpose

Thin signal that a wallet has posted a verified presentation against a council page's outstanding nonce. The council page reacts by fetching `GET /api/blueprint/presentation-responses/{nonce}` to retrieve the disclosed claims.

Follows the Feature 118 thin-signal contract: **opaque ID + timestamp only, no domain payload.**

## Typed-client method

```csharp
public interface IBlueprintHubClient
{
    // … existing methods …

    /// <summary>
    /// Fired when a wallet has posted a verified presentation against an outstanding nonce.
    /// Council pages subscribed to the nonce's group should fetch
    /// <see cref="PresentationEndpoints.GetPresentationResponseAsync"/> for the disclosed claims.
    /// </summary>
    Task PresentationReceived(string nonce);
}
```

## Group semantics

Council pages subscribe to the group `BlueprintHubGroups.PresentationNonce(nonce)` immediately after minting the presentation request and before the citizen taps the QR. Group string built only via the `BlueprintHubGroups` builder class (per Feature 118 grep gate — no inline `$"presentation:{nonce}"` interpolation).

```csharp
public static class BlueprintHubGroups
{
    // … existing builders …

    public static string PresentationNonce(string nonce) => $"presentation:{nonce}";
}
```

Server publishes to the group with:

```csharp
await _hubContext.Clients
    .Group(BlueprintHubGroups.PresentationNonce(nonce))
    .PresentationReceived(nonce);
```

## Polling fallback (FR-021)

If the council page cannot establish a SignalR connection within 2 s of minting the request, `IPresentationSignal` falls back to polling `GET /api/blueprint/presentation-responses/{nonce}` on a 3 s cadence. Same shape as F126's `IEnrolPairingSignal`.

After 60 s with no signal (neither SignalR nor a successful GET), the manual-recovery affordance fires on the council page ("Couldn't reach your wallet — let's try again").

## Lifecycle

1. Council page calls `POST /api/blueprint/presentation-requests` → receives `nonce`.
2. Council page subscribes to `BlueprintHubGroups.PresentationNonce(nonce)`.
3. Citizen scans QR / taps link → wallet calls `POST /api/blueprint/presentation-responses` with the nonce + signed VP.
4. Server validates, stashes claims, publishes `PresentationReceived(nonce)`.
5. Council page receives the event, fetches `GET /api/blueprint/presentation-responses/{nonce}`, rerenders with disclosed claims.

## Authorization

Group subscription is **public** — knowledge of the nonce is sufficient. The nonce is high-entropy + short-lived; an attacker would need to know it to subscribe, and the worst case is they receive the "PresentationReceived" signal but cannot fetch claims (a hardened follow-up could bind the GET to a session cookie set when minting the request — Spec 5 work).

## Observability

- Hub-level OTel instrumentation (existing via Feature 118).
- Custom histogram `blueprint.presentation_signal.latency_ms` measuring time from `POST .../presentation-responses` receipt to `PresentationReceived` event dispatch. Primary SC-004 verification mechanism.
