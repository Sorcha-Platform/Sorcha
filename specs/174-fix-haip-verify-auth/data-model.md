# Phase 1 Data Model: Verification State

No database or persistence schema changes. This document models the **in-UI verification state**
the fix must distinguish, grounded in the existing types. The whole point of the fix is that the
state machine currently collapses three distinct conditions into one ("got `null` → keep waiting"),
which renders as the false not-configured / silent-stall symptom.

## Existing types (source of truth)

- `HaipVerificationResult` (`…/Services/User/Credentials/IHaipOfferService.cs`)
  ```csharp
  record HaipVerificationResult(
      Guid RequestId,
      string State,                                  // one of HaipVerificationStates.*
      bool? IsValid,
      Dictionary<string, JsonElement>? VerifiedClaims,
      IReadOnlyList<string>? Errors);
  ```
- `HaipVerificationStates` (`…/Services/User/Credentials/HaipStates.cs`)
  `Pending`, `Submitted`, `Verified`, `Denied`, `Expired`, `Cancelled`;
  `IsTerminal = Verified | Denied | Expired | Cancelled`.
- `HaipPollingDefaults`: `PollInterval`, `MaxPollTicks = 150`, `ErrorCloseDelayMs = 3000`.
- Server result DTO: `VerificationResult` (HAIP Service) and `PresentationRequestResultViewModel`
  (web `PresentationAdminService`), plus `PresentationStatusResponse` (Blueprint `/status`,
  lifecycle-only, no claims).

## State model the UI must render

| UI state | Trigger | Maps to | Render |
|---|---|---|---|
| **Pending** | request created, no result yet | `State=Pending`, or BFF `awaiting-presentation` | QR + "Waiting for wallet to scan…" (existing) |
| **Submitted** | wallet posted vp_token, verifying | `State=Submitted` | spinner "Verifying…" (existing) |
| **Verified** (terminal) | success + claims | `State=Verified`, `IsValid=true`, `VerifiedClaims` | success + disclosed claims (existing) |
| **Denied / Expired / Cancelled** (terminal) | negative outcomes | corresponding `State` | existing alerts |
| **Error / Retryable** (NEW) | transport/auth/server failure (401/403/5xx/network) | *no longer `null`* — a discriminated transport-failure outcome | error alert **+ Retry control**; distinct from not-configured |
| **NotConfigured** (preserved) | host did not wire verification at all (registration absent, not a 401) | N/A — absence of the surface | legitimate "not configured" message (no regression) |

### The core invariant this fix introduces

> A failed transport call MUST NOT be indistinguishable from "no result yet" or from
> "not configured." Today `HaipOfferService.GetVerificationResultAsync` returns `null` for **all**
> of: not-found, 401/403, 5xx, and network error — and the QR card's loop treats `null` as
> "keep polling." The fix gives the transport a result type that separates *transient/recoverable
> failure* (→ Error/Retry) from *no-result-yet* (→ keep polling) from *terminal outcome*.

## State transitions

```
Pending ──(poll: result.State advances)──▶ Submitted ──▶ Verified | Denied
   │                                                         (terminal)
   ├──(ExpiresAt passed)──────────────────▶ Expired (terminal)
   │
   └──(transport/auth/server failure)─────▶ Error/Retryable
                                              │
                                              └──(user Retry, backend recovered)──▶ Pending/Submitted/Verified
```

`NotConfigured` is **not** a transition target from the polling loop — it is determined at
surface-mount time by whether verification is wired up for the host. Keeping it off the failure
path is what guarantees SC-005 (no regression of the legitimate case).

## Validation rules (from FRs)

- FR-006/FR-007: a transport failure ⇒ Error/Retry, never blank/empty, never NotConfigured.
- FR-008/SC-005: genuine NotConfigured remains reachable and unchanged.
- FR-009/SC-004: Retry re-issues the request and, on success, proceeds to the live session.
- Edge "retry storms": retries bounded by `MaxPollTicks` + user-initiated Retry; no unbounded
  auto-retry, no UI lock.
