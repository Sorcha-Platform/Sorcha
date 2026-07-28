# Presentation gate: one control, two transports

**Date:** 2026-07-28
**Status:** Approved (Stuart, 2026-07-28)
**Related:** F127 (credential gates), F111/F119 (presentation lifecycle), F135 (HAIP verifier),
#1324 (credential-match JSON binding), #1325 (verifier 404 handling), [#1322](https://github.com/Sorcha-Platform/Sorcha/issues/1322) (unrelated address-form defect)

## Problem

Submitting the AIAS Cyber questionnaire on the web app opens a QR dialog that polls
`GET /api/v1/verifier/requests/{id}/result` and gets 404 for the whole five-minute window, then
reports **Expired**. Observed live on n1, 2026-07-28.

The gate is `presentationSource: "SorchaWallet"`, so its request lives in Blueprint's F127
lifecycle. HAIP has never heard of it. Three independent gaps produce this:

1. **`PresentationRequestQrCard` is HAIP-only.** It injects `IHaipOfferService`, speaks
   `HaipVerificationStates`, and never branches on `PresentationSource`.
2. **The call sites don't branch either.** `NewSubmissionWorkspace.razor` and `MyActions.razor`
   open the HAIP dialog unconditionally for any `result.PresentationRequest`.
3. **The contract cannot support a branch.** `HaipPresentationRequestResponse` (server) and
   `HaipPresentationRequestInfo` (client) carry no source discriminator and no
   `ClaimsFetchToken` — even though `PresentationInitiationResult` **already carries the token**
   and `ActionExecutionService` silently drops it during mapping.

Gap 3 is the same defect class as the T032 Redis fields (#1314) and the DID-document publish
(#1318): a hand-maintained mapping loses a field, nothing verifies the join, and the failure
surfaces far away as something else.

The F127 *client* machinery already exists and is correct — `CredentialGateComponent`,
`CredentialGateInit`, `PresentationHubConnection`, the disclosed-claims fetch — but it was built
for council pages and was never wired into Sorcha's own submission flow.

## Decisions

| Decision | Choice | Rationale |
|---|---|---|
| Scope | Unify the **gate** surfaces | Verify flow stays separate; see Non-goals |
| Delivery | **One PR** | Stuart's call; contract + control + transports land together |
| `CredentialGateComponent` | **Retire and migrate** the sample portal | Strathcarron is not currently deployed, so the blast radius is small |
| Seam shape | **New `IPresentationGateTransport`** | Not a mode flag; not a reuse of `IVerificationTransport` |

### Why not reuse `IVerificationTransport`

It is verdict-shaped: `PollSessionAsync` returns a `vp_token` plus a `VerificationOutcome` for
client-side verdict computation. A gate does not want a verdict — it wants disclosed claims to
prefill a form, obtained through a single-use token. Bending that seam adds a third master to an
interface already serving two hosts.

### Why not an internal `PresentationSource` switch

That is the mode-flag shape: two protocol state machines in one component, with the single-use
token rule sitting in a branch. The two lifecycles differ in more than a URL — different state
vocabularies, claims delivered inline vs via a token-bound fetch, hub-primary vs poll-only. The
codebase has twice chosen a transport seam for exactly this (`IVerificationTransport`,
`IProximityTransport`).

## Architecture

```
PresentationRequestCard            (Sorcha.UI.Components.User — shared with the PWA)
├── owns  QR render, expiry, state display, claims table,
│         the Unreachable give-up from #1325
└── uses  IPresentationGateTransport, selected by PresentationSource

IPresentationGateTransport
├── Task<GateOutcome> WaitForOutcomeAsync(Guid requestId, CancellationToken ct)
└── Task<IReadOnlyDictionary<string, object?>?> FetchClaimsAsync(
        Guid requestId, string? claimsFetchToken, CancellationToken ct)

  SorchaWalletGateTransport   races PresentationHubConnection.PresentationOutcomeReady against a
                              3s poll of /api/presentations/{id}/status; owns the single-use
                              ClaimsFetchToken rule
  HaipGateTransport           polls /api/v1/verifier/requests/{id}/result; claims arrive inline,
                              so FetchClaims reads what the outcome already returned
```

Claims are `object?`-valued, matching F127's existing `DisclosedClaimsResponse.Claims`
(`IReadOnlyDictionary<string, object?>`). HAIP's `Dictionary<string, JsonElement>` adapts into it
for free, since a `JsonElement` boxes as `object?`; the reverse would require re-serialising. It
is lossless for non-string claims (`age_over_18` is a bool, a portrait a long base64 string). The
card formats values for display, so it renders one claims type regardless of transport.

(Corrected during planning: the design first specified `JsonElement`, which would have forced the
F127 transport to re-serialise its own response.)

`GateOutcome` is a shared enum — `Pending / Submitted / Success / Declined / Expired / Abandoned /
Unreachable` — and each transport maps its own vocabulary onto it. F127's
`abandoned-with-late-outcome` maps to `Success` (the outcome did arrive). Mapping lives in the
transport, never in the card.

**Hub is an optimisation, not a guarantee.** F119's deferred-outcome path does not publish
`PresentationOutcomeReady` yet (inline TODO in `PresentationLifecycleService.HandleOutcomeAsync`),
so the SorchaWallet transport must treat the poll as load-bearing and the hub as a latency win.

## Contract changes

**Server** — `HaipPresentationRequestResponse` → `PresentationRequestResponse` (it was never
HAIP-specific), gaining:

```csharp
public required PresentationSource Source { get; init; }   // SorchaWallet | HaipExternalWallet
public string? ClaimsFetchToken { get; init; }             // F127 single-use; null for HAIP
```

`ActionExecutionService` populates both instead of discarding them — `Source` from the matched
requirement (the local `haipRequirement` is renamed accordingly), `ClaimsFetchToken` from
`PresentationInitiationResult`.

**Client** — `HaipPresentationRequestInfo` → `PresentationRequestInfo`, mirroring both fields.

One DTO rather than two: there is one presentation request and only the transport differs. A
parallel type would duplicate five fields and give the mapping a second place to drop something.

## Data flow

Submit → server returns `AwaitingPresentation` + `PresentationRequestResponse{Source,
ClaimsFetchToken}` → `NewSubmissionWorkspace` / `MyActions` open the dialog → the card resolves its
transport from `Source` → `WaitForOutcomeAsync` → on `Success`, `FetchClaimsAsync` → claims
returned to the caller for prefill.

## Error handling

Each rule was earned by a live defect today:

- **404 / no such request** → `Unreachable`, honest message, stop polling. Never reported as
  `Expired` (#1325).
- **Transient failure** (500, network) → retry within the existing tick budget. Never conflated
  with 404.
- **Claims fetch fails after a successful outcome** → surfaced as its own state. The presentation
  *did* succeed; reporting failure would repeat the "no matching credential" lie (#1324).
- **Token reuse** — the SorchaWallet transport holds the `ClaimsFetchToken` and reuses it across
  retries, because the endpoint consumes it even on a `pending` response.

## Testing

- Transport unit tests per implementation: state mapping, 404 vs transient, token reuse on retry.
- bUnit: the card selects the correct transport from `Source`.
- Contract test (`Sorcha.UI.ContractTests`, in the style of #1324): the client DTO round-trips
  through the server's own serializer options, **and** a `SorchaWallet` initiation carrying a
  `ClaimsFetchToken` still has it after mapping. That second assertion is the one that would have
  caught this defect class three times today.
- `rehearse.ps1 -Scenario cyber` still green.
- Manual click-through on n1 — neither the rehearsal nor any unit test exercises this UI path,
  which is why the bug survived to production.

## Non-goals

- The verify flow and `IVerificationTransport` are untouched.
- [#1322](https://github.com/Sorcha-Platform/Sorcha/issues/1322) (`Postcode`/`Country` render
  defect) is unrelated and stays separate.
- No change to the PWA's holder-side `Present.razor`.

## Deployment note

**The existing blueprints must be updated with these feature changes before the strathcarron
portal is deployed.** Retiring `CredentialGateComponent` is safe today only because that portal is
not currently deployed.
