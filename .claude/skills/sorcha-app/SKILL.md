---
name: sorcha-app
description: Build, sign, and ship the Sorcha mobile apps (Capacitor + fastlane on a Mac build node, driven from the Windows orchestrator). Use when triggering or debugging iOS/Android builds, running a fastlane lane, dispatching the build workflow, watching a build, registering or fixing the self-hosted runner, deriving app versions, onboarding the Mac toolchain, mapping where signing secrets live, or configuring native passkeys / Associated Domains (AASA, assetlinks, App-ID capability, relying-party / Fido2 ServerDomain, secure-origin server.url). Covers the Sorcha Wallet PWA (app.sorcha.wallet).
---

# sorcha-app — mobile build/deploy orchestration

You orchestrate; you are not in the critical path of a build. The build engine (Capacitor +
fastlane, in the repo at `mobile/`) runs headless on the Mac and is identical whether triggered
by a human, a git tag, or you. If a build only works because you drove it interactively, it's wrong.

## Topology (the one-screen model)

| Role | Host | What it does |
|------|------|--------------|
| **Orchestrator** | Windows dev box (here) | SSH to the Mac, trigger lanes, dispatch the workflow, watch/parse builds. **No build toolchain.** |
| **Build node** | Mac mini M1, `ssh stuart@macmini` (192.168.51.4) | Xcode + JDK 21 + Android SDK + Capacitor + fastlane. **All signing secrets live here and nowhere else.** Hosts the GitHub Actions self-hosted runner. |
| **Unattended trigger** | GitHub Actions | `.github/workflows/build.yml` on a `[self-hosted, sorcha-build]` runner; fires on tag `v*`, release, or `workflow_dispatch`. |

App: **`src/Apps/Sorcha.Wallet.Pwa`** — a Blazor **WASM** PWA (NOT a JS app), wrapped by Capacitor 8
in `mobile/wallet/`. Bundle id **`app.sorcha.wallet`**. The Verifier app is server-hosted → not in scope.

Full detail: `references/topology.md`. Secret locations: `references/secrets-map.md`. Fixes: `references/troubleshooting.md`. Passkeys / Associated Domains (PROVEN iOS recipe — RP model, App-ID capability + match force, AASA/assetlinks via the apex CI pipeline + the upload-pages-artifact dotfile-strip gotcha, Fido2 ServerDomain, secure-origin server.url, account reset): `references/passkeys.md`.

## Session preflight (do this FIRST, every mobile session)
The build node is a **separate machine** — nothing builds without it. Before driving any lane, confirm
the orchestrator can reach the Mac over SSH (PowerShell tool, never Git Bash):

```powershell
ssh stuart@macmini 'echo OK; hostname; cd ~/projects/Sorcha && git rev-parse --short HEAD'
```

- Expect `OK` + `macmini.local` + a commit. If it hangs or `Permission denied`/`Too many auth failures`:
  the orchestrator box's SSH **public key isn't in the Mac's `~/.ssh/authorized_keys`**, the Mac is off/asleep,
  or you used Git Bash (use the **PowerShell tool**). A **new/different orchestrator box must have its key
  added to the Mac first** — without it, you cannot build. Resolve access before anything else.
- `~/projects/Sorcha` is the **dedicated lane-running checkout** (distinct from the runner's `~/actions-runner/_work/...`).
  Keep it clean on `master`; lanes that mutate it (pbxproj signing edits) are reset with `git reset --hard origin/master`.

## Iron rules

1. **SSH only via Windows OpenSSH (the PowerShell tool), never the Bash tool / Git Bash** — Git Bash
   has no agent socket and every key auth fails ("Too many authentication failures"). All scripts here are PowerShell.
2. **Never copy signing material to Windows or into the repo/skill.** Keystores, certs, profiles,
   `.p8`/`.p12`, `keystore.properties` stay on the Mac. The skill records *locations only*.
3. **Don't trust an outer ssh wrapper's exit code** — have the remote script append its own `EXIT=$?`
   and read that. (`ssh host 'cmd; echo EX=$?'` — single-quote in PowerShell so `$?` reaches the remote.)
4. **Versioning is automatic**: `versionName` from the git tag (`v1.2.3`→`1.2.3`), `versionCode` from the
   Actions run number. Never bump versions by hand.
5. **Production store lanes are out of scope.** First targets only: TestFlight, Play Internal, ad-hoc.

## Intent → lane → script

| You want to… | Do this |
|--------------|---------|
| On-call build right now (debug, bypass CI) | `scripts/trigger-build.ps1 -Lane android_adhoc` (SSH-runs fastlane on the Mac) |
| Fire the unattended workflow on demand (**Android only**) | `scripts/dispatch-workflow.ps1 -Lane android_adhoc` (needs build.yml on master; else push a `v*` tag). **Do NOT use for iOS** — the runner has no keychain session (see gotchas); run iOS lanes via the SSH/`nohup` path below. |
| Watch a build + get the failure reason | `scripts/watch-build.ps1 [-RunId <id>]` |
| Mirror CI's version for a local build | `scripts/bump-version.ps1 -BuildNumber <n>` |
| Stand up / repair the Mac toolchain | `mobile/scripts/bootstrap-mac.sh` (idempotent; in the repo) |
| (Re)register the runner | `mobile/scripts/setup-runner.sh` (defaults `SCOPE=repo` — see below) + `sudo mobile/scripts/install-runner-daemon.sh` |

### Lanes (`mobile/wallet/fastlane/Fastfile`) — **ALL FOUR PROVEN END-TO-END (2026-06-23)** on real accounts
- **`android_adhoc`** — signed release APK. No account needed.
- **`android_internal`** — AAB → Play Internal via `supply`. Needs `~/.sorcha-signing/play_service_account.json`
  + the SA granted **account-level Admin** in Play Console (a narrower track grant kept failing). Bump
  `SORCHA_VERSION_CODE` above the last used (vc1 manual, vc2 automated).
- **`ios_adhoc`** — signed ad-hoc IPA. **`ios_beta`** — IPA → TestFlight. Both use `match` (repo
  `Sorcha-Platform/ios-certs`) + the ASC API key; the lanes call `setup_ci` (headless keychain) and
  `apply_match_signing` (Capacitor ships automatic signing/no team → must force manual). iOS env
  (`MATCH_GIT_URL`/`MATCH_PASSWORD`) lives in `~/.sorcha-signing/ios-match.env` (NOT `~/.zprofile`), so
  iOS lanes must `source` it — `trigger-build.ps1` does NOT, so run iOS inline with the env sourced.

Every lane begins with the shared web step `mobile/scripts/build-web.sh`: `dotnet publish` (the Blazor
WASM "web build") → `www` → rewrite `<base href>` to `/` → strip precompressed dupes → `cap sync`.

**Driving a lane headless (the proven pattern):** write a small script on the Mac that `source`s
`~/.zprofile` (+ `~/.sorcha-signing/ios-match.env` for iOS), `cd`s to `mobile/wallet`, `bundle install`,
then `bundle exec fastlane <lane>`; launch it with `nohup … > ~/<lane>.log 2>&1 &` and poll the log
(builds outlast a single SSH call). Remove the scratch script + log when done.

## Decision rules / gotchas (learned the hard way)

- **Runner must be registered at the REPO level, not org level.** An org runner (Default group,
  `visibility:all`, online, labels matching) is silently *not* routed repo jobs — the job hangs on
  "Waiting for a runner…". Repo-level registration dispatches in seconds. `setup-runner.sh` defaults to `SCOPE=repo`.
- **Match `runs-on` on the unique `sorcha-build` label**, not the OS label `macOS`/`macos` (case ambiguity).
- **Runner persistence = system LaunchDaemon** (`install-runner-daemon.sh`, needs sudo once). Survives
  reboot with no GUI login. (svc.sh's LaunchAgent can't bootstrap over SSH.)
- **iOS lanes will NOT run on the GH Actions runner — only Android.** The runner is a system
  LaunchDaemon with **no user keychain/Security session**, so `setup_ci`'s `security create-keychain`
  fails and every `dispatch-workflow.ps1 -Lane ios_*` dies at `setup_ci`→`setup_keychain`
  (setup_ci.rb:42) in ~15s. Tell-tale in the log: `ORIGINAL_DEFAULT_KEYCHAIN = "…/System.keychain"`
  (a real user session shows `login.keychain`). **Android signs from the keystore *file*, never the
  macOS keychain, so it builds fine on the runner.** Don't chase red herrings — it is NOT the app code,
  a reboot, a stale temp keychain (`security delete-keychain` doesn't help), or the user's default-keychain
  pref (restoring it doesn't propagate to the daemon session). **Run iOS via the SSH/`nohup` user-session
  pattern above** (a real `stuart` login session *can* create keychains → `setup_ci` 0s, TestFlight upload OK).
  (Durable fix candidate: host the runner as a per-user GUI-session LaunchAgent so it has a keychain —
  blocked by the same can't-bootstrap-over-SSH issue; the SSH path is the pragmatic answer.)
- **A run-script authored on Windows ships CRLF.** Piped to the Mac it yields `source ~/.zprofile^M:
  no such file or directory` and a `parse error near '\n'`. Strip carriage returns on upload:
  `Get-Content script.sh | ssh stuart@macmini "tr -d '\r' > ~/script.sh"`.
- **CI steps run `bash + source ~/.zprofile`** (not a login shell), so `.zprofile` must export `dotnet`,
  `JAVA_HOME` (JDK 21), `ANDROID_HOME`, ruby/gem bin — bootstrap handles this.
- **Capacitor 8 needs JDK 21** (docs say "17+", but its Android lib compiles at Java 21) and **SDK 36**.
- **Toolchain unblocked (Xcode 26.5 / clang 21, 2026-06-18):** native gems compile, `Gemfile.lock` is
  committed, lanes run via `bundle exec fastlane` (deterministic). Earlier Xcode 15.4 blocked this — its
  clang lacked the C23 `<stdckdint.h>` ruby needs.
- **Little Snitch blocks Homebrew Ruby → Apple ASC API** (the session-eating one): `match` hangs at
  "Creating authorization token for App Store Connect API" with the socket in `CLOSE_WAIT`, while `curl`
  to the same host works. The Mac's per-app outbound firewall denies the unsigned Ruby; headless there's
  no GUI prompt. Fix = LS allow rule `*.apple.com`:443 Any Process.
- **iOS signing on a headless Mac**: `match` needs `setup_ci` (login.keychain is locked over SSH/daemon)
  and the Capacitor project must be forced to **manual** signing (`apply_match_signing`) or archive fails
  "requires a development team". Both are already in the Fastfile.
- **Play upload "caller does not have permission"**: grant the SA **account-level Admin** in Play Console
  → Users and permissions (a narrower "Release to testing tracks" grant kept failing). Setup→API access
  is now folded into Users and permissions.
- **TestFlight**: internal testing needs **no redeem code** (that's external); testers must be team users.
  Export compliance is baked into `Info.plist` (`ITSAppUsesNonExemptEncryption=false`).

## Accounts — **ACTIVE (Individual/Personal) since 2026-06-23**
Both enrolled; all creds placed on the Mac under `~/.sorcha-signing` (see `references/secrets-map.md`).
- **Apple**: Developer Program live (team **HY5HSW5FUT**); ASC API key `asc_api_key.p8`+`asc_api_key.json`
  (key MBZVZTN4VX); App ID `app.sorcha.wallet` + a device UDID registered; `match` repo `Sorcha-Platform/ios-certs`;
  ASC app id `6783321595`. **Little Snitch on the Mac silently blocked Homebrew Ruby → Apple's ASC API**
  (match hung at "Creating authorization token") — fixed with an LS allow rule for `*.apple.com`; if the
  Ruby/toolchain is reinstalled or LS rules reset, this can recur (see troubleshooting).
- **Google Play**: live; `play_service_account.json` placed; SA `sorcha-play-ci@sorcha-494515…` granted
  **account-level Admin**. The first AAB (vc1) was uploaded manually; `supply` automates from there.
- Setup runbook (what to do if re-enrolling / a fresh account): `references/finishing-accounts.md`.

**Production store lanes** (App Store / Play production) remain out of scope and need extra work:
a **publicly reachable backend** the build points at + a **demo login** for App Review, plus store
listing/screenshots. Internal/TestFlight (current targets) need none of that.

## Native plugins (Feature 185 — the first one)

`mobile/plugins/sorcha-proximity/` is the repo's **first native code**. Until F185, `mobile/wallet` had
**zero** Capacitor plugins and its only hand-edited native file was `App.entitlements`.

**The rule that keeps it small:** the plugin moves **opaque bytes** and knows nothing else — no CBOR, no mdoc,
no credentials, no session state. Every byte of ISO 18013-5 protocol lives in C# (`Sorcha.Mdoc`), written once
and shared by holder and reader. That is what lets `LoopbackProximityTransport` stand in for the radio so the
whole exchange is proven in CI **with no phones**. Protocol logic in the plugin would be logic the C# suite
cannot reach — written twice, in two languages, with two chances to get the tag-24 rules wrong, and those fail
*silently*. **If you want to parse something in a plugin, you are in the wrong file.**

### Building / verifying a plugin on the Mac node

```bash
# iOS — SPM. NOTE: plain `swift build` targets macOS and fails with "no such module 'Capacitor'";
# Capacitor is iOS-only, so you MUST give xcodebuild an iOS destination.
cd mobile/plugins/sorcha-proximity
xcodebuild -scheme SorchaProximity -destination "generic/platform=iOS" build
xcodebuild test -scheme SorchaProximity -destination "platform=iOS Simulator,name=iPhone 17 Pro"

# Android — the plugin is a gradle subproject of the APP, not standalone.
cd mobile/wallet && npm install && npx cap sync android      # registers the plugin
cd android && ./gradlew :sorcha-proximity:testDebugUnitTest  # unit tests
./gradlew assembleDebug                                      # app builds with the plugin
```

**Gotchas that cost time:**
- **`npm install` SYMLINKS a `file:` dependency.** Gradle therefore compiles the plugin from
  `mobile/plugins/…`, and **build output lands there** — not under `mobile/wallet/android/`. Looking for test
  results in the app tree finds nothing and looks like "0 tests ran".
- **The simulator name must exist.** `iPhone 16` is not installed on this Mac; `xcrun simctl list devices
  available` is the source of truth (currently iPhone 17 Pro / 17 Pro Max / 17e).
- **`xcodebuild -list` names the scheme after the library PRODUCT** (`SorchaProximity`), not the target
  (`SorchaProximityPlugin`).
- The plugin's `package.json` `files` whitelist governs what a *published* package ships. It does not affect a
  symlinked `file:` dep, but omitting `android/src/test/` would hide the tests from a real consumer.

### Permissions the proximity plugin needs

- **iOS:** `NSBluetoothAlwaysUsageDescription` in `Info.plist` (the app is killed on first BLE touch without
  it). The peripheral role is restricted in the **background** — irrelevant here, a presentation is always
  foreground.
- **Android:** `BLUETOOTH_ADVERTISE` / `BLUETOOTH_CONNECT` / `BLUETOOTH_SCAN`, the last with
  **`neverForLocation`** — without that flag Android demands `ACCESS_FINE_LOCATION`, i.e. we would be asking a
  citizen for their location in order to show a credential to the person standing in front of them.

Full detail: `mobile/plugins/sorcha-proximity/README.md`.

## Optional separate workstream (ASK FIRST)
A NAT'd Docker backend node can run on the Mac so the apps have a real backend during dev/ad-hoc builds.
Before touching it, ask Stuart: what the node should be (dev API? full peer/validator? demo register?),
which ports to forward, and LAN-only vs publicly tunnelled. Don't assume.
