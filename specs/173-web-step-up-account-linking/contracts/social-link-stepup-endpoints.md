# Contract: F168 Anonymous Social-Link Step-Up Endpoints (UI-consumed)

This is the **consumer's view** of the Feature 168 server contract. Feature 173 implements no server
endpoints — it calls these three. The link-pending token is the **principal**; no bearer session is
sent. (See `research.md` R1: these endpoints are a prerequisite and are not yet present in this
worktree.)

All three are governed server-side by the `platform-auth` rate-limit policy and enforce token
expiry, single-use semantics, proof-tier flooring, account-match, and conflict detection.

---

## 1. Initiate — `POST /api/auth/social/link/challenge/initiate`

Anonymous. Begins the step-up for the account addressed by the link-pending token.

**Request**
```json
{ "linkPendingToken": "<opaque>", "preferredMethod": "Passkey" }
```
`preferredMethod` optional (`Passkey` | `Totp`); omit/null ⇒ server selects strongest available.

**Response 200**
```json
{ "method": "Passkey", "payload": { /* PublicKeyCredentialRequestOptions, base64url */ } }
```
`payload` is null for `Totp`.

**Failures the prompt must map**
| Status | Meaning | Prompt state |
|--------|---------|--------------|
| 401 | link-pending token invalid/expired | `Expired` (FR-015) |
| 400 | no v1-eligible method enrolled | `Recovery` (FR-018) |
| 429 | rate limited | `ErrorRetry` (throttled, non-leaky — FR-016) |

---

## 2. Verify — `POST /api/auth/social/link/challenge/verify`

Anonymous. Submits the proof; returns a single-use challenge token.

**Request**
```json
{ "linkPendingToken": "<opaque>", "method": "Totp", "proof": { "code": "123456" } }
```
For `Passkey`, `proof` is the WebAuthn assertion JSON returned by
`PasskeyInteropService.GetCredentialAsync`.

**Response 200**
```json
{ "token": "ch_<opaque>", "expiresIn": 300 }
```

**Failures the prompt must map**
| Status | Meaning | Prompt → `ChallengeVerifyError` |
|--------|---------|-------------------------------|
| 401 | proof rejected / token invalid-expired | `ProofRejected` or `Expired` |
| 403 `proof_tier_insufficient` | proof below required tier | `ProofTierInsufficient` → `Recovery` |
| 429 | rate limited | `Failed` (throttled message) |

---

## 3. Confirm — `POST /api/auth/social/link/confirm`

Anonymous. Redeems link-pending token + challenge proof, links the social identity, issues a session.

**Request**
```json
{ "linkPendingToken": "<opaque>" }
```
**Header**: `X-Auth-Challenge: ch_<opaque>` (required; absence ⇒ 401).

**Response 200** (same shape as a normal social sign-in)
```json
{ "accessToken": "<jwt>", "refreshToken": "<opaque>", "expiresIn": 3600 }
```

**Failures the prompt must map**
| Status | Meaning | Prompt → `ConfirmOutcome` |
|--------|---------|---------------------------|
| 401 | token/challenge invalid, expired, or already consumed (replay) | `Expired` / `ProofInvalid` |
| 403 | account mismatch / wrong scope / tier | `ProofInvalid` (non-leaky) |
| 409 | already linked elsewhere / email collision | `Conflict` (non-leaky, no session) |
| 429 | rate limited | `RateLimited` |

**Success post-condition**: feed `{accessToken, refreshToken}` into the existing session path
(see `fragment-and-session.md`).

---

## Client service surface (`IAnonymousSocialLinkClientService`)

```csharp
Task<AnonymousLinkInitiateResult> InitiateAsync(string linkPendingToken, ChallengeMethod? preferred = null, CancellationToken ct = default);
Task<AnonymousLinkVerifyResult>   VerifyAsync(string linkPendingToken, ChallengeMethod method, JsonElement proof, CancellationToken ct = default);
Task<AnonymousLinkConfirmResult>  ConfirmAsync(string linkPendingToken, string challengeToken, CancellationToken ct = default);
```
Registered in `Sorcha.UI.Components.User` DI alongside the other client services; uses the configured
`HttpClient` base address (`AddCoreServices`). No `Authorization` header is attached on these calls.
