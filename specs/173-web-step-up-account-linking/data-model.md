# Phase 1 Data Model: Web Step-Up Social Account Linking (B-UI)

Client-side model only — this feature persists nothing server-side and adds no database entities.
Wire DTOs mirror the Feature 168 contract (see [`contracts/`](./contracts/)). Where an enum already
exists and is wire-compatible, **reuse it** rather than redefining.

---

## Reused types (no new definition)

| Type | Source | Use here |
|------|--------|----------|
| `ChallengeMethod` (`Totp`, `Password`, `Passkey`, `ReOAuth`) | `Models/User/Authentication/AuthChallengeModels.cs` | The proof method the server indicates / the prompt submits. v1 renders only `Totp` + `Passkey`. |
| `ChallengeVerifyError` (`None`, `ProofRejected`, `Expired`, `Failed`, `ProofTierInsufficient`) | same file | Mapped from verify status codes to drive prompt messaging. |

---

## New types (`Models/User/Authentication/AnonymousSocialLinkModels.cs`)

### `LinkPendingOutcome` (captured from the fragment)
Opaque carrier of the staged link-required state. The client treats `LinkPendingToken` as opaque.

| Field | Type | Notes |
|-------|------|-------|
| `LinkPendingToken` | `string` | Opaque, short-lived (~5 min) server token. Principal for all three calls. Never logged, never shown. |

> Captured from `#outcome=LinkRequired&linkPendingToken=…` by the extended fragment-handoff JS;
> surfaced to C# via the JS accessor. No display name / email is decoded client-side (token is
> opaque) — any human-readable context shown in the prompt comes from the server `initiate` response
> or generic copy.

### `AnonymousLinkInitiateRequest`
| Field | Type | Notes |
|-------|------|-------|
| `LinkPendingToken` | `string` | required |
| `PreferredMethod` | `ChallengeMethod?` | optional; null lets the server pick the strongest available v1 method |

### `AnonymousLinkInitiateResult`
| Field | Type | Notes |
|-------|------|-------|
| `Method` | `ChallengeMethod` | the method to render |
| `Payload` | `JsonElement?` | WebAuthn request options for `Passkey`; null for `Totp` |
| `Outcome` | `InitiateOutcome` (enum) | `Ok`, `Expired`, `Unsupportedv1Method`, `RateLimited`, `Failed` — derived from status code |

### `AnonymousLinkVerifyRequest`
| Field | Type | Notes |
|-------|------|-------|
| `LinkPendingToken` | `string` | required |
| `Method` | `ChallengeMethod` | must match initiate |
| `Proof` | `JsonElement` | `{ "code": "######" }` for TOTP; WebAuthn assertion for Passkey |

### `AnonymousLinkVerifyResult`
| Field | Type | Notes |
|-------|------|-------|
| `Succeeded` | `bool` | |
| `ChallengeToken` | `string?` | `ch_…`; presented at confirm via `X-Auth-Challenge` |
| `Error` | `ChallengeVerifyError` | reused enum |

### `AnonymousLinkConfirmResult`
| Field | Type | Notes |
|-------|------|-------|
| `Outcome` | `ConfirmOutcome` (enum) | `Linked`, `Expired`, `ProofInvalid`, `Conflict`, `RateLimited`, `Failed` |
| `AccessToken` | `string?` | present only on `Linked` |
| `RefreshToken` | `string?` | present only on `Linked` |
| `ExpiresIn` | `int?` | seconds |

---

## Prompt UI state machine (`LinkExistingAccountPrompt`)

```text
Detecting ──(staged token present)──► Explaining
Detecting ──(none / malformed)──────► (gate inert → signed-out home)        [FR-003]

Explaining ──(continue)────────────► Initiating
Explaining ──(cancel)──────────────► Cancelled → signed-out home           [FR-017]

Initiating ─(Ok: Passkey)──────────► PasskeyCeremony
Initiating ─(Ok: Totp)─────────────► AwaitingCode
Initiating ─(Unsupportedv1Method)──► Recovery                               [FR-018, edge: no v1 method]
Initiating ─(Expired)──────────────► Expired                               [FR-015]
Initiating ─(RateLimited/Failed)───► ErrorRetry

PasskeyCeremony ─(assertion)───────► Verifying
PasskeyCeremony ─(cancelled/no WebAuthn)► ErrorRetry / switch-to-Totp      [FR-007]
AwaitingCode ───(submit)───────────► Verifying

Verifying ─(Succeeded)─────────────► Confirming
Verifying ─(ProofRejected)─────────► ErrorRetry (retry allowed)            [US2 #2, FR-016]
Verifying ─(ProofTierInsufficient)─► Recovery                              [FR-016/FR-018]
Verifying ─(Expired)───────────────► Expired                              [FR-015]

Confirming ─(Linked)───────────────► establish session → signed-in        [FR-010/FR-011, SC-006]
Confirming ─(Conflict)─────────────► ConflictFailure (non-leaky, no session) [edge: provider linked elsewhere, FR-016]
Confirming ─(Expired/ProofInvalid)─► Expired / ErrorRetry                  [FR-015, replay edge]
Confirming ─(RateLimited/Failed)───► ErrorRetry
```

**Terminal-fail invariants (SC-004, fail-closed)**: `Expired`, `ConflictFailure`, `Recovery`,
`Cancelled`, and any `Failed` state establish **no session and complete no link**, and present a
**non-leaky** message (no disclosure of target-account existence beyond what the social flow already
revealed — FR-016).

**Validation rules**
- TOTP code: 6 numeric digits, trimmed; client-side shape check only — acceptance is server-side.
- `LinkPendingToken`/`ChallengeToken`: opaque; never parsed, never persisted beyond the in-memory
  flow; the link-pending token is stripped from the URL at capture (FR-002, SC-005).
- Method gating: only `Passkey` and `Totp` are renderable; any other indicated method ⇒ `Recovery`.

---

## State transitions vs. requirements traceability

| State / transition | Requirements |
|--------------------|--------------|
| Detecting → Explaining; URL stripped | FR-001, FR-002, SC-005 |
| Detecting → home (no/malformed) | FR-003 |
| Initiating with link-pending token as principal | FR-005 |
| Passkey path | FR-006, FR-007, FR-008, FR-014 |
| TOTP path | FR-006, FR-009 |
| Verify → Confirm → session | FR-010, FR-011, SC-001, SC-002, SC-006 |
| Expired/invalid token | FR-015, SC-004 |
| Rejected/insufficient/mismatch/conflict | FR-016, SC-003, SC-004 |
| Cancel | FR-017 |
| No v1 method / recovery | FR-018, SC-003 |
| Inline feedback only | FR-019 |
