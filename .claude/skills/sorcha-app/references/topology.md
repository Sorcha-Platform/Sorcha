# Topology — Sorcha mobile build/deploy

## Machines

- **Windows dev box (orchestrator)** — where you (Claude) run. No mobile toolchain. Drives the Mac
  over **Windows OpenSSH** (PowerShell tool). Holds no secrets.
- **Mac mini M1 `macmini` (192.168.51.4), macOS 26.x** — the single build node for both platforms.
  `ssh stuart@macmini`. Repo clone at `~/projects/Sorcha`. Toolchain installed by `bootstrap-mac.sh`:
  - Homebrew, **node** (npm), **openjdk@21** (Capacitor 8's Android compile level), **Android SDK**
    (`~/Library/Android/sdk`, platform `android-36` + build-tools `36.0.0`), **ruby** (Homebrew) +
    **fastlane** + **cocoapods**, **gh**.
  - Env wired into `~/.zprofile` (replace-on-run block): `JAVA_HOME`, `ANDROID_HOME`, ruby+gem bin,
    `/usr/local/share/dotnet`, `LANG=UTF-8`.
  - **Xcode** managed by hand (NOT by bootstrap). Currently 15.4 → must reach 17.x for iOS/store SDK.

## App + wrapper

- App: `src/Apps/Sorcha.Wallet.Pwa` — **Blazor WebAssembly PWA**. The "web build" is `dotnet publish`
  (not npm). Scope `/wallet/`; Capacitor serves `www/` at root so `<base href>` is rewritten to `/`.
- Wrapper: `mobile/wallet/` — Capacitor 8.4, `appId app.sorcha.wallet`, committed `android/` native
  project (so CI needs no `cap add`), `fastlane/` lanes, `package-lock.json` (committed), `www/` + `node_modules/` (gitignored).
- minSdk 24, compileSdk/targetSdk 36 (`android/variables.gradle`). targetSdk 36 ≥ Play floor (35). v2-only APK signing is correct for minSdk ≥ 24.

## Repo layout (committed)

```
mobile/
  .gitignore                         # ignores www/, node_modules/, android build outputs, *.jks/*.p8/...
  scripts/                           # Mac-side (bash), run on the build node
    bootstrap-mac.sh                 # idempotent toolchain bring-up
    build-web.sh                     # shared web step (self-locates repo; works under CI _work)
    configure-android-signing.sh     # appends signing + version-override block to app/build.gradle
    make-upload-keystore.sh          # one-time keystore gen (refuses to overwrite)
    setup-runner.sh                  # register self-hosted runner (SCOPE=repo by default)
    install-runner-daemon.sh         # sudo: host runner as a reboot-surviving LaunchDaemon
  wallet/                            # Capacitor wrapper (package.json, capacitor.config.json, Gemfile, fastlane/, android/)
.github/workflows/build.yml          # [self-hosted, sorcha-build]; tag v* / release / workflow_dispatch
.claude/skills/sorcha-app/           # THIS skill (orchestrator-side PowerShell scripts + docs)
```

## Build flow (identical for human / tag / you)

```
git tag v1.2.3            (or workflow_dispatch, or trigger-build.ps1 on-call)
   └─> Mac runner picks up build.yml
         1. actions/checkout
         2. resolve lane + version (tag->versionName, run#->versionCode)
         3. npm ci            (Capacitor CLI deps; needs package-lock.json)
         4. fastlane <lane>:
              build-web.sh:  dotnet publish -> www -> base href / -> strip .br/.gz -> cap sync
              gradle assembleRelease  (signed via ~/.sorcha-signing/keystore.properties)
         5. upload-artifact: app-release.apk
```

## Runner

- GitHub Actions self-hosted runner at `~/actions-runner`, **registered to the repo**
  `Sorcha-Platform/Sorcha` (not the org — org-level didn't route jobs). Labels: `self-hosted`, `sorcha-build`.
- Hosted as system LaunchDaemon `com.sorcha.actions-runner` (runs `run.sh` as `stuart`; `RunAtLoad`+`KeepAlive`).
- Org for the repo: `Sorcha-Platform`. gh on the Mac authed as `StuartF303` (scopes incl. `admin:org`, `repo`, `workflow`), `gh auth setup-git` done for HTTPS push.
