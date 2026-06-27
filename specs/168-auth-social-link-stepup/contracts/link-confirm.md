# Contract: POST /api/auth/social/link/confirm

Redeems a link-pending token **and** a `LinkSocial` step-up challenge proof, links the social identity
to the target account, and issues the same session a normal social sign-in would. **Unauthenticated**
(the two tokens are the credentials). Rate limit: `RateLimitPolicies.PlatformAuth`.

## Request
- Body: `{ "linkPendingToken": "<opaque>" }`
- Header: `X-Auth-Challenge: <challenge token from the verify step>`

## Behaviour (ordered, fail-closed)
1. Verify link-pending token (signature + expiry) → `targetAccountId`, `provider`, `subject`,
   `socialEmail`, `displayName`. Invalid/expired → **401**, no change. (FR-015)
2. Require the `X-Auth-Challenge` header. Absent → **401**. (FR-006)
3. Consume the challenge token for `ScopedOperation.LinkSocial`. Wrong operation / invalid / already
   consumed → **401/403**. (FR-008)
4. Assert challenge token's bound `PlatformUserId == targetAccountId`. Mismatch → **403**, no link.
   (FR-007)
5. `ISocialLinkService.LinkAsync(targetAccountId, provider, subject, socialEmail, displayName)`:
   - `Linked` / `AlreadyLinkedToCaller` → continue.
   - `AlreadyLinkedToDifferentUser` / `EmailCollision` → **409 Conflict**, no overwrite. (FR-012, SC-006)
6. Issue session via `ITokenService` with the same tier/audience derivation as the social callback
   (web ⇒ Platform, citizen wallet ⇒ Consumer). → **200**. (FR-011, SC-003)

## Responses
| Status | When | Notes |
|--------|------|-------|
| 200 | Linked + session issued | `{ "accessToken": "...", "refreshToken": "...", "expiresIn": N }` (same shape as social sign-in) |
| 401 | Token invalid/expired, or no/invalid proof | Non-leaky |
| 403 | Proof account ≠ target, wrong operation, or proof tier insufficient | Non-leaky |
| 409 | Link-time collision (already-linked-elsewhere / email-belongs-to-another) | No state change |
| 429 | Rate limited | — |

## Invariants
- FR-005: only links + issues a session on a valid token **and** valid proof.
- FR-018: distinct, non-leaky 401/403/409; does not reveal target-account existence beyond what the
  social flow already exposes.
- US4: never called / expired token → no link row, no session, account unchanged.
- Telemetry (FR-017): record `link-confirm` outcome (`success` / `conflict` / `rejected`) on the
  existing social-login counter.
