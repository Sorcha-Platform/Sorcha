# Contract: Fragment Capture & Session Establishment (web host)

How the `LinkRequired` outcome enters the web app and how a completed link establishes a signed-in
session. These are **client-host** contracts (JS interop + Blazor seams), not network endpoints.

---

## A. Fragment capture (extends existing `fragment-handoff.js`)

**Inbound**: F168 redirects the web surface to
`/app/#outcome=LinkRequired&linkPendingToken=<opaque>` (no `token` param).

**Existing behaviour (verified)**: the eager pre-boot script in
`Sorcha.UI.Web/wwwroot/app/js/fragment-handoff.js` only stages + clears when a `token` param exists.
A `LinkRequired` fragment is therefore ignored today → dead-end + token left in URL.

**New behaviour (this feature)**: when `outcome === "LinkRequired"` and `linkPendingToken` present:
1. Stage `{ outcome: "LinkRequired", linkPendingToken }` in `window.__sorcha_link_pending` and
   `localStorage['sorcha:link-pending']` (parallel to the existing token staging).
2. `history.replaceState(null, '', pathname + search)` immediately — strips the token from the
   address bar/history (FR-002, SC-005).

**New accessors** on `window.sorcha.fragmentHandoff`:
| Function | Returns | Notes |
|----------|---------|-------|
| `getLinkPending()` | `{ linkPendingToken } \| null` | peek without clearing (gate reads on boot) |
| `clearLinkPending()` | `void` | clears window global + localStorage once the flow ends (success, cancel, or terminal failure) |

**Reload/back-nav (edge)**: after `clearLinkPending()` nothing is staged ⇒ `getLinkPending()` null ⇒
signed-out home; never a partial link or crash (FR-003).

---

## B. Boot-time gate (`LinkRequiredGate.razor`, mounted in `Routes.razor`)

- Mounted **outside** `AuthorizeRouteView` (same seam as `FragmentTokenHandler`) so it runs with no
  `ClaimsPrincipal`.
- `OnAfterRenderAsync(firstRender)` → `getLinkPending()`. If present, render
  `LinkExistingAccountPrompt` as a full-screen takeover and suppress the normal signed-out home.
- If absent, render nothing (transparent passthrough).

---

## C. Session establishment on confirm success (reuse, no new path)

On `ConfirmOutcome.Linked`, the prompt establishes the session via the **existing** post-sign-in
machinery — identical to a normal social callback:

1. Persist `{accessToken, refreshToken}` through `ITokenCache.StoreTokenAsync` (or stage via the same
   `window.__sorcha_fragment_token` + `localStorage['sorcha:fragment-pending']` mechanism the social
   callback uses), **then** `clearLinkPending()`.
2. Trigger `CustomAuthenticationStateProvider` to re-evaluate
   (`NotifyAuthenticationStateChanged` / re-run `GetAuthenticationStateAsync`), which validates
   expiry, reads JWT claims, and produces the signed-in `ClaimsPrincipal` (identity type `"jwt"`).
3. Navigate to the default signed-in landing (`/` / dashboard), exactly as a social sign-in leaves
   the user (FR-011, SC-006).

**Invariant**: no bespoke token-handling path is introduced — the confirm tokens flow through the
same validate/store/notify sequence as `TryConsumeFragmentTokenAsync` to guarantee identical
signed-in state.

---

## D. Isolation guarantees (SC-007)

- No edits to `AuthChallengeDialog.razor` or any Feature 150 `Security/` component.
- `FragmentTokenHandler.razor` is referenced as the pattern for the gate but is not modified (its
  `returnUrl` responsibility is unchanged).
- The only edit to shipped files is the **additive** extension of `fragment-handoff.js` and the
  one-line mount in `Routes.razor`; all prompt/service/model code is net-new files.
