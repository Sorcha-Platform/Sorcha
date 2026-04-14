# API Contracts — Feature 104 Credential Claim Action

Wave 14 introduces **one new endpoint** and modifies the behaviour of **one existing endpoint**. This document summarises both. Full OpenAPI definitions are in the adjacent `*.yaml` files.

---

## New: `POST /api/blueprint/instances/{instanceId}/actions/{actionId}/claim-expired`

**Purpose:** Client-side trigger to transition an expired credential claim action to `Failed` state on the register. Fired by `CredentialClaimCard` when it detects `expires_at < now` on a pending claim action.

**Authorization:** Requires a JWT carrying the authenticated citizen's identity. The action's late-bound sender wallet MUST match the JWT's wallet claim.

**Rate limit policy:** `RateLimitPolicies.Api` (default).

**Request:**

```http
POST /api/blueprint/instances/5e8f2c1b-aa31-4b3c-9f1e-7b2d4a5c6e8f/actions/2/claim-expired
Authorization: Bearer <jwt>
Content-Type: application/json

{
  "senderWallet": "ws1q8tuvvdykly8n0fy5jkuu8cjw0fu0p6jl5rp9gxy...",
  "registerAddress": "ws1q...",
  "reason": "expired"
}
```

**Response (success — 200):**

```json
{
  "instanceId": "5e8f2c1b-aa31-4b3c-9f1e-7b2d4a5c6e8f",
  "actionId": "2",
  "newState": "Failed",
  "reason": "expired",
  "sealedAt": "2026-04-14T11:32:04Z"
}
```

**Error responses:**

- `400 Bad Request` — The action is not a credential claim action (no `x-credential-offer` field in its schema), or the offer is not actually expired (`expires_at >= now` at evaluation time).
- `401 Unauthorized` — JWT missing or invalid.
- `403 Forbidden` — JWT wallet claim does not match the action's late-bound sender wallet.
- `404 Not Found` — Instance or action not found.
- `409 Conflict` — The action is already in a terminal state (Completed, Failed, Rejected).

**Server-side behaviour:**
1. Resolve the instance and action.
2. Verify the action has an `x-credential-offer` field in its schema.
3. Extract `expires_at` from `Instance.PendingActionPayloads[actionId].credentialOffer.expires_at`.
4. Confirm `expires_at < DateTimeOffset.UtcNow`.
5. Build and submit a failure transaction via the normal action-execution transaction chain so the register audit trail is written.
6. Mark `Instance.PendingActionPayloads[actionId]` removed atomically with the state change.

**Why a dedicated endpoint rather than reusing `SubmitActionExecuteAsync`:**
- Execute is for successful action submissions; overloading it with a "this didn't happen" code path muddles semantics.
- A dedicated endpoint lets authz require that the action has an `x-credential-offer` schema field, so the endpoint cannot be abused to arbitrarily fail unrelated pending actions.

---

## Modified: `POST /api/blueprint/instances/{instanceId}/actions/{actionId}/execute`

**Existing purpose:** Submit a pending action for execution, validation, routing, and sealing to the register.

**Wave 14 changes:** Additive only. The payload shape in requests and responses is unchanged.

**New server-side behaviour (transparent to clients):**
1. After loading the instance and the action, load `instance.PendingActionPayloads[actionId]`. If present, merge it with the submitted `payloadData` before validation (submission wins on key collision, per FR-007).
2. The merged payload is what `ValidateActionDataAsync` runs against and what is sealed to the register.
3. On successful seal, atomically remove `instance.PendingActionPayloads[actionId]`.
4. When the route that fires during this execution has a non-null `OutputMapping`, evaluate it against the current action's source document (submitted payload + calculations + HAIP mint output when present) and write the resulting per-next-action payloads into `instance.PendingActionPayloads`.

**Client-visible effects:**
- If a client submits an action that was seeded by a previous action's `OutputMapping`, the response includes the sealed transaction's full payload (the merged object), not just the submitted fields. Existing clients that only inspect the submitted fields continue to work.
- No new request fields, no new response fields, no new status codes.

---

## Modified: `GET /api/blueprint/instances/{instanceId}/pending-actions`

**Existing purpose:** List pending actions for a citizen's wallet, rendered by `MyActions.razor`.

**Wave 14 changes:** The response for each pending action MAY now include a `prepopulatedPayload` field carrying the `instance.PendingActionPayloads[actionId]` object when present.

**New response shape (showing only the new field):**

```json
{
  "instanceId": "...",
  "actionId": 2,
  "blueprintId": "verified-citizen-v2",
  "title": "Claim your Verified Citizen credential",
  "senderWallet": "ws1q...",
  "createdAt": "...",
  "prepopulatedPayload": {
    "credentialOffer": {
      "credential_offer_uri": "openid-credential-offer://?credential_offer=...",
      "display": {
        "title": "Verified Citizen Credential",
        "subtitle": "Issued by Government of Exampleland",
        "description": "Confirms your verified identity for future online services.",
        "issuer": {
          "name": "Government of Exampleland",
          "logo": { "uri": "https://...", "alt": "Government logo" }
        }
      },
      "expires_at": "2026-04-15T11:32:04Z"
    }
  }
}
```

**Client-visible effects:**
- `MyActions.razor` passes `prepopulatedPayload` to `ActionWorkspace` as initial form state.
- `SorchaFormRenderer` sees the `x-credential-offer` field and renders `CredentialClaimCard` instead of a generic object editor.
- Existing pending actions (from non-claim blueprints) have `prepopulatedPayload: null` (or the field omitted) — no behaviour change.

---

## No changes

- Blueprint publish endpoint — signature unchanged. New validation checks (`VAL_BP_011`, `VAL_BP_012`, `WARN_BP_006`) are evaluated inside the existing publish flow and returned via the existing `warnings` / errors response fields.
- All other blueprint and wallet endpoints — unchanged.
- HAIP issuer endpoints — unchanged (the wave 13 flow is reused verbatim for local claim).

---

## OpenAPI file (machine-readable)

See `claim-expired.yaml` for the new endpoint's full OpenAPI specification.
