# Troubleshooting — Sorcha mobile build

Hard-won fixes. Symptom → cause → fix.

## SSH / orchestration

**`Too many authentication failures` / `Permission denied` when SSHing to the Mac**
→ You used the **Bash tool (Git Bash)**, which has no agent socket and offers every key.
→ Use the **PowerShell tool** (Windows OpenSSH + Windows agent). All scripts here are PowerShell.

**A remote command "succeeded" (exit 0) but actually failed**
→ The outer `ssh host 'script; echo ...'` returns the *last* command's status, not the script's.
→ Make the script append `EXIT=$?` to its log and read that. In PowerShell, **single-quote** the
   ssh command so `$?`/`$VAR` reach the remote shell unexpanded.

**zsh errors `bad pattern: [...]` or `== not found` in `zsh -lc "..."`**
→ Unquoted `[` / leading `=` in `echo` trigger zsh globbing/`=`-expansion.
→ Avoid bracketed/`=`-leading echo labels, or run the real command without the label.

## Self-hosted runner

**Job stuck on "Waiting for a runner to pick up this job…" though the runner is online/idle/labelled**
→ **Org-level** registration silently doesn't route repo jobs (even with Default group `visibility:all`).
   Listener log shows zero job offers; broker session is healthy.
→ **Re-register at the REPO level.** `setup-runner.sh` defaults `SCOPE=repo`. To migrate:
   `./config.sh remove --token $(gh api -X POST /orgs/<org>/actions/runners/remove-token --jq .token)`
   then re-run `setup-runner.sh`. Picks up jobs in seconds.

**"No runner matched the labels"**
→ `runs-on` label case/spelling. Match the unique **`sorcha-build`** label (+ implicit `self-hosted`),
   not `macos`/`macOS`.

**`svc.sh install` → `Load failed: 5: Input/output error` over SSH**
→ svc.sh installs a *LaunchAgent* needing a GUI login session; can't bootstrap over SSH.
→ Use the system **LaunchDaemon**: `sudo install-runner-daemon.sh` (survives reboot, no login).

**Runner shows online but a fresh session still won't take jobs**
→ Restart cleanly: `sudo launchctl kickstart -k system/com.sorcha.actions-runner`. If still stuck,
   it's the org-vs-repo routing above, not the daemon.

## Build

**`error: invalid source release: 21` in `compileReleaseJavaWithJavac`**
→ Running JDK < 21; Capacitor 8's Android lib compiles at Java 21.
→ `bootstrap-mac.sh` installs `openjdk@21` and points `JAVA_HOME` at it. Confirm `java -version` = 21.

**`dotnet: command not found` in a CI step (but works in your interactive shell)**
→ CI steps run `bash + source ~/.zprofile` (not a login shell), so macOS `path_helper` (which adds
   `/usr/local/share/dotnet`) never runs.
→ `.zprofile` block (from bootstrap) exports dotnet explicitly. Re-run `bootstrap-mac.sh` if missing.

**`bundle install` fails building native gem (json): `'stdckdint.h' file not found`**
→ Active clang (Xcode 15.4) predates the C23 `<stdckdint.h>` that Ruby 4.x headers need → no native
   gem compiles.
→ Install/select **Xcode 17+** (`sudo xcode-select -s /Applications/Xcode.app`). Until then, run
   fastlane via the global precompiled gems (`BUNDLE_GEMFILE=`), and `Gemfile.lock` determinism + all
   iOS lanes stay blocked. (Same root cause blocks iOS.) The `bootstrap-mac.sh` preflight warns about this.

**Blazor app loads blank / 404s assets in the webview**
→ `<base href="/wallet/">` not rewritten. `build-web.sh` rewrites it to `/`; confirm `www/index.html`.

**`sdkmanager` "fails" but everything actually installed**
→ `set -o pipefail` + `yes | sdkmanager` — `yes` dies SIGPIPE (141), tripping the pipe. `bootstrap-mac.sh`
   reads `${PIPESTATUS[1]}` (sdkmanager's own code) instead. Not a real failure.

**APK built but is it signed with the right key?**
→ `$ANDROID_HOME/build-tools/36.0.0/apksigner verify --print-certs <apk> | grep SHA-256` and compare
   to the keystore SHA-256 (in the password manager / `make-upload-keystore.sh` output).

## Disk
**Mac data volume low (~tens of GiB)** — Android SDK + gems ~8–10 GiB; **Xcode 17 needs ~40 GiB transient**.
Free space (old simulators, prior Xcode after upgrade, `~/Library/Developer/Xcode/DerivedData`) before upgrading Xcode.

## Line endings
Scripts authored on Windows can carry CRLF; macOS shells choke. Transfer then `tr -d '\r'` before running,
or author with LF. (All `mobile/scripts/*.sh` are committed LF.)
