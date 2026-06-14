# PWA Dual-Tier / Org-Role Work — Design (Sub-project D)

**Date:** 2026-06-14
**Status:** Design — ready to speckit specify/plan/tasks (implementation deferred to a focused session)
**Decision owner:** Stuart Fraser
**Parent programme:** PWA workflow participation (A/B/C/D). Depends on **A** (inbox) and benefits from **C** (offline).

---

## 1. Context & scope decision

The goal of D is to let the **same human** do their **organisational-role** workflow work on the
phone (e.g. a verification analyst performing the issuing "Action 2" of AssuredIdentity), not only
their personal/citizen (consumer-tier) work — deliberately bringing platform-tier work to the PWA,
which sits at `/wallet/` (consumer tier) under the F136 audience model.

**Scope decision (2026-06-13):** *Light up org-role work on the existing context switcher* — NOT a
from-scratch dual-tier auth build. Grounding (below) showed the hard plumbing already exists.

### Grounding (verified read-only — the findings that shape D)

- **The auth plumbing largely exists.** `/api/auth/switch-org` already re-mints a **platform-tier,
  org-scoped** token; the PWA's `IUserContext` / `ManagedUserContext` already calls it; `MainLayout`
  already hosts `ContextChipSwitcher` + `IUserOrgMembershipsClient` (Feature 125). Switching to an
  org context in the PWA *already yields a platform/org token*.
- **Tier = audience** (`SorchaAudiences`); entitlement gate (`TierResolver`): Platform only for
  members holding `SystemAdmin|Administrator|Designer|Auditor`; an **explicit** `tier=platform`
  request by a non-entitled user is **refused 403**; a destination-derived preference **downgrades
  to entitlement**. These gates are the security boundary and **must not be weakened**.
- **Org-role actions already surface in the inbox.** In Sorcha's model the org-role action's
  participant is bound to the **member's own personal wallet** (pre-baked at publish — e.g. the
  analyst's wallet seeded into `instance.ParticipantWallets` by `InstanceProjectionResolver`), so
  `GET /api/actions/pending` returns it via the member's `platform_user_id` **regardless of tier**
  (`ActionEndpoints.ResolveUserWalletAddressesAsync`).
- **Executing an org-role/issuing action needs the platform/org token.** `ActionExecutionService`
  uses `org_id` (+ roles) for credential-issuance config (Feature 120) and participant-ownership
  verification (`ActionExecutionService.cs:~2409`). So the action must be performed **while in the
  org context** (platform token carries `org_id`), which the existing switch-org provides.
- **PWA login hardcodes `tier="consumer"`** in three places (`IAuthService.SignInAsync` /
  `VerifyTwoFactorAsync` / `SignInWithPasskeyAsync`); `AccessTokenRecord` has **no tier field** — the
  session doesn't know which "hat" it's wearing.
- **No backend change required.** `/api/actions/pending`, `/execute`, and `/api/auth/switch-org` are
  all tier-agnostic / already exist. The F136 gates stay untouched.

---

## 2. What D actually delivers

A user who is both a citizen and an org member can, on the phone:
1. See which **context/hat** they're in (Personal = consumer; **<Org>** = platform/org).
2. Switch to an **org context** via the existing chip → the PWA holds the platform/org token.
3. See their **org-role pending actions** in the same "Things to do" inbox, clearly framed as
   **"acting as <Org>"**, and **perform** them (execute succeeds because `org_id` is present).
4. Switch back to Personal for their own citizen work.

**Out of scope:** holding consumer + platform sessions *simultaneously* (D uses the existing
single-active-context model — switch re-mints, matching the web app); a from-scratch role picker at
first sign-in (sign-in stays consumer; role is reached via the switcher); org-only members with no
personal wallet (documented edge — see risks).

---

## 3. Design

- **Tier-aware session.** Add a `tier`/context marker to the stored session (derive from the JWT
  `aud` via `SorchaAudiences`, or store alongside `AccessTokenRecord`). The PWA shell reflects it:
  Personal vs. "acting as <Org>". This is the core new state — everything else is framing over the
  existing switch.
- **Context switch = existing mechanism.** Reuse `IUserContext.SetActiveContextAsync(orgId)` →
  `/api/auth/switch-org` (already implemented) to obtain the platform/org token; Personal context
  returns to the consumer token (re-mint or stored). No new auth endpoint.
- **Inbox framing (reuse A).** `Actions.razor` already lists `/api/actions/pending` for the active
  token. In an org context it will return the member's org-role actions (bound to their personal
  wallet); add an "acting as <Org>" banner/affordance so the user knows the hat. The count badge
  (US2 of A) reflects the active context.
- **Execute in-context.** Because the active token in an org context carries `org_id`, the existing
  `ApplicationInstance` → `/execute` path performs the org-role action correctly (issuance config +
  participant-ownership checks pass). No change to the submit path beyond using the active token.
- **Entitlement respect.** Only show/enable the org switcher for members who actually hold
  org memberships/roles (existing `IUserOrgMembershipsClient`). Never force `tier=platform` for a
  non-entitled user — the server 403 stands; the UI simply doesn't offer it.

### Components
- Tier/context marker on the session + shell indicator ("acting as <Org>").
- Reuse: `IUserContext` / `ContextChipSwitcher` / `IUserOrgMembershipsClient` (already in PWA), A's
  inbox + `ApplicationInstance`.
- Small: an "acting as <Org>" banner in the inbox; ensure the inbox + badge refresh on context switch.

---

## 4. Risks / must-validate-live

- **Org-context-at-execute (primary risk).** The whole feature hinges on an org-role/issuing action
  executing correctly under the platform/org token (org_id + roles present). This must be
  **live-validated** (run the AssuredIdentity analyst Action 2 from the PWA in the org context)
  before D is trusted — it depends on subtle `ActionExecutionService` org-context behaviour.
- **Org-only member, no personal wallet.** `/api/actions/pending` resolves only the member's
  personal wallet(s); an org member with no personal wallet would see no actions. Real members
  (analyst) have one. If org-only membership becomes a real case, a follow-up would resolve the org
  wallet or add an org-context list parameter (a backend change — explicitly NOT in D).
- **Token lifecycle on switch.** Returning to Personal must restore a consumer token (re-mint or
  cached); ensure the bearer handler always sends the active-context token and the inbox refreshes.
- **Security boundary.** Do NOT add any path that lets a non-entitled user obtain a platform token;
  rely on the server entitlement gate (403). The PWA only *offers* org context to entitled members.

---

## 5. Likely user stories (for speckit)

- **US1 (P1):** Tier-aware session + "which hat am I in" shell indicator (Personal vs. acting-as-Org).
- **US2 (P1):** Switch to an org context and see + perform my org-role pending actions in the inbox
  ("acting as <Org>" framing); execute succeeds in-context. *(The proof slice — live-validate.)*
- **US3 (P2):** Switch back to Personal cleanly; inbox/badge reflect the active context throughout.
- **US4 (P3):** Entitlement-aware affordance (switcher only for members with org roles; never
  force-elevate; honest messaging if a context can't be entered).

**No backend change anticipated.** If org-only-no-personal-wallet must be supported, that is a
separate backend follow-up, not part of D.

---

## 6. Definition of done (D)

A citizen who is also an org member can, on the phone, switch into their organisation, see and
perform the workflow actions that are theirs to do *as that org member* (clearly framed as such),
and switch back to personal — reusing the existing switch-org + inbox + execute infrastructure, with
the F136 entitlement/audience gates intact, validated live against the AssuredIdentity analyst flow.
