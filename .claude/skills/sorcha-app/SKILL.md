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

### Lanes (`mobile/wallet/fastlane/Fastfile`)
- **`android_adhoc`** — signed release APK. **Works today; no account needed.**
- `android_internal` — AAB → Play Internal. Blocked: needs Play account + service-account JSON.
- `ios_adhoc` / `ios_beta` — toolchain ready (Xcode 26.5). Gate now is Apple Developer Program + a `match` repo (+ ASC API key for TestFlight) + iOS platform `cap add ios`.

Every lane begins with the shared web step `mobile/scripts/build-web.sh`: `dotnet publish` (the Blazor
WASM "web build") → `www` → rewrite `<base href>` to `/` → strip precompressed dupes → `cap sync`.

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
  clang lacked the C23 `<stdckdint.h>` ruby needs. iOS lanes now gate only on **Apple Developer Program
  enrolment + a `match` repo** (signing), not the toolchain.

## Accounts (long-pole prerequisites)
- **Apple**: Developer Program ($99/yr; org needs a D-U-N-S number — the slow step); ASC API key (`.p8` +
  key id + issuer id) for CI; registered bundle id `app.sorcha.wallet`; device UDIDs for ad-hoc; a `match` repo.
- **Google Play**: $25 once; org account also needs D-U-N-S now; service-account JSON for `supply`; **the
  first AAB must be uploaded manually** before the API accepts automated uploads.

## Optional separate workstream (ASK FIRST)
A NAT'd Docker backend node can run on the Mac so the apps have a real backend during dev/ad-hoc builds.
Before touching it, ask Stuart: what the node should be (dev API? full peer/validator? demo register?),
which ports to forward, and LAN-only vs publicly tunnelled. Don't assume.
