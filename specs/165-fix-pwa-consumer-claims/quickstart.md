# Quickstart / Validation: Fix PWA Consumer-Token Claims

**Feature**: 165 | **Date**: 2026-06-26

Validation has two layers: **(A)** deterministic backend regression tests (CI), and **(B)** the mandatory interactive `n1` proof (FR-008). Both must pass to close relaunch backlog items 3/4/5 (SC-006).

---

## Prerequisites

- .NET 10 SDK, Docker Desktop.
- Access to the live `n1` network (`n1.sorcha.dev`) and a real citizen account that can sign in to the Citizen Wallet PWA. See the `network-bootstrap` skill if `n1` needs (re)deploying.
- The build under test deployed to `n1` (record the image tag `:2.<run>.<attempt>` — CLAUDE.md §14).

---

## A. Backend regression tests (CI, deterministic)

Run from repo root:

```bash
# Minting-coverage contract: every consumer path + refresh carries platform_user_id,
# and never carries role/wallet_address (contracts/consumer-token-claims.md)
dotnet test tests/Sorcha.Tenant.Service.Tests \
  --filter "FullyQualifiedName~ConsumerToken|FullyQualifiedName~PlatformUserIdClaim"

# Identity-resolution contract: legacy token recovers the CORRECT platform id,
# not the org-scoped sub (contracts/citizen-identity-resolution.md)
dotnet test tests/Sorcha.Wallet.Service.Tests \
  --filter "FullyQualifiedName~ResolveCitizenContext|FullyQualifiedName~CitizenWallet"
```

**Expected**: green. Asserts INV-1…INV-4 (data-model.md) and resolution rules M-1…M-4.

> Filter names above are indicative — align them to the test class/method names created in the `/speckit-tasks` + implementation phase.

---

## B. Interactive `n1` verification (mandatory — the real proof)

> A synthetic minted-token test is necessary but **not** sufficient; FR-008 requires a real interactive citizen sign-in.

### B0. Confirm the deployed build carries the fix
- Record the `n1` image/version. Confirm it is at or after the build containing this branch (the minting line `TokenService.cs:110` + the hardened `ResolveCitizenContext`).

### B1. Story 1 — Security (P1, SC-001)
1. Sign in as a citizen on the PWA against `n1`.
2. Open **Security**.
3. **Expect**: the page loads the citizen's own security state — no unauthorized error, no blank/error fallback. First attempt, every time.
4. Record which backend call `SecurityHome` issues (closes the research open item).

### B2. Story 2 — Devices (P1, SC-002)
1. As the same citizen, open **Devices**.
2. **Expect**: the citizen's own device list returns (an empty list is a valid success — the criterion is a successful, citizen-scoped response, not a non-empty list).
3. If ≥1 device exists, relabel or revoke one → **Expect**: action succeeds and persists on reload.

### B3. Story 3 — Add a phone (P2, SC-003)
1. Start **Add a phone**.
2. **Expect**: the flow loads and produces a pairing artefact bound to the citizen's wallet (not a load failure).
3. Complete pairing on the second device → return to **Devices** → **Expect**: the new device appears.

### B4. Renewal regression (SC-004)
1. Keep the session alive past an access-token renewal (or force a refresh).
2. Re-open Security / Devices / Add-phone.
3. **Expect**: all still load — the renewed consumer token carries the same `platform_user_id`.

### B5. Tier boundary preserved (SC-005)
1. Take the citizen's consumer token and call one representative platform/admin-only operation.
2. **Expect**: rejected (401/403) — the consumer token remains inert at platform surfaces.

### B6. Legacy-token degrade (FR-007) — if a pre-fix token is available
1. With a token minted before the fix (no `platform_user_id`), open Devices.
2. **Expect**: it still loads via backend recovery (correct identity), not a blank/foreign result.

---

## Done / evidence (SC-006)

Backlog items 3, 4, 5 are closed when, for each of the three surfaces, there is a recorded successful citizen run on `n1` (B1–B3), the renewal and tier-boundary checks pass (B4–B5), and the CI regression suite (A) is green. Capture the `n1` image tag and a short run note per surface as evidence.

## References
- Claim contract: [contracts/consumer-token-claims.md](./contracts/consumer-token-claims.md)
- Resolution contract: [contracts/citizen-identity-resolution.md](./contracts/citizen-identity-resolution.md)
- Claim set & precedence: [data-model.md](./data-model.md)
- Root-cause analysis: [research.md](./research.md)
