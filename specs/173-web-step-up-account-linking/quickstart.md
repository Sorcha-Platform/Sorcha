# Quickstart / Validation Guide: Web Step-Up Social Account Linking (B-UI)

Runnable validation for the `/app` anonymous link-existing-account prompt. Implementation detail
lives in `plan.md`, `data-model.md`, and `contracts/`; this guide is how you prove the feature works.

## Prerequisites

- **Feature 168 server endpoints available** (challenge initiate/verify/confirm + the
  `LinkRequired` redirect). ⚠ Not present in this worktree — see `research.md` R1. Live/E2E
  validation is blocked until F168 is merged or stubbed.
- .NET 10 SDK; Docker Desktop (for the UI Playwright infra — see the `sorcha-ui` skill).
- A test account with (a) a registered passkey and (b) a separate account enrolled in TOTP, each
  matchable by a social provider's verified email that is **not yet linked**.

## Build & unit tests (no F168 required)

```bash
dotnet build src/Apps/Sorcha.UI/Sorcha.UI.Components.User
dotnet test --filter "FullyQualifiedName~AnonymousSocialLink"
```
The client-service unit tests mock the HTTP boundary and the WebAuthn interop; they assert
status-code → outcome mapping (401/403/409/429), `X-Auth-Challenge` header on confirm, and
fail-closed transitions.

## Run the web app

```bash
dotnet run --project src/Apps/Sorcha.AppHost   # then open http://localhost/app
```

## Validation scenarios

### US1 — Passkey link (P1, happy path) — SC-001, SC-005, SC-006
1. Trigger a social sign-in that returns `LinkRequired` for the passkey account → lands at
   `/app/#outcome=LinkRequired&linkPendingToken=…`.
2. **Expect**: prompt appears (not the signed-out home); address bar shows `/app` with **no
   fragment** (verify via reload + back button — token must not reappear, SC-005).
3. Continue → complete the passkey check → **expect** signed-in landing within ≤3 interactions /
   <60s, identical to a normal social sign-in.
4. Sign out, sign in again with the same provider → **expect** direct sign-in, no prompt (SC-006).

### US2 — TOTP link (P1) — SC-002
1. Trigger `LinkRequired` for the TOTP-only account.
2. **Expect**: authenticator-code challenge offered.
3. Enter a valid 6-digit code → **expect** link + signed-in.
4. Enter an invalid/expired code → **expect** "code not accepted", **no link, no session**, retry
   allowed (subject to throttling).

### US3 — Fail-closed & abandonment (P2) — SC-003, SC-004
| Case | Action | Expect |
|------|--------|--------|
| Expired token | present a stale `linkPendingToken` | "request expired — sign in again"; no link/session |
| Tampered token | mutate the token | same non-leaky expired/invalid message; no link/session |
| Cancel | click Cancel | signed-out home; no link/session |
| No v1 method | account viable only via password/ReOAuth | recovery guidance ("sign in with your existing method"); never a dead end |
| Conflict | provider already linked elsewhere by confirm time | non-leaky failure; no session |
| Replay | reuse a consumed token/challenge | no second link, no second session |
| Reload after capture | refresh once the token is cleared | signed-out home; no crash/partial link |

### Isolation check — SC-007
```bash
git diff --name-only master... | grep -E 'Security/(AuthChallengeDialog|SecurityHome|PasswordSection|PasskeysSection|SocialLinksSection|TwoFactorSection|AssuranceBadge)\.razor'
# Expect: no output (zero edits to Feature 150/116 component files)
```

## E2E (Playwright, requires F168)

Follow the `sorcha-ui` skill's Docker test workflow. Cover US1 (passkey via virtual authenticator),
US2 (TOTP valid + invalid), and the US3 fail-closed matrix. Assert: prompt shown for every
`LinkRequired` landing (zero dead-ends, SC-003); no session/link on any failure path (SC-004); no
toast surfaces used (inline feedback only, FR-019).

## Done when
- Unit tests green; Playwright US1/US2/US3 green against F168; SC-007 diff check clean; manual
  reload/back-nav confirms the token never persists (SC-005).
