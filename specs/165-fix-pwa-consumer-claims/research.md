# Phase 0 Research: Fix PWA Consumer-Token Claims

**Feature**: 165 | **Date**: 2026-06-26

This phase resolved the spec's central uncertainty — *where* the consumer token loses its identity — by reading the actual minting and resolution code. The headline finding overturns the naive reading of the symptom and re-points the fix.

---

## Finding 0 (decisive): The minting code already adds `platform_user_id`

**Decision**: Do **not** "add the missing claim to the minter." It is already there. Treat minting as correct-but-unverified and lock it with regression tests; put the real fix on the deployment + backend-resolution side.

**Evidence**:
- `TokenService.GenerateUserTokenAsync` (`src/Services/Sorcha.Tenant.Service/Services/TokenService.cs:103-111`) builds the common human-token claim list and adds `new("platform_user_id", platformUserId.ToString())` **unconditionally** for both Consumer and Platform tiers.
- The tier branch at `TokenService.cs:123-131` only adds **roles + wallet address** for `Tier.Platform` — confirming the Feature 136 boundary (consumer = no roles, no wallet binding) is intact and that `platform_user_id` is *not* gated behind it.
- All interactive issuance paths pass a real platform user id into this method: password login / org-selection (`LoginService.cs:296`, `:408`), social callback (`SocialCallback.cshtml.cs:207`), 2FA Razor (`Login.cshtml.cs:293`), 2FA API (`AuthEndpoints.cs:513`), passkey assert (`PublicPasskeyEndpoints.cs:387`), org switch (`AuthEndpoints.cs:1028`), enterprise OIDC (`OidcCallback.cshtml.cs:217`, `:334`), passkey/totp/social API variants.
- **Refresh** re-emits the claim and already has a recovery fallback: `RefreshTokenAsync` reads `platform_user_id` from the refresh principal and, if absent, recovers it from `UserIdentity.PlatformUserId` (`TokenService.cs:313-324`), then re-adds it.

**Rationale**: The spec's own Assumptions anticipate this ("The root cause is path/coverage and/or deployment, not page logic"). The code audit confirms the *path coverage* is already complete in source. That leaves **deployment** and **backend resolution** as the live defects.

**Alternatives considered**: Adding the claim in each call site (rejected — redundant, the central minter already owns it); inventing a new claim (rejected — spec forbids new claim names/formats).

---

## Finding 1: The backend fallback mis-binds to the org-scoped id

**Decision**: Harden `ResolveCitizenContext` in the Wallet Service so a token lacking `platform_user_id` recovers the **true** platform user id from `sub` via the identity registry, instead of treating `sub` as if it were the platform user id.

**Evidence**:
- `ResolveCitizenContext` (`src/Services/Sorcha.Wallet.Service/Endpoints/CitizenWalletEndpoints.cs:587-599`) computes:
  `platform_user_id ?? ClaimTypes.NameIdentifier` → parse as the citizen's platform user id.
- But `ClaimTypes.NameIdentifier` maps to `sub`, and `sub` is set to `user.Id` = **`UserIdentity.Id`** (the org-scoped user id) at mint time (`TokenService.cs:105`) — **not** the platform user id.
- Device records are keyed by **platform user id** (`deviceClient.ListAsync(platformUserId, …)`, `CitizenWalletEndpoints.cs:266`). So on a token missing the claim, the fallback yields a *valid GUID that is the wrong identity* → device queries return an empty/foreign set. The page renders blank/"unauthorized", matching the reported symptom. This is a **silent** mis-bind, not a 401.
- A correct pattern already exists elsewhere: the Tenant Service's `PlatformUserDeviceEndpoints.ResolvePlatformUserIdAsync` (`PlatformUserDeviceEndpoints.cs:215-234`) reads `platform_user_id`/`pid`, and on absence falls back to `sub` **and then queries the identity registry to recover the real `PlatformUserId`** from the `UserIdentity`. That is the shape to mirror in the Wallet Service.

**Rationale**: Satisfies FR-007 (legacy/pre-fix tokens must *degrade, not error*) correctly — recovering the right identity rather than binding to the wrong one. Reuses an established, audited resolution pattern (no novel mechanism).

**Alternatives considered**:
- Leave the `sub` fallback as-is (rejected — it resolves the wrong identity and is the proximate cause of the blank surfaces for legacy tokens).
- Drop the fallback and hard-401 when `platform_user_id` is absent (rejected — violates FR-007; in-flight pre-fix sessions would break instead of degrading).
- Resolve wallet purely by owner lookup (already done for enrolment via `ResolveCitizenContextAsync`, `CitizenWalletEndpoints.cs:610-643`) — good for wallet address, but the **device** endpoints still need the correct platform user id as the key, so identity recovery is the necessary piece.

---

## Finding 2: Deployment lag on `n1`

**Decision**: Treat "redeploy current build to `n1` and verify interactively" as a first-class, required step of this feature, not an afterthought.

**Rationale**: Since the minting fix already exists in source, citizens hitting the failure on `n1` are being served by an older build whose tokens predate `TokenService.cs:110`. FR-008 and the spec Assumptions make `n1` the canonical verification network and require a *real interactive sign-in*, not a synthetic-token test. The unified versioning scheme (CLAUDE.md §14, images tagged `:2.<run>.<attempt>`) means verification must record the deployed image/version so the evidence is unambiguous.

**Alternatives considered**: Synthetic minted-token integration test only (rejected — necessary but not sufficient; spec demands interactive `n1` proof).

---

## Finding 3: The three surfaces share one root cause

**Decision**: Fix the identity seam once; verify all three surfaces.

**Evidence**: Surface-to-backend mapping —
- **Security** (`Sorcha.Wallet.Pwa/Pages/Security.razor`) mounts the shared `SecurityHome` component; its data is citizen-identity scoped (resolved from the same claim).
- **Devices** (`Devices.razor:127`) → `ICitizenWalletClient.ListDevicesAsync()` → `GET /api/v1/wallet/devices` → `ResolveCitizenContext` (no-fallback site).
- **Add a phone** (`Enrol.razor:272`) → enrolment → `POST /api/v1/wallet/devices/enrol` → `ResolveCitizenContextAsync` (already has a wallet-by-owner fallback, so it is the most resilient of the three; still needs the correct platform user id for device registration).

**Rationale**: All three resolve the citizen from the same claim; the Devices/Add-phone paths additionally key device records by platform user id. Hardening the single resolution seam (plus the deploy) is expected to clear all three, consistent with the spec's "share one root cause" assumption.

**Open verification item (not a blocker)**: Confirm the exact backend call behind `SecurityHome` during the `n1` pass so its identity dependency is recorded in the quickstart evidence. It is claims-driven like the others; no separate code path is anticipated.

---

## Finding 4: Optional consistency cleanup — `platform_user_id` constant

**Decision (optional, low priority)**: Add a `PlatformUserId = "platform_user_id"` constant to `TokenClaimConstants` (`src/Common/Sorcha.ServiceClients.Http/Auth/TokenClaimConstants.cs`) and use it at the mint and read sites, replacing the scattered string literals.

**Rationale**: The claim name is currently a bare string literal at every site (`TokenService.cs:110`, `CitizenWalletEndpoints.cs:590`, `PlatformUserDeviceEndpoints.cs:220`). Centralising it prevents a future typo-drift between mint and read. **This is conformance, not a new claim** — the wire name is unchanged, so it respects the spec Assumption "No new claim names or token formats are introduced."

**Alternatives considered**: Leave literals (acceptable; lower-risk but leaves the drift hazard). Sequence this *after* the behavioural fix so it never masks a regression.

---

## Summary of resolved unknowns

| Spec unknown | Resolution |
|--------------|------------|
| Where is the claim dropped? | **Not in minting** — minting adds it for all consumer paths (`TokenService.cs:110`). Live defects are deployment lag on `n1` + an unsafe Wallet-side fallback. |
| Why do surfaces fail despite a fallback? | The Wallet `ResolveCitizenContext` fallback resolves `sub` (= org-scoped `UserIdentity.Id`), the **wrong** key for device lookups → silent empty result. |
| How to support legacy tokens (FR-007)? | Recover the real platform user id from `sub` via the identity registry — mirroring `PlatformUserDeviceEndpoints.ResolvePlatformUserIdAsync`. |
| Is the tier boundary at risk? | No. Roles + wallet binding stay platform-only (`TokenService.cs:123-131`); audience mapping in `SorchaAudiences` is untouched. |
| How is "fixed" proven? | Interactive citizen sign-in on `n1` loading all three surfaces, with the deployed image/version recorded (FR-008, SC-001…006). |

All NEEDS CLARIFICATION items from Technical Context are resolved. Proceed to Phase 1.
