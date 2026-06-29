# Quickstart: Validate the Verification Fix

Validation guide for the three paths in [spec.md](./spec.md) (SC-001…SC-005). Implementation
details live in `tasks.md`; this file is the run/validate guide. See
[contracts/verification-transport.md](./contracts/verification-transport.md) and
[data-model.md](./data-model.md) for the state mapping referenced below.

## Prerequisites

- .NET 10 SDK, Docker Desktop.
- A running stack with HAIP Service + Blueprint Service + API Gateway + the web client:
  ```bash
  docker-compose up -d
  # web client: http://localhost/app   gateway: http://localhost:80
  ```
- An authenticated web user session (sign in at `/app`).
- A HAIP-capable external wallet (or the test wallet harness) to scan the QR and present a
  credential.

## Build & unit/component tests

```bash
dotnet build
# Transport + state-discrimination tests (web verification surface)
dotnet test --filter "FullyQualifiedName~Haip|FullyQualifiedName~Presentation|FullyQualifiedName~Verif"
```

Expected: the new tests for the **Error/Retry** state and the **legitimate not-configured** state
pass alongside the existing happy-path tests.

## Scenario A — Configured host, happy path (SC-001, SC-002; FR-001/002/004)

1. Open the verification surface (the QR / presentation-request card) on the web client.
2. Start a verification request.
3. **Expect**: a QR renders and the card sits in **Pending** ("Waiting for wallet to scan…") —
   **no** "not configured" message, **no** silent stall.
4. Scan with the wallet and present the credential.
5. **Expect**: card advances **Submitted → Verified** and shows the disclosed claims.

Network check (devtools): the result-poll calls a **`/api/v1/presentations/*`** (BFF) URL carrying
the user's `Authorization: Bearer …`, **not** `/api/v1/verifier/requests/*`. No 401/403 on the poll.

## Scenario B — Backend failing, error + retry (SC-003, SC-004; FR-006/007/009)

1. Make the backend fail for a configured host — stop HAIP Service, or force the BFF result call to
   return 401/500.
2. Start a verification request.
3. **Expect**: the card shows an **error state with a Retry control** — distinct from
   "not configured," and **not** a blank/empty session.
4. Restore the backend; click **Retry**.
5. **Expect**: polling resumes and the session reaches **Verified** without reloading the page.

Retry-storm check: while the backend is down, confirm polling is **bounded** (stops at
`MaxPollTicks`) and the UI is not locked.

## Scenario C — Genuinely not-configured host, preserved (SC-005; FR-008)

1. Use a host/surface where verification is **not** wired up (verification components not
   registered).
2. Open the verification surface.
3. **Expect**: the legitimate **"not configured"** state still shows — confirming the fix did not
   turn the failure path into a blanket "configured" claim.

## PWA spot-check (FR-003; Edge: token refresh, clock skew)

The PWA doorstep `Verify.razor` uses the local `IVerifierEngine` and is **not** on the broken
polling path. If task analysis adds a PWA surface that calls the BFF:
1. In the PWA, run a verification while signed in as a holder.
2. **Expect**: the request carries the holder bearer token; an expired-but-refreshable token
   refreshes-and-retries (no hard error); server-clock handling stays in effect (no skew rejection).

## Pass criteria (maps to Success Criteria)

| Scenario | Success Criteria |
|---|---|
| A | SC-001, SC-002 |
| B | SC-003, SC-004 |
| C | SC-005 |
| Tiers/credentials | SEC-013 held (verifier stays `RequireService`); no service creds in a public client |
