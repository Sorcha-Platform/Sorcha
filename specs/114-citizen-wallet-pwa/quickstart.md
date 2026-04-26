# Quickstart — Citizen Wallet PWA

**Feature**: 114-citizen-wallet-pwa
**Audience**: developers picking up implementation; testers validating end-to-end after deploy.

This document walks through the canonical "happy path" for the v1 citizen wallet: install → enrol → cache credentials → present offline → recover after device loss. Each section includes the exact dev-env steps and what success looks like.

---

## 0. Prerequisites

- Sorcha development environment running per `CLAUDE.md` (`docker-compose up -d` or `dotnet run --project src/Apps/Sorcha.AppHost`).
- The new wallet + verifier services running:
  - **Wallet PWA**: `http://localhost/wallet/` (Aspire HTTPS port 7400)
  - **Reference Verifier**: `http://localhost/verify/` (Aspire HTTPS port 7401)
- A pre-existing platform user with at least one issued credential. The walkthroughs/TradeFinance setup creates these; alternatively, run an open-participant flow in the existing Sorcha web UI to issue a Verified Citizen credential.

---

## 1. Install the wallet on your phone or desktop

Open `http://localhost/wallet/` in any modern browser. The browser prompts to install (Chrome shows an install icon in the address bar; iOS Safari uses Share → Add to Home Screen).

**Success**: app icon on home screen / launcher; opens to the wallet's sign-in page in standalone mode (no browser chrome).

## 2. Enrol the device

1. Tap "Sign in to your Sorcha account."
2. Complete the existing sign-in flow (email + password, social, or passkey). The wallet receives a JWT scoped with audience `sorcha:citizen-wallet`.
3. The wallet generates two non-extractable keys via WebCrypto (ECDSA P-256 for signing, HMAC-SHA256 for content wrapping) and stores both in IndexedDB.
4. Wallet computes the JWK thumbprint of the signing key, then POSTs `/api/v1/wallet/devices/enrol` with `{ deviceLabel, devicePublicJwk, platform, userAgent }`.
5. Server (Wallet Service) derives (or fetches) the citizen's holder key under `sorcha:citizen-holder` (slot 108), issues the device delegation credential, allocates a status-list bit, returns `{ deviceId, delegationCredential, holderPublicJwk, statusListUri, statusListIndex, delegationExpiresAt }`.
6. Wallet stores the delegation, then immediately calls `GET /api/v1/wallet/credentials` to seed its credential cache.

**Success**:
- The wallet's home screen shows your credentials as id-cards (rendered with the Feature 107 `IdCardLayout` component).
- A device entry appears in `GET /api/v1/me/devices` and in the existing main Sorcha web UI's "My Devices" page.

**Validation queries**:
```bash
# From any authenticated context
curl -H "Authorization: Bearer $JWT" http://localhost/api/v1/me/devices
# → 200 OK with one device summary
```

---

## 3. Browse credentials and inspect details

In the wallet:
- Home shows a list of credentials.
- Tap any credential → id-card detail view, expanded to show the full attribute set the issuer included.
- Pull-to-refresh (or any focus regain) triggers a `/wallet/sync` call; new credentials appear automatically.

**Success**: every credential the citizen holds on the server is visible offline.

## 4. Make an offline presentation

### 4a. Verifier setup

In a second browser window or another device, open `http://localhost/verify/{verifierOrgId}/{purpose}`. The reference verifier shows a QR code containing an OID4VP `openid4vp://` deep link.

For testing offline behaviour:
- In Playwright, use `BrowserContext.SetOffline(true)` on both wallet and verifier contexts.
- In manual testing, disable Wi-Fi / cellular on the wallet device.

### 4b. Citizen flow

1. Tap "Present a credential" in the wallet → camera scanner opens.
2. Scan the verifier's QR. Wallet parses the request, extracts `presentation_definition`, `nonce`, `client_id`, `response_mode`.
3. Wallet matches presentation definition against cached credentials. Consent screen shows:
   - The verifier label (untrusted, displayed only)
   - Mandatory disclosed attributes (pre-checked, locked)
   - Optional disclosed attributes (unchecked by default)
4. Citizen taps "Hold to share." Wallet:
   - Builds an SD-JWT VC presentation with selective disclosure applied
   - Signs a key-binding JWT with the device key (audience = `client_id`, nonce = verifier nonce)
   - Includes the device delegation credential
5. Depending on `response_mode`:
   - `direct_post`: wallet POSTs to verifier's response endpoint (works on local network without internet)
   - `direct_post.qr`: wallet displays VP as a QR for the verifier to scan
6. Verifier validates the chain locally: issuer signature → holder signature on delegation → device signature on KB-JWT → status-list bits (refreshed within 24h).
7. Wallet writes a local presentation log entry.

**Success**:
- Verifier displays "Accepted ✓" with the disclosed attributes.
- Wallet's "Recent activity" view shows the presentation with credential reference, disclosed claims, verifier label, timestamp.
- Both happen with `setOffline(true)` enforced — no platform contact during the exchange.

### 4c. After both devices return online

Wallet auto-syncs on next focus regain:
- Posts the new presentation log entry to `/wallet/presentations/log`
- Server forwards to Blueprint Service `OfflinePresentationConsumer`
- Lifecycle events `PresentationInitiated` + `PresentationOutcome` written to the originating register, with the offline timestamps preserved (per Feature 111 contract extension)

**Validation**:
```bash
# Inspect the originating register's transaction stream
curl -H "Authorization: Bearer $JWT" \
  http://localhost/api/v1/registers/{registerId}/transactions?type=PresentationInitiated
# → includes the new entry with the offline `presentedAt` (not the catch-up timestamp)
```

---

## 5. Recover after losing the device

Simulate device loss by clearing wallet IndexedDB and uninstalling the PWA from one device.

1. On any other device (or even the existing main Sorcha web UI), sign in with the same platform account.
2. Navigate to "My Devices" — see the lost device with its label.
3. Tap "Revoke."
4. Server: marks `PlatformUserDevice.Status = Revoked`, flips the status-list bit, regenerates the signed list, publishes a `deviceRevoked` SignalR event.
5. Within 24h (configurable), every verifier that refreshes its cached status list will reject any future presentation from the revoked device.
6. Citizen installs the wallet on a new device and enrols it (Step 2). All credentials reappear immediately — no re-issuance from any issuer.

**Validation**:
```bash
# Verifier-facing status list
curl http://localhost/api/v1/wallet/status/{orgId}/citizen-devices/{listId}.statuslist+jwt
# → JWT with the revoked device's bit set
```

**Failure mode**: an attacker who finds the revoked device and regains network access cannot re-enrol — the device's IndexedDB is local-only and re-enrolment requires a fresh authentication flow. The lost-device key cannot be reused.

---

## 6. Renewal (silent)

Device delegation credentials expire after 12 months by default. The wallet checks expiry on every online open; if `exp - now < 30 days`, it silently calls `POST /api/v1/wallet/devices/renew-delegation` and stores the new delegation.

**Validation** (forced renewal in dev):
- Set `Wallet:DelegationLifetimeDays` to a small value (e.g. 1) in the Wallet Service config.
- Re-enrol a device, wait the configured lifetime / 12 (so renewal window kicks in), and observe the renewal call in the Wallet Service logs.

---

## 7. Cross-context Playwright smoke

The end-to-end happy path is validated in CI by `tests/Sorcha.Citizen.Wallet.E2E.Tests/Docker/PresentationFlowTests.cs`:

```csharp
[Test]
[Category("CitizenWallet")]
[Category("Smoke")]
public async Task PresentationFlow_OfflineEndToEnd_Succeeds()
{
    // Two browser contexts in the same Playwright session
    await using var citizenContext = await Browser.NewContextAsync();
    await using var verifierContext = await Browser.NewContextAsync();

    var citizenPage = await citizenContext.NewPageAsync();
    var verifierPage = await verifierContext.NewPageAsync();

    // Pre-enrol the citizen and pre-load a credential (test fixture)
    await CitizenWalletPage.EnrolAndCacheAsync(citizenPage, TestFixtures.SeededCitizen);

    // Take both offline
    await citizenContext.SetOfflineAsync(true);
    await verifierContext.SetOfflineAsync(true);

    // Verifier shows QR
    await verifierPage.GotoAsync($"{TestConstants.VerifierUrl}/age-check");
    var qrPayload = await VerifierPage.ReadQrPayloadAsync(verifierPage);

    // Citizen scans (programmatically — bypass camera)
    await CitizenWalletPage.PresentToQrAsync(citizenPage, qrPayload);

    // Verifier confirms
    await VerifierPage.AssertOutcomeAsync(verifierPage, VerifierOutcome.Accepted);

    // Citizen activity log
    await CitizenWalletPage.GotoActivityAsync(citizenPage);
    await CitizenWalletPage.AssertLatestEntryAsync(citizenPage, c => c.Outcome == "Presented");

    // Restore network and verify lifecycle catch-up
    await citizenContext.SetOfflineAsync(false);
    await Task.Delay(2000);  // sync settle
    await AssertRegisterContainsPresentationLifecycleAsync(/* ... */);
}
```

---

## 8. Troubleshooting

| Symptom | Cause | Fix |
|---|---|---|
| Wallet won't install | Service worker not registered (dev build) | Rebuild with `dotnet publish -c Release`; dev runs use `service-worker.js` (no caching) |
| "Device clock looks wrong" banner | Local time skewed > 5 minutes from server | Sync device time |
| Verifier "Status list expired" | Verifier offline > 24h | Verifier must regain network; this is by design |
| Credentials missing after enrol | Issuer used pre-Feature-114 holder key (`credential-holder-binding` slot 105) | Credential not usable in wallet — requires re-issuance with citizen-holder slot 108 binding (handled via display flag in spec FR-D invariants) |
| Sync token rejected with 410 | Sync token > server retention window | Wallet auto-recovers by re-syncing without `since` |

---

## 9. What's NOT in v1 (callouts)

- No persona offline (Feature 092 integration) — Phase 2.
- No native iOS/Android app — Phase 4.
- No NFC / BLE proximity — Phase 5.
- No mdoc credentials — Phase 6.
- No external (non-Sorcha) verifier interop guarantee — Phase 3.

These are documented so testers don't accidentally treat their absence as a bug.
