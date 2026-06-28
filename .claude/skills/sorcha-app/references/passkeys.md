# Passkeys (WebAuthn) in the wrapped apps — PROVEN end-to-end 2026-06-24 (iOS)

The Capacitor-wrapped PWA runs WebAuthn (`navigator.credentials.create/get`) inside the
device WebView. Getting passkey sign-in to work took **eight** coordinated pieces across the
app, the Apple/Play accounts, the web/CI pipeline, and the n1 server. Miss any one and you get a
silent "Passkey sign-in was cancelled" (iOS refused before any UI) or a generic app crash.

**iOS works. Android does NOT** — the Android System WebView does not expose
`window.PublicKeyCredential`, so `IsSupportedAsync()` is false and the button hides even with a
valid `assetlinks.json`. Android passkeys need the **native Credential Manager**, not the WebView
(out of scope until someone wires a native plugin). Social login is unaffected.

## The relying-party (RP) model — parent domain

Use the **parent domain `sorcha.dev`** as the WebAuthn RP ID, not the per-host subdomain. WebAuthn
permits the RP ID to be a *registrable parent* of the ceremony origin, so RP `sorcha.dev` is valid
from origin `https://n1.sorcha.dev`, and **one passkey roams across every `*.sorcha.dev`
installation**. Apple does **not** support wildcards for `webcredentials:` (only `applinks:`), so the
parent-domain RP is the only way to get "all subdomains" from one entry.

A passkey is bound to its RP ID **at creation**. Changing the RP ID orphans existing passkeys — the
user must re-register (or have the account reset; see "Account reset" below).

## The eight pieces (all required for iOS)

1. **App-ID capability** — `Associated Domains` enabled on App ID `app.sorcha.wallet` (team
   `HY5HSW5FUT`). `fastlane produce enable_services` wants an Apple-ID username (not configured);
   do it headlessly with the **ASC API key** via Spaceship ConnectAPI:
   ```ruby
   require "json"; require "spaceship"
   m = JSON.parse(File.read(File.expand_path("~/.sorcha-signing/asc_api_key.json")))
   Spaceship::ConnectAPI.token = Spaceship::ConnectAPI::Token.create(
     key_id: m["key_id"], issuer_id: m["issuer_id"],
     filepath: File.expand_path("~/.sorcha-signing/asc_api_key.p8"))
   b = Spaceship::ConnectAPI::BundleId.all.find { |x| x.identifier == "app.sorcha.wallet" }
   b.create_capability("ASSOCIATED_DOMAINS") unless b.get_capabilities.map(&:capability_type).include?("ASSOCIATED_DOMAINS")
   ```
   Run it `bundle exec ruby` from `mobile/wallet` (so Spaceship is on the load path).

2. **`match` profile regen** — adding a capability to the App ID does **not** update existing
   provisioning profiles, and `match` reuses a still-valid profile by default. Run `match` with
   `force: true` **once** after enabling the capability, or `build_app` fails: *"provisioning
   profile doesn't include the com.apple.developer.associated-domains entitlement."*

3. **App entitlement** — `mobile/wallet/ios/App/App/App.entitlements` with
   `com.apple.developer.associated-domains = [webcredentials:sorcha.dev]`. Wired at **build time**
   via `apply_match_signing` (`update_code_signing_settings entitlements_file_path: "App/App.entitlements"`)
   so the committed Capacitor project stays generic (mirrors how team/profile are applied). Android
   needs **no** app-side entitlement.

4. **TestFlight build number** — now handled automatically (#1052): both iOS lanes inject a
   monotonic **epoch-seconds** `CURRENT_PROJECT_VERSION` via `build_app xcargs` (see `ios_version_xcargs`
   in the Fastfile), so re-uploads never collide. No manual bump needed. (Historically the committed
   lane used a static build number that collided 409 on re-upload — see troubleshooting → TestFlight.)

5. **Capacitor secure origin** — the installed app MUST load from the real HTTPS origin
   (`capacitor.config.json` `server.url = https://n1.sorcha.dev/wallet`), **not** the bundled
   `capacitor://localhost`. On the local origin `crypto.subtle` is absent → the IndexedDB bridge
   throws on sign-in load → `AppErrorBoundary` "Something went wrong"; AND the WebAuthn origin won't
   match the RP. (Fix history: PR #1030 / the `fix/wallet-pwa-mobile-backend-url` work.)

6. **AASA + assetlinks served at the APEX** — `https://sorcha.dev/.well-known/apple-app-site-association`
   (iOS, `{"webcredentials":{"apps":["HY5HSW5FUT.app.sorcha.wallet"]}}`) and `/assetlinks.json`
   (Android, Digital Asset Links `delegate_permission/common.get_login_creds`, package
   `app.sorcha.wallet`, **both** the upload-key AND Play-App-Signing SHA-256 fingerprints). These
   live in the embedded marketing content (`Sorcha.UI.Web/wwwroot/.well-known/`, single source of
   truth) and ship to the apex via `copy-landing-to-site.js` → `docs/site` → the `gh-pages.yml`
   deploy. Three CI/hosting gotchas, all hit live:
   - **`actions/upload-pages-artifact` hardcodes `--exclude=".[^/]*"`** → strips the entire
     `.well-known` dot-folder (its `include-hidden-files` input does not exist; the value no-ops).
     Fix: archive manually with a plain `tar .` and upload via `actions/upload-artifact@v4` as the
     `github-pages` artifact (deploy-pages consumes it unchanged). (PR #1032.)
   - **Apple's AASA fetcher does not follow redirects** → the apex must be the GitHub Pages
     *primary* domain (serves 200), not redirecting to `www`. Flip `docs/site/CNAME` from
     `www.sorcha.dev` → `sorcha.dev` (www then 301s to apex). **No DNS change** — the apex already
     has the Pages A/AAAA records.
   - **A dangling `CNAME` file in a *different* repo claiming the apex** hangs the deploy. Removed a
     stale `CNAME=sorcha.dev` from `StuartF303/sorcha.dev` (legacy site, served at github.io) to
     free the apex, then `gh api -X PUT repos/Sorcha-Platform/Sorcha/pages -f cname=sorcha.dev`.

7. **Server RP ID** — n1 `docker-compose.n1.yml`: `Fido2__ServerDomain: sorcha.dev` (the RP ID =
   parent) and `Fido2__Origins__0: https://n1.sorcha.dev` (the ceremony origin stays the real host).
   Recreate tenant-service; verify with `POST /api/auth/passkey/assertion/options` → response
   `options.rpId == "sorcha.dev"`.

8. **On-device diagnostics** — TestFlight/Play release builds are **not** web-inspectable by default
   (iOS 16.4+ needs `WKWebView.isInspectable`; Android needs `setWebContentsDebuggingEnabled`). For
   visibility without a debug build, the PWA's `AppErrorBoundary` (`ShowDetails="true"` in the PWA
   `MainLayout`) renders a collapsible, copyable exception block — the device user can read/copy the
   real error. To use Safari Web Inspector / `chrome://inspect`, build a *debug/adhoc* build.

## Build sequence (the one-off, after a capability change)

A temporary `ios_beta_assoc` lane (appended to the Mac Fastfile, reverted after) did: `setup_ci` →
`web_build` → `match(appstore, force: true)` → `apply_match_signing` →
`increment_build_number(latest_testflight + 1)` → `build_app` → `upload_to_testflight`. After the
profile carries the capability, subsequent builds don't need `force`. Mac checkout reset with
`git reset --hard origin/master` afterwards.

## Account reset to re-register a passkey (RP change orphans old ones)

Passkey bound to old RP `n1.sorcha.dev` won't match the new RP `sorcha.dev`. Either add a new passkey
on the existing account, or delete the account for a clean re-signup. Full clean-slate delete on n1
(`sorcha_tenant`: `PlatformUsers` cascades passkey/device/membership/inbox + explicit
`UserIdentities`; `sorcha_wallet`: `Wallets` cascades addresses/access/txns/recovery + explicit
`Credentials`/`DerivedKeyRecords`/`CitizenHolderIndex`/`Citizen*` by `PlatformUserId`). All
`PlatformUsers` child FKs are `ON DELETE CASCADE`; `UserIdentities` is **not** a cascade child.
