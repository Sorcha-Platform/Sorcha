# Finishing the store lanes once the developer accounts exist

Everything is prestaged. When the accounts are active, the only work is: register a couple of
identifiers, create the credentials, **place them on the Mac under `~/.sorcha-signing`**, and run
the lanes. The lanes already exist (`mobile/wallet/fastlane/Fastfile`) and fail loud with the exact
missing-file path until the credentials are present.

Secrets live ONLY on the Mac. Never copy them to Windows or the repo.

## STATUS (2026-06-23) — PIPELINE COMPLETE, ALL LANES PROVEN END-TO-END
**All four lanes proven on real accounts:** `android_adhoc` (CI), `android_internal` (Play Internal —
versionCode 2 uploaded via `supply`), `ios_adhoc` (signed IPA), `ios_beta` (TestFlight). The Play SA
needed **account-level Admin** (Users and permissions → SA → Account permissions → Admin); a narrower
track grant kept failing. Android testing live via vc1 (manual) + vc2 (automated); iOS via TestFlight Internal.

- **iOS `ios_adhoc` + `ios_beta` PROVEN end-to-end** — signed ad-hoc IPA **and** TestFlight upload both
  work (App Store Connect app id `6783321595`, team **HY5HSW5FUT**, bundle `app.sorcha.wallet`, profiles
  `match AdHoc/AppStore app.sorcha.wallet`, match repo `Sorcha-Platform/ios-certs`). App installed/testing
  on Stuart's iPhone via TestFlight Internal. Fixes (PR #1024, in Fastfile): `setup_ci` (headless
  keychain) + `apply_match_signing` (manual signing). Export-compliance baked into `Info.plist`
  (`ITSAppUsesNonExemptEncryption=false`, PR #1025) so TestFlight stops prompting. Infra fix: a **Little
  Snitch** allow rule for `*.apple.com` (was blocking Homebrew Ruby → ASC API; see troubleshooting).
  Creds in `~/.sorcha-signing`: `asc_api_key.p8` + `asc_api_key.json` (key MBZVZTN4VX, issuer 3c58937d-…)
  + `ios-match.env` (MATCH_GIT_URL + MATCH_PASSWORD; deliberately NOT in `~/.zprofile`, so iOS lanes must
  `source` it — `trigger-build.ps1` does NOT, so iOS via that script needs the source added or run inline).
  ⚠ json key name is `issuer_id` not `issure_id` (typo silently breaks ASC auth).
- **Android** — manual AAB (versionCode 1) on Play Internal works for testing NOW. `android_internal`
  lane builds+signs the AAB fine but the **automated upload is still blocked on the Play permission grant**:
  service account `sorcha-play-ci@sorcha-494515.iam.gserviceaccount.com` (JSON at
  `~/.sorcha-signing/play_service_account.json`) needs **"Release to testing tracks"** on the Sorcha
  Wallet app in Play Console → Users and permissions. Once granted, re-run with `SORCHA_VERSION_CODE=2`
  (vc1 is taken). Note in `deploy/mobile-artifacts/PLAY-SERVICE-ACCOUNT-NOTE.md`.

## iOS (ios_adhoc / ios_beta)

Prereq: Apple Developer Program active (Individual is fine).

1. **Register the App ID** `app.sorcha.wallet` — https://developer.apple.com/account/resources/identifiers/list → +.
2. **Register device UDIDs** (for ad-hoc installs) — https://developer.apple.com/account/resources/devices/list → +.
3. **Create an App Store Connect API key** — https://appstoreconnect.apple.com/access/integrations/api → + (Access: App Manager).
   Download the **`.p8` (once)**; note the **Key ID** and **Issuer ID**.
4. **Create a private match repo**, e.g. `Sorcha-Platform/ios-certs` (empty private repo).
5. **Place on the Mac:**
   ```bash
   mkdir -p ~/.sorcha-signing
   mv ~/Downloads/AuthKey_XXXXXX.p8 ~/.sorcha-signing/asc_api_key.p8
   printf '{"key_id":"XXXXXXXXXX","issuer_id":"xxxxxxxx-xxxx-xxxx-xxxx-xxxxxxxxxxxx"}\n' > ~/.sorcha-signing/asc_api_key.json
   chmod 600 ~/.sorcha-signing/asc_api_key.*
   ```
   And export the match repo + a chosen passphrase (add to ~/.zprofile so CI/login shells see them; passphrase also to your password manager):
   ```bash
   export MATCH_GIT_URL="git@github.com:Sorcha-Platform/ios-certs.git"   # or HTTPS
   export MATCH_PASSWORD="<choose a strong passphrase>"
   ```
6. **Run** (I drive these): first `bundle exec fastlane match adhoc` (creates+stores certs), then
   `ios_adhoc`; later `ios_beta` for TestFlight. Orchestrator: `trigger-build.ps1 -Lane ios_adhoc`.
   - Note: first real run may need a small signing-settings tweak in the Xcode project (manual
     signing + profile mapping) — expected; I'll finalize it then.

## Android internal (android_internal)

Prereq: Play Console account active + app record created.

1. **Create the app** in Play Console → "Sorcha Wallet", and set up the **Internal testing** track + testers.
2. **Enrol Play App Signing** (default); register the **upload key SHA-256** (in your password manager,
   from `make-upload-keystore.sh`) when prompted.
3. **Service account for CI:** Google Cloud Console → enable the Play Android Developer API
   (https://console.cloud.google.com/apis/library/androidpublisher.googleapis.com) → create a service
   account + JSON key (https://console.cloud.google.com/iam-admin/serviceaccounts). In Play Console →
   Users and permissions → invite the service-account email → grant "Release to testing tracks".
4. **Place on the Mac:**
   ```bash
   mv ~/Downloads/<service-account>.json ~/.sorcha-signing/play_service_account.json
   chmod 600 ~/.sorcha-signing/play_service_account.json
   ```
5. **⚠️ Manual first upload:** build one AAB and upload it **by hand** in the Play UI once — the API
   refuses automated uploads until a first manual AAB exists. Build it with:
   `trigger-build.ps1 -Lane android_internal` will fail at the API upload the first time; instead use
   the AAB it produced at `mobile/wallet/android/app/build/outputs/bundle/release/app-release.aab`
   (or run gradle `bundle Release` directly) and upload that in the console. After that, `android_internal` automates.

## Hand-off phrase
Say **"I have the accounts"** and tell me which (Apple / Google / both) are active. I'll: confirm the
credential files are in place (or tell you exactly what's missing), run `match`, then the lanes, fix
any first-run signing wiring, and validate `mobile-pipeline` prestage PR before merging.
