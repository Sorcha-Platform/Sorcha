# Secrets map — LOCATIONS ONLY (never values, never copied off the Mac)

Hard rule: every secret below lives **only on the Mac build node**. Never copy any of it to the
Windows box, the repo, this skill, or GitHub repo secrets. The self-hosted runner reads them at
build time on the Mac, so GitHub only ever sees workflow YAML and logs.

## Android (exists today)

| Secret | Location (Mac) | Notes |
|--------|----------------|-------|
| Upload keystore | `~/.sorcha-signing/sorcha-wallet-upload.jks` | RSA-2048, alias `sorcha-wallet-upload`, validity ~27y. Mode 600. |
| Keystore credentials | `~/.sorcha-signing/keystore.properties` | `storeFile/storePassword/keyAlias/keyPassword`. Mode 600. Read by `app/build.gradle` (the committed signing block references this path; no secret in gradle). |
| Password backup | Stuart's password manager | The store/key password + the keystore SHA-256. Generated once by `make-upload-keystore.sh`. Under Play App Signing the upload key is resettable. |
| Play service-account JSON | `~/.sorcha-signing/play_service_account.json` | For `fastlane supply` (android_internal). Create once Play account exists. |

## iOS (ACTIVE — Apple Developer Program live, team `HY5HSW5FUT`)

| Secret | Location (Mac) | Notes |
|--------|----------------|-------|
| App Store Connect API key | `~/.sorcha-signing/asc_api_key.p8` + `asc_api_key.json` (`{key_id,issuer_id}`) | Avoids 2FA in CI (build_app/pilot/match). Read by the `asc_api_key` helper in the Fastfile. |
| Certs + provisioning profiles | `fastlane match` repo `Sorcha-Platform/ios-certs` (`MATCH_GIT_URL` in `~/.sorcha-signing/ios-match.env`), encrypted with `MATCH_PASSWORD` | **Stored on the `master` branch** — the default branch (`main`) holds only a README. ⚠️ Do NOT conclude "certs missing" by browsing the default branch / `gh api …/HEAD`; check `master`. Contents: `Y8B6P8VGRB.cer` + `.p12` (Apple Distribution), `AppStore_app.sorcha.wallet.mobileprovision`, `AdHoc_app.sorcha.wallet.mobileprovision`. Decrypted into the temp keychain at build time → identity `Apple Distribution: STUART JOHNSTON FRASER (HY5HSW5FUT)`. |
| Device UDIDs | Apple dev portal (Devices) → match adhoc profile | For the `ios_adhoc` profile. |

> **iOS build number = epoch, automatic.** `ios_version_xcargs` in the Fastfile injects `CURRENT_PROJECT_VERSION=Time.now.utc.to_i` at archive time → CFBundleVersion is a monotonic epoch that always exceeds the prior TestFlight upload. **Never bump it by hand.** A TestFlight `-19232 "bundle version must be higher than … <N>"` error means the Mac lane-runner checkout (`~/projects/Sorcha`) is **stale** (predates the epoch fix) — `git fetch origin master && git reset --hard origin/master` there before running, don't touch the version.

## Service / infra credentials (Mac)

| Item | Location | Notes |
|------|----------|-------|
| GitHub auth (gh) | `~/.config/gh/hosts.yml` | Account `StuartF303`; scopes `admin:org`, `repo`, `workflow`. `gh auth setup-git` configures git HTTPS push. |
| Runner credentials | `~/actions-runner/.credentials*`, `.runner` | Created by `setup-runner.sh`; repo-scoped registration. |

## Quick checks (safe — no values printed)
- Keystore fingerprint: `keytool -list -v -keystore ~/.sorcha-signing/sorcha-wallet-upload.jks -alias sorcha-wallet-upload -storepass "$(grep '^storePassword=' ~/.sorcha-signing/keystore.properties | cut -d= -f2-)" | grep SHA256`
- Verify an APK was signed with it: `$ANDROID_HOME/build-tools/36.0.0/apksigner verify --print-certs <apk> | grep SHA-256`
