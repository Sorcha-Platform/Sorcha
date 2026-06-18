#!/usr/bin/env bash
# bootstrap-mac.sh — idempotent Mac build-node bring-up for Sorcha mobile (Wallet PWA)
#
# SAFE TO RE-RUN. Every step checks before acting. Installs / verifies the toolchain
# needed to build the Sorcha Wallet PWA (Blazor WASM) into native iOS/Android apps via
# Capacitor + fastlane:
#   - Homebrew packages: node, openjdk@17, cocoapods, gh, ruby
#   - Android SDK: cmdline-tools, platform-tools, build-tools, platform android-35
#     (android-35 = Google Play target-API requirement / Android 15, in force since
#      2025-08-31; also Capacitor 7's default compileSdk — verified, not assumed)
#   - Ruby 3.x (Homebrew) + bundler + fastlane (project Gemfile.lock pins exact versions)
#   - JAVA_HOME / ANDROID_HOME / PATH wired into ~/.zprofile (no sudo — no /Library symlink)
#   - git pull --ff-only of the repo
#
# DOES NOT touch: signing material (keystores, certs, profiles), Xcode (managed by hand),
# or anything requiring sudo. If a privileged step is ever needed it FAILS LOUD with the
# exact command to run interactively, rather than hanging on a password prompt.
#
# Usage:  ./bootstrap-mac.sh
# Env override: SORCHA_REPO=/path/to/Sorcha  (default: ~/projects/Sorcha)

set -euo pipefail

# --- pretty logging -------------------------------------------------------
log()  { printf "\n\033[1;34m==> %s\033[0m\n" "$*"; }
ok()   { printf "  \033[1;32mOK\033[0m %s\n" "$*"; }
warn() { printf "  \033[1;33m!!\033[0m %s\n" "$*"; }
die()  { printf "\n\033[1;31mXX %s\033[0m\n" "$*" >&2; exit 1; }

# --- config (single source of truth for versions) ------------------------
ANDROID_PLATFORM="android-36"        # Capacitor 8.4's compileSdk/targetSdk (Android 16).
                                     # Also satisfies Google Play's target-API floor (>= android-35,
                                     # Android 15, in force since 2025-08-31). Verified, not assumed.
ANDROID_BUILD_TOOLS="36.0.0"
JDK_FORMULA="openjdk@21"             # Capacitor 8's Android lib compiles at Java 21 (NOT 17:
                                     # the docs say "17+" but Android Studio bundles a JDK 21,
                                     # so headless builds need 21 or javac fails 'invalid source release: 21')
REPO_DIR="${SORCHA_REPO:-$HOME/projects/Sorcha}"
SDK_ROOT="$HOME/Library/Android/sdk"
ZPROFILE="$HOME/.zprofile"
MARKER_BEGIN="# >>> sorcha-mobile bootstrap >>>"
MARKER_END="# <<< sorcha-mobile bootstrap <<<"

# --- 0. preflight: disk space --------------------------------------------
log "Preflight"
AVAIL_GB="$(df -g /System/Volumes/Data | awk 'NR==2 {print $4}')"
echo "  free space on data volume: ${AVAIL_GB} GiB"
if [ "${AVAIL_GB:-0}" -lt 12 ]; then
  warn "Low disk (<12 GiB). Android SDK + gems need ~8-10 GiB. Continuing, but free space if installs fail."
fi
# C toolchain check: native Ruby gems include Ruby headers that #include <stdckdint.h>
# (C23), which only ships with Xcode 16+/clang 17+. On Xcode 15.x, `bundle install` will
# fail building native gems (e.g. json). fastlane itself still works via precompiled gems.
if printf '#include <stdckdint.h>\nint main(void){return 0;}\n' | clang -xc - -o /tmp/_ckd_test 2>/dev/null; then
  ok "C toolchain has C23 <stdckdint.h> — native Ruby gem builds (bundle install) OK"
else
  warn "Active clang lacks C23 <stdckdint.h> ($(xcodebuild -version 2>/dev/null | head -1 || echo 'Xcode ?'))."
  warn "  -> 'bundle install' of native gems will FAIL until Xcode 16+/clang 17+ is installed & selected."
  warn "  -> fastlane still runs via precompiled gems; the Android gradle build is unaffected (uses the JDK)."
fi
rm -f /tmp/_ckd_test

# --- 1. Homebrew on PATH --------------------------------------------------
log "Homebrew"
if [ -x /opt/homebrew/bin/brew ]; then
  eval "$(/opt/homebrew/bin/brew shellenv)"
  ok "$(brew --version | head -1)"
else
  die "Homebrew not found at /opt/homebrew. Install it once, interactively:
  /bin/bash -c \"\$(curl -fsSL https://raw.githubusercontent.com/Homebrew/install/HEAD/install.sh)\""
fi

# --- 2. Homebrew packages -------------------------------------------------
log "Homebrew packages"
brew update >/dev/null 2>&1 || warn "brew update failed (continuing with current formulae)"

brew_formula() {
  if brew list --formula "$1" >/dev/null 2>&1; then ok "$1 present"
  else log "installing formula $1"; brew install "$1"; fi
}
brew_cask() {
  if brew list --cask "$1" >/dev/null 2>&1; then ok "$1 present"
  else log "installing cask $1"; brew install --cask "$1"; fi
}

brew_formula node
brew_formula "$JDK_FORMULA"
brew_formula ruby
brew_formula cocoapods
brew_formula gh
brew_cask    android-commandlinetools

JAVA_HOME_PATH="$(brew --prefix)/opt/$JDK_FORMULA/libexec/openjdk.jdk/Contents/Home"
RUBY_BIN="$(brew --prefix)/opt/ruby/bin"
[ -d "$JAVA_HOME_PATH" ] || die "$JDK_FORMULA installed but JDK home missing at $JAVA_HOME_PATH"

# --- 3. environment in ~/.zprofile (replace-on-each-run, idempotent) -----
log "Shell environment (~/.zprofile)"
# gem executable dir (where the `fastlane` binary lands) — derived from the Ruby
# we just installed so it survives a future ruby version bump + re-run.
GEM_BIN="$("$RUBY_BIN/ruby" -e 'puts Gem.bindir' 2>/dev/null || true)"
[ -n "$GEM_BIN" ] || GEM_BIN="$RUBY_BIN"

touch "$ZPROFILE"
if grep -qF "$MARKER_BEGIN" "$ZPROFILE" 2>/dev/null; then
  # strip the existing managed block so edits below always propagate (no dupes)
  sed -i '' "/$MARKER_BEGIN/,/$MARKER_END/d" "$ZPROFILE"
fi
{
  echo ""
  echo "$MARKER_BEGIN"
  echo 'eval "$(/opt/homebrew/bin/brew shellenv)"'
  echo 'export LANG=${LANG:-en_US.UTF-8}   # CocoaPods requires a UTF-8 locale'
  echo "export JAVA_HOME=\"$JAVA_HOME_PATH\""
  echo "export ANDROID_HOME=\"$SDK_ROOT\""
  echo 'export PATH="$ANDROID_HOME/cmdline-tools/latest/bin:$ANDROID_HOME/platform-tools:$JAVA_HOME/bin:$PATH"'
  echo "export PATH=\"$RUBY_BIN:$GEM_BIN:\$PATH\""
  # .NET SDK lives at /usr/local/share/dotnet, normally added to PATH by macOS path_helper
  # (login shells only). CI steps use `bash -c source ~/.zprofile`, so add it explicitly here.
  echo 'export PATH="/usr/local/share/dotnet:$HOME/.dotnet/tools:$PATH"'
  echo "$MARKER_END"
} >> "$ZPROFILE"
ok "wrote sorcha-mobile block (JAVA_HOME, ANDROID_HOME, ruby+gem bin: $GEM_BIN)"

# apply to THIS shell too
export LANG=${LANG:-en_US.UTF-8}
export JAVA_HOME="$JAVA_HOME_PATH"
export ANDROID_HOME="$SDK_ROOT"
export PATH="$ANDROID_HOME/cmdline-tools/latest/bin:$ANDROID_HOME/platform-tools:$JAVA_HOME/bin:$RUBY_BIN:$GEM_BIN:$PATH"

# --- 4. Android SDK components -------------------------------------------
log "Android SDK ($SDK_ROOT)"
# sdkmanager ships in the android-commandlinetools cask
CLI_SDKMANAGER="$(command -v sdkmanager || true)"
if [ -z "$CLI_SDKMANAGER" ]; then
  CLI_SDKMANAGER="$(brew --prefix)/share/android-commandlinetools/cmdline-tools/latest/bin/sdkmanager"
fi
[ -x "$CLI_SDKMANAGER" ] || die "sdkmanager not found (expected from android-commandlinetools cask)"
echo "  using sdkmanager: $CLI_SDKMANAGER"

mkdir -p "$SDK_ROOT"
# Install a self-contained cmdline-tools inside SDK_ROOT plus the build components.
# sdkmanager is idempotent: already-installed packages are skipped.
#
# NOTE: `yes | sdkmanager` is piped to auto-answer the license prompts. Under
# `set -o pipefail`, `yes` is killed by SIGPIPE (exit 141) when sdkmanager closes
# its stdin, which would falsely fail the whole pipe even on a clean install. So we
# disable pipefail around the pipe and read sdkmanager's OWN status from PIPESTATUS.
set +o pipefail
yes | "$CLI_SDKMANAGER" --sdk_root="$SDK_ROOT" \
  "cmdline-tools;latest" \
  "platform-tools" \
  "platforms;$ANDROID_PLATFORM" \
  "build-tools;$ANDROID_BUILD_TOOLS" >/tmp/sdkmanager-install.log 2>&1
SDK_RC=${PIPESTATUS[1]}
set -o pipefail
[ "$SDK_RC" -eq 0 ] || { tail -20 /tmp/sdkmanager-install.log | tr '\r' '\n' | tail -20; die "sdkmanager install failed rc=$SDK_RC (see /tmp/sdkmanager-install.log)"; }
ok "platform-tools, $ANDROID_PLATFORM, build-tools $ANDROID_BUILD_TOOLS"

# Accept licenses (idempotent; same PIPESTATUS guard)
set +o pipefail
yes | "$CLI_SDKMANAGER" --sdk_root="$SDK_ROOT" --licenses >/dev/null 2>&1
LIC_RC=${PIPESTATUS[1]}
set -o pipefail
[ "$LIC_RC" -eq 0 ] && ok "licenses accepted" || warn "license acceptance returned rc=$LIC_RC"

# --- 5. Ruby gems: bundler + fastlane ------------------------------------
log "Ruby / fastlane"
echo "  ruby: $(ruby -v)"
gem list -i bundler >/dev/null 2>&1 && ok "bundler present" || { log "gem install bundler"; gem install bundler; }
# fastlane: installed here so the tool exists for ad-hoc use; per-project Gemfile.lock
# (added in Phase 1) is the deterministic pin used by `bundle exec fastlane` in CI.
gem list -i fastlane >/dev/null 2>&1 && ok "fastlane present" || { log "gem install fastlane"; gem install fastlane; }

# --- 6. sync repo --------------------------------------------------------
log "Repo sync ($REPO_DIR)"
if [ -d "$REPO_DIR/.git" ]; then
  git -C "$REPO_DIR" fetch --quiet origin || warn "git fetch failed"
  if git -C "$REPO_DIR" pull --ff-only 2>/tmp/gitpull.log; then
    ok "$(git -C "$REPO_DIR" log -1 --oneline)"
  else
    warn "git pull --ff-only failed (local changes / diverged?). Leaving repo untouched:"
    cat /tmp/gitpull.log
  fi
else
  warn "no git repo at $REPO_DIR (set SORCHA_REPO or clone it)"
fi

# --- 7. verification ------------------------------------------------------
log "Verification"
printf "  node       : %s\n" "$(node -v 2>&1)"
printf "  npm        : %s\n" "$(npm -v 2>&1)"
printf "  java       : %s\n" "$("$JAVA_HOME/bin/java" -version 2>&1 | head -1)"
printf "  JAVA_HOME  : %s\n" "$JAVA_HOME"
printf "  ANDROID_HOME: %s\n" "$ANDROID_HOME"
printf "  sdkmanager : %s\n" "$("$CLI_SDKMANAGER" --version 2>&1 | head -1)"
printf "  ruby       : %s\n" "$(ruby -v 2>&1)"
printf "  fastlane   : %s\n" "$(fastlane --version 2>&1 | grep -i '^fastlane ' | head -1 || echo '(run: source ~/.zprofile)')"
printf "  cocoapods  : %s\n" "$(pod --version 2>&1)"
printf "  gh         : %s\n" "$(gh --version 2>&1 | head -1)"

log "Bootstrap complete. Open a new shell (or 'source ~/.zprofile') to pick up PATH/JAVA_HOME/ANDROID_HOME."
