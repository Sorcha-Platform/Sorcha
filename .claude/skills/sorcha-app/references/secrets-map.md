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
| Play service-account JSON | `~/.sorcha-signing/` (TBD) | For `fastlane supply` (android_internal). **Not yet created** — needs Play account. |

## iOS (not yet — needs Apple Developer Program + Xcode 17)

| Secret | Location (Mac) | Notes |
|--------|----------------|-------|
| App Store Connect API key | `~/.sorcha-signing/` (TBD `.p8`) | + Key ID + Issuer ID. Avoids 2FA in CI (pilot/match). |
| Certs + provisioning profiles | `fastlane match` private git repo | Managed by `match`; decrypted into the Mac keychain at build time. **Repo not yet created.** |
| Device UDIDs | match / ASC | For the ad-hoc profile. |

## Service / infra credentials (Mac)

| Item | Location | Notes |
|------|----------|-------|
| GitHub auth (gh) | `~/.config/gh/hosts.yml` | Account `StuartF303`; scopes `admin:org`, `repo`, `workflow`. `gh auth setup-git` configures git HTTPS push. |
| Runner credentials | `~/actions-runner/.credentials*`, `.runner` | Created by `setup-runner.sh`; repo-scoped registration. |

## Quick checks (safe — no values printed)
- Keystore fingerprint: `keytool -list -v -keystore ~/.sorcha-signing/sorcha-wallet-upload.jks -alias sorcha-wallet-upload -storepass "$(grep '^storePassword=' ~/.sorcha-signing/keystore.properties | cut -d= -f2-)" | grep SHA256`
- Verify an APK was signed with it: `$ANDROID_HOME/build-tools/36.0.0/apksigner verify --print-certs <apk> | grep SHA-256`
