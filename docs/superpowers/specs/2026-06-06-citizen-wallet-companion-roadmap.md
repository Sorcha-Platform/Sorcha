# Citizen Wallet PWA — Device Setup & Sync Assessment + Companion-First Roadmap

**Date:** 2026-06-06
**Status:** Direction agreed (companion-first). P0/P1/P2 below are the backlog.
**Decision owner:** Stuart Fraser

---

## 1. Decision

The Citizen Wallet PWA is a **companion** to the full web app, not (yet) a self-contained
mobile wallet. The web app owns **account creation, wallet creation, signup, and recovery**;
the PWA owns **sign-in, device pairing, and hold / sync / present / verify**.

"First-class" for this milestone means: **the web→PWA handoff and the hold/sync/present loop
are flawless.** Making the PWA self-contained (in-app wallet creation, signup, recovery) is a
**separate future milestone**, explicitly out of scope here.

This decision is why, in the 2026-06-06 PWA work, we: suppressed the guided tour in the PWA,
gated signed-in chrome off the sign-in page, and added a "Create an account → web signup" link.
Those were all symptoms of treating the PWA as self-contained when it isn't.

---

## 2. Demonstrable baseline (what is proven to work on n1)

The AssuredIdentity Phase-1 walkthrough runs the full spine green on n1:

> web creates account + wallet → citizen signs into the PWA → enrols / pairs **this device**
> (non-extractable WebCrypto P-256 key → holder-signed delegation + status-list slot → Tenant
> device registry) → an issuer mints a credential → it **syncs to the PWA and the citizen
> claims it**.

So the core companion loop is real, not theoretical. The roadmap below hardens the edges of
this proven spine.

> Note: an automated audit initially reported credential sync events as an "empty v1 stub."
> That is stale — `EfCoreCitizenCredentialEventStream` (Wallet Service `Program.cs:175`) queries
> `CitizenCredentialEventLog`; the walkthrough delivering a credential is the proof.

---

## 3. Current state (evidence-backed)

### Works (proven)
| Capability | Evidence |
|---|---|
| Web wallet creation | `Sorcha.Wallet.Service/Endpoints/WalletEndpoints.cs` `POST /api/v1/wallets` |
| PWA device enrolment (F114) | `Sorcha.Wallet.Pwa/Services/IEnrolmentService.cs`; `CitizenWalletEndpoints.cs` `POST /api/v1/wallet/devices/enrol` |
| Non-extractable device key | `Sorcha.Wallet.Pwa/Services/WebCryptoDeviceKeyService.cs` (EC P-256 via WebCrypto) |
| Device registry | Tenant `Models/PlatformUserDevice.cs` (thumbprint-idempotent) |
| Pairing takeover (F128) | `Sorcha.Wallet.Pwa/Components/PairingTakeover.razor` |
| 6-digit short-code pair | Tenant `PairingShortCodeEndpoints.cs`; PWA `PairingShortCodeRedeemer.cs` (TTL + rate-limited) |
| QR / enrol-session handoff | `Sorcha.UI.Components.User/Components/Pairing/PairingHandoffSurface.razor` |
| Multi-device + list (rename/revoke) | `Sorcha.Wallet.Pwa/Pages/Devices.razor`; `CitizenWalletEndpoints` revoke broadcasts SignalR + inbox entry |
| Credential sync (delta + snapshot) | `Sorcha.Wallet.Pwa/Services/ISyncService.cs` `GET /api/v1/wallet/sync`; signed 30-day cursor |
| Encrypted local cache | `IndexedDbCredentialCache.cs` (XChaCha20-Poly1305, device-bound) |
| Realtime sync push | `CitizenWalletHubConnection.cs` `CredentialAvailable` → silent sync |
| Offline + freshness cues | `Components/OfflineBanner.razor`; "last synced" hint in `Pages/Index.razor` |
| Delegation renewal (client) | `Services/IDelegationRenewalClient.cs` → `POST /api/v1/wallet/devices/renew-delegation` |

### Partial
- **Delegation renewal is silent + client-only.** If the PWA is not opened within 30 days of
  `DelegationExpiresAt`, the device quietly loses the ability to present. No server pre-renewal,
  no reminder. (`IDelegationRenewalClient` only runs on PWA open.)
- **Revoked credentials stay in the cache.** Revocation deltas are processed but the row is not
  removed (`ISyncService.cs` revocation branch — no `ICredentialCache.RemoveAsync`); the verifier
  rejects them, but the UI still shows them.
- **Offline revocation lag.** A revoked device can still present offline until its next sync.
- **Status-list signatures** trusted via TLS only; the verifier does the authoritative check
  (`IndexedDbStatusListService.cs`).

### Gaps / stubs
- **Recovery is not implemented.** Passkey + org recovery return `501` behind
  `Features:WalletRecoveryEnabled` (default off); signature verification unimplemented
  (`WalletEndpoints.cs` `RecoverViaPasskey` / `RecoverViaOrg`). No citizen self-recovery if a
  device is lost and cannot be revoked from another.
- **PWA can't create a wallet or sign up** (web-only). Critically, **PairingTakeover's "Set up
  this device" assumes a wallet already exists** — enrol resolves a wallet from the token, so a
  citizen who reaches the PWA without a web-created wallet dead-ends.
- **No web-push / background sync** (service worker is a no-op: `wwwroot/service-worker.js`). No
  "your credential arrived" when the app is closed; sync only while open or hub-connected.
- **No device audit affordance.** Tenant stores `LastSeenAt`, but `/devices` doesn't surface it,
  so a citizen can't spot a rogue pairing.
- No device quotas / enrol rate-limit.

---

## 4. Companion-first roadmap

### P0 — correctness / safety (do first)
1. **Make PairingTakeover wallet-aware.** ✅ **DONE (Feature 149).** Detect "has a wallet?" (not
   just "has a device?") via `GET /api/v1/wallet/exists` + one-shot `IHasWalletProbe`, and route a
   walletless citizen to the web `/wallets/create` handoff instead of offering "Set up this device"
   (which dead-ended at the enrol 404). Same companion-first pattern as the tour suppression +
   signup link. Design: `docs/superpowers/specs/2026-06-06-pwa-pairing-takeover-wallet-aware-design.md`.
2. **Make recovery honest.** Decide: implement passkey-recovery verification, OR explicitly scope
   recovery to the web app for now and make the PWA's messaging/affordances match (no dead
   recovery paths, a clear "recover on the web" route). Companion-first leans toward the latter
   short-term.

### P1 — first-class companion polish
3. **Delegation-renewal safety net.** Server-side pre-renewal, or an inbox/push reminder before
   the silent 30-day expiry, so a device never silently dies.
4. **Remove revoked credentials from the cache** (`ICredentialCache.RemoveAsync`) so the UI
   matches verifier reality.
5. **Surface device `LastSeenAt`** on `/devices` (data already in Tenant) for rogue-pairing
   visibility.

### P2 — reach
6. **Web-push + service-worker background sync** for real "credential arrived" notifications when
   the app is closed.
7. **Then** evaluate the self-contained PWA milestone (in-app wallet creation / signup / recovery)
   as its own plan.

---

## 5. Out of scope (future milestone: self-contained PWA)
- In-PWA wallet creation (mnemonic generation + backup UX).
- In-PWA signup.
- In-PWA recovery (passkey/org) with full crypto verification.

When that milestone starts, the suppressed onboarding can be re-enabled — see
`pwa-reenable-onboarding-tour` (tour) and the signup-link revert.

---

## 6. References
- Sync architecture detail: this assessment §3 + `Sorcha.Wallet.Pwa/Services/ISyncService.cs`.
- Device/enrolment detail: `Sorcha.Wallet.Pwa/Services/IEnrolmentService.cs`,
  `Sorcha.Wallet.Service/Endpoints/CitizenWalletEndpoints.cs`, Tenant `PlatformUserDevice`.
- Related 2026-06-06 PWA changes: PR #971/#972 (auth/session hardening), #973 (login chrome +
  tour suppression + build badge + signup link), #974 (tour-loop tests).
