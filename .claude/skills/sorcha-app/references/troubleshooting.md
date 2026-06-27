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

## iOS signing (first real ios_adhoc / ios_beta run)

**`match` hangs forever at `Creating authorization token for App Store Connect API`** (no error,
process alive, socket to `17.56.x:443` ends in `CLOSE_WAIT`)
→ The Mac runs **Little Snitch** (per-app outbound firewall). It silently denies the **Homebrew
   Ruby** process (ad-hoc signed, no Team ID) to Apple's developer endpoints, while `curl`/`nc`
   (system binaries) are allowed — so the network "looks fine". Headless over SSH/daemon there's no
   GUI to answer LS's approval prompt, so the connect just times out.
→ Diagnose: `source ~/.zprofile && ruby -rsocket -e 'p Socket.tcp("17.56.10.18",443,connect_timeout:8)'`
   times out, but `curl -m5 https://api.appstoreconnect.apple.com/v1/apps` returns 401 fast → it's
   Little Snitch, not the network. Confirm: `systemextensionsctl list | grep -i littlesnitch`.
→ Fix: add a Little Snitch **allow** rule, *Any Process* → `*.apple.com` :443 (covers `developer.apple.com`,
   `api.appstoreconnect.apple.com`, `developerservices2.apple.com`, `idmsa`, `contentdelivery`). "Any
   Process" avoids churn when Homebrew bumps Ruby's Cellar path. Must be a permanent rule — the CI
   daemon is headless too.

**`match` itself hangs importing the cert / "User interaction is not allowed" on the keychain**
→ `login.keychain` is locked over SSH and under the runner LaunchDaemon (no GUI session).
→ The lanes call `setup_ci(force: true)` before `match` (temporary keychain). Already in the Fastfile.

**`archive` fails: `Signing for "App" requires a development team`**
→ Capacitor's generated Xcode project uses *automatic* signing with no team; `match` is *manual*.
→ The `apply_match_signing` helper (Fastfile) sets manual signing + `DEVELOPMENT_TEAM` +
   `PROVISIONING_PROFILE_SPECIFIER` from match's `sigh_<bundle>_<type>_{team-id,profile-name}` env
   vars before `build_app`. Team ID is **HY5HSW5FUT**; ad-hoc profile **`match AdHoc app.sorcha.wallet`**.

**`sdkmanager` "fails" but everything actually installed**
→ `set -o pipefail` + `yes | sdkmanager` — `yes` dies SIGPIPE (141), tripping the pipe. `bootstrap-mac.sh`
   reads `${PIPESTATUS[1]}` (sdkmanager's own code) instead. Not a real failure.

**APK built but is it signed with the right key?**
→ `$ANDROID_HOME/build-tools/36.0.0/apksigner verify --print-certs <apk> | grep SHA-256` and compare
   to the keystore SHA-256 (in the password manager / `make-upload-keystore.sh` output).

## Play Store upload (android_internal)

**`Google Api Error: Invalid request - The caller does not have permission`** (AAB builds fine,
upload step fails)
→ The service account authenticated but isn't authorised to release **this app's** tracks.
→ Play Console → **Users and permissions** → invite/select the SA email
   (`sorcha-play-ci@sorcha-494515.iam.gserviceaccount.com`). **What actually worked (2026-06-23):**
   open the SA → **Account permissions** tab → tick **Admin (all permissions)** → Apply. A narrower
   "Release to testing tracks" grant kept returning the permission error (didn't take / wrong scope);
   account-level Admin succeeded immediately. The separate **Setup → API access** page is now merged
   into Users and permissions on most accounts — the SA invite there IS the grant; don't hunt for it.
   Verify the SA shows **Status = Active** (not pending) before retrying.

**`Version code N has already been used`** on upload
→ The manual first upload was **versionCode 1**. Automated lane runs must use a higher code — pass
   `SORCHA_VERSION_CODE=<n>` (CI derives it from the run number; for a manual lane run bump it by hand).

## TestFlight (iOS internal testing)

**TestFlight asks for a "redeem code" / "an error occurred, try again later"**
→ You're on the *external/public* path or there's no testable build for your Apple ID yet. **Internal
   testing needs no code.** Fixes: (1) the build must be **Ready to Test** — finish Processing + answer
   the **export-compliance** question; (2) add yourself under **TestFlight → Internal Testing** as a
   tester (internal testers must be **team users** in Users and Access, max 100) and enable the build
   for the group; (3) the iPhone's TestFlight must be signed in with the **exact** invited Apple ID.

**Encryption questionnaire on every upload**
→ Bake the answer into `Info.plist`. Sorcha uses only standard crypto (Ed25519/P-256/RSA/AES/SHA) →
   `ITSAppUsesNonExemptEncryption=false` (Category 5 Part 2 exemption). Already committed.

**`ios_beta` upload rejected: `The bundle version must be higher than the previously uploaded
version: 'N'`** (HTTP 409, `cfBundleVersion` / `ENTITY_ERROR.ATTRIBUTE.INVALID.DUPLICATE`). NB this
can masquerade as a "`match` failure" — a CI run that errors early near `match` may actually be
fine there; run the lane locally with `--verbose` and read to the END: `match`/archive/sign/export
all succeed and the real death is the final `upload_to_testflight`.
→ The committed Capacitor project pins `CURRENT_PROJECT_VERSION = 1` and Info.plist resolves
   `CFBundleVersion = $(CURRENT_PROJECT_VERSION)`, so **every iOS build is version `1`**. CI exports
   `SORCHA_VERSION_CODE` (the run number) but **only the Android lane consumes it** — the iOS lanes
   ignored it. Once TestFlight holds any build, `1` is always rejected as lower/duplicate.
→ Fixed (#1052): both iOS lanes inject a **monotonic epoch-seconds** build number at archive time via
   `build_app(xcargs: ios_version_xcargs)` → `CURRENT_PROJECT_VERSION=#{Time.now.utc.to_i}` (+
   `MARKETING_VERSION` from `SORCHA_VERSION_NAME`). Epoch (~1.78e9) always exceeds any prior upload;
   the Android run-number scheme can't (run numbers are small — too low to beat an existing build).
   The committed pbxproj stays generic (xcargs overrides at build time only). No manual bump needed.

**Demo account / sign-in info requested**
→ Only for **App Review** (external TestFlight first build + App Store), never internal testing. When
   you do go external/prod: Sorcha needs a working **demo login** AND a **publicly reachable backend**
   the build points at (a LAN/dev box fails review "can't sign in").

## Disk
**Mac data volume low (~tens of GiB)** — Android SDK + gems ~8–10 GiB; **Xcode 17 needs ~40 GiB transient**.
Free space (old simulators, prior Xcode after upgrade, `~/Library/Developer/Xcode/DerivedData`) before upgrading Xcode.

## Line endings
Scripts authored on Windows can carry CRLF; macOS shells choke. Transfer then `tr -d '\r'` before running,
or author with LF. (All `mobile/scripts/*.sh` are committed LF.)
