---
name: sorcha-app
description: Build, sign, and ship the Sorcha mobile apps (Capacitor + fastlane on a Mac build node, driven from the Windows orchestrator). Use when triggering or debugging iOS/Android builds, running a fastlane lane, dispatching the build workflow, watching a build, registering or fixing the self-hosted runner, deriving app versions, onboarding the Mac toolchain, or mapping where signing secrets live. Covers the Sorcha Wallet PWA (app.sorcha.wallet).
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

Full detail: `references/topology.md`. Secret locations: `references/secrets-map.md`. Fixes: `references/troubleshooting.md`.

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
| Fire the unattended workflow on demand | `scripts/dispatch-workflow.ps1 -Lane android_adhoc` (needs build.yml on master; else push a `v*` tag) |
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

## Optional separate workstream (ASK FIRST)
A NAT'd Docker backend node can run on the Mac so the apps have a real backend during dev/ad-hoc builds.
Before touching it, ask Stuart: what the node should be (dev API? full peer/validator? demo register?),
which ports to forward, and LAN-only vs publicly tunnelled. Don't assume.
