# Quickstart: Cold-start onboarding and device pairing UX (Feature 128)

**Audience:** Operator validating the four routes against a running environment (local docker-compose or n1.sorcha.dev). Assumes the feature branch has been deployed.

**Prerequisites:**
- Sorcha platform up via `docker-compose up -d` or n1 deploy refreshed per the `n1-deploy` skill.
- A test citizen seed account NOT yet used for pairing (zero paired devices). Create one fresh via Sorcha Web signup, OR reset existing test account devices via the admin tooling.
- A phone (or second browser profile that supports installable PWAs) for the cross-device routes.

## Route 1 — In-PWA pairing takeover (Story 1, P1)

**Setup:**
1. Sign up a new citizen account at the Sorcha Web signup page using a fresh email. Do NOT scan the post-signup QR — close the browser tab instead so no device gets paired this way.
2. On a phone (or installable-PWA-capable browser), navigate to `https://wallet.sorcha.dev/wallet/` (n1) or `http://localhost/wallet/` (local) and install/Add-to-Home-Screen.
3. Open the installed PWA. Sign in with the citizen credentials from step 1.

**Expected:**
- The PWA renders the `PairingTakeover` full-screen — no nav, no Home, no skip.
- Headline: "Set up this device". One primary button: "Set up".
- Below the primary button, an expandable "Already started on another device?" disclosure containing a 6-digit code input.

**Verify FR-010 / FR-011 / FR-012:**
- Try every navigation path you can think of (browser back button, MainLayout navigation if visible, deep-link to a known wallet page). None should escape the takeover.
- Tap "Set up". The F114 device-pairing ceremony runs in-place. On success, the takeover dismisses and you land on the standard wallet Home with the device visible under My Devices.

**Verify FR-014 (auto-dismiss on remote pair):**
- Sign up a *second* fresh citizen account.
- Open the PWA on Phone A (signed in as the new account). Confirm takeover shows.
- On a separate device, sign in as the same account and complete pairing via Route 2 (below).
- Phone A's takeover should dismiss within a few seconds without you tapping anything on Phone A.

## Route 2 — Desktop → phone handoff (Story 2, P1)

**Setup:** Fresh signup at the Sorcha Web signup page in a desktop browser.

**Expected immediately after signup:**
- Browser routes to `/setup/add-device` showing:
  - A large QR code
  - "Email me a link" affordance
  - "Skip for now" link

**Happy path — scan + pair:**
1. Scan the QR with the phone camera. The phone opens the wallet PWA at `/enrol?session=...`.
2. The PWA shows the standalone copy variant ("You're being set up").
3. Pairing ceremony runs. Both the phone and the desktop tab show success within seconds.

**Verify FR-022 (email handoff):**
1. Repeat with a new citizen. On the handoff, click "Email me a link".
2. Check the inbox (or n1 email log). The email is Sorcha-branded (no org logo), contains a single resumption link.
3. Click the link in a fresh browser tab — you're re-authenticated and land back on `/setup/add-device` with a fresh QR.

**Verify FR-023 / FR-024 / FR-025 (skip path):**
1. Repeat with a new citizen. On the handoff, click "Skip for now".
2. You land on Sorcha Web with a persistent banner: "You haven't paired a phone — credentials can't be received. [Pair my phone]".
3. Dismiss the banner. Navigate to the devices area; the "Add my phone" menu entry is present.
4. Click either the banner link OR the menu entry — both return you to `/setup/add-device`.

**Verify FR-026 (no auto-route for already-paired citizens):**
1. Sign out, sign back in as a citizen who already has ≥1 paired device.
2. After signin, you should NOT be auto-routed to `/setup/add-device` — you land on the normal post-signin destination.

## Route 3 — Mobile-web → same-phone PWA install (Story 3, P2)

**Setup:** Fresh signup at the Sorcha Web signup page in mobile Safari (iOS) or mobile Chrome (Android). Phone must not already have the wallet PWA installed.

**Expected immediately after signup:**
- Browser routes to `/setup/add-device`.
- Page detects the installable mobile context and shows the install-flavoured variant — "Install Sorcha Wallet" button (or iOS Add-to-Home-Screen instructions).
- A 6-digit short code is visible on the page below the install button — FR-032.

**Happy path — Android Chrome (seamless):**
1. Tap "Install Sorcha Wallet". Browser shows the WebAPK install prompt; accept.
2. Open the installed wallet from the home screen.
3. The wallet opens already paired — no takeover, no short code entry needed.

**Fallback path — iOS Safari (or Android happy-path failure):**
1. Follow the on-screen Add-to-Home-Screen instructions.
2. Open the installed wallet from the home screen.
3. Wallet opens to the PairingTakeover (Route 1 takeover).
4. Tap "Already started on another device?", enter the 6-digit code from the original Safari tab.
5. Pairing completes; takeover dismisses.

**Verify FR-031 / FR-032:** the short code MUST be visible without further interaction on the install-flavoured handoff — verify by reload + visual inspection.

## Route 4 — App-store cold landing (Story 4, P3)

**Unauthenticated visit:**
1. Open an incognito/private browser window.
2. Navigate to `https://sorcha.dev/get` (or whatever the deployed landing path is — check `Sorcha.UI.Web/Pages/Get.cshtml`).
3. Page renders:
   - Explanation of what Sorcha is and the B2B2C model.
   - A "Find services that use Sorcha" link.
   - An "I already have an account — sign in" affordance.

**Signed-in pass-through to handoff:**
1. From the landing, click "Sign in". Authenticate as a citizen with zero paired devices.
2. After signin, you are routed to `/setup/add-device` (not directly into a wallet view).

## Cross-cutting checks

### Mode/context enforcement (FR-003, SC-007)

1. As a developer, use curl to attempt: `POST /api/auth/enrol-session` with `{ "mode": "gated" }` and no `returnTo`. Expect 400 `mode-context-mismatch`.
2. Same with `{ "mode": "standalone", "returnTo": "https://example.com" }`. Expect 400 `mode-context-mismatch`.

### Replay rejection (FR-050)

1. Mint a standalone enrol-session token.
2. Redeem it successfully via the PWA.
3. Replay the same token to `/redeem`. Expect 400 `replay-token` with the user-facing copy "this code has already been used".
4. Same flow with a short code — mint, redeem, replay. Expect 400 `replay-code`.

### Expiry rejection (FR-051)

1. Mint a short code (5 minute TTL).
2. Wait 6 minutes.
3. Redeem. Expect 400 `expired-code` with the user-facing copy "this code has expired, please return and try again".

### Telemetry visibility (FR-053, SC-005)

1. Run through Routes 1–4 once each.
2. Query the Sorcha OTel metrics endpoint (or Aspire dashboard) for `sorcha_pair_mint_total` and `sorcha_pair_redeem_total`.
3. Verify all four `route` values appear and that the `mode` breakdown shows `standalone` for the new routes and `gated` for any council-gate flow exercised separately.

## Common issues

- **PWA takeover doesn't render** → check `HasPairedDeviceProbe` cache; the `GET /api/devices/has-any` endpoint might be returning true. Inspect via browser devtools network tab.
- **iOS install never opens wallet paired** → expected; iOS is the lossy path. The short code is the recovery.
- **QR scan opens wrong app** → ensure the scanner is the system camera and the deep link is the wallet PWA's scope (`/enrol`). Some QR apps strip query strings.
- **"Email me a link" never arrives** → check Mailpit (local) or the n1 email log. The F112 dispatcher logs failures via `sorcha_pair_resumption_email_total{result=dispatch_failed}`.
- **Mode mismatch 400 on a council-gate flow** → existing F126 callers must not send a `mode` field (or must explicitly send `"gated"`). Default behaviour is preserved; check the calling component.
