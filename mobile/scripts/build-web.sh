#!/usr/bin/env bash
# build-web.sh — the shared "web build" step for every Sorcha mobile lane.
#
# Our apps are Blazor WASM, NOT JS apps, so the web build is `dotnet publish` (not `npm run build`).
# This script is the single deterministic web step every lane begins with; it is headless-runnable and
# identical whether a human, a tag, or CI triggers it.
#
#   1. dotnet publish the app          -> static wwwroot
#   2. copy wwwroot into the Capacitor wrapper's  www/
#   3. rewrite Blazor's <base href="/<app>/"> to "/"   (Capacitor serves www/ at root)
#   4. strip *.br/*.gz precompressed dupes the webview won't content-negotiate (slims the APK)
#   5. npx cap sync                    (only once a native platform has been added)
#
# TWO APPS (Feature 185):
#
#   wallet  — the citizen wallet.  BLE peripheral (holder).  app.sorcha.wallet
#   reader  — the verifier reader. BLE central  (reader).    app.sorcha.reader
#
# Both wrap a Blazor WASM app and both use the SAME sorcha-proximity plugin, in opposite roles. Keeping ONE
# script for both is deliberate: a second copy would drift, and the base-href rewrite (step 3) is exactly the
# kind of detail that goes silently wrong in a copy — the app would load to a blank page and nothing would
# say why.
#
# Usage:  ./build-web.sh [wallet|reader]      (default: wallet — preserves every existing caller)
# Env:    SORCHA_REPO (default: self-located), CONFIG (default: Release)

set -euo pipefail
log() { printf "\n\033[1;34m==> %s\033[0m\n" "$*"; }

APP="${1:-wallet}"

case "$APP" in
  wallet)
    PROJ_REL="src/Apps/Sorcha.Wallet.Pwa/Sorcha.Wallet.Pwa.csproj"
    BASE_HREF="/wallet/"
    ;;
  reader)
    PROJ_REL="src/Apps/Sorcha.Verifier.Pwa/Sorcha.Verifier.Pwa.csproj"
    BASE_HREF="/reader/"
    ;;
  *)
    echo "Unknown app '$APP'. Expected 'wallet' or 'reader'." >&2
    exit 2
    ;;
esac

# Self-locate the repo from this script's path ($REPO/mobile/scripts/build-web.sh), so the web step operates
# on whatever checkout it lives in — the dev clone locally, or the runner's _work/<repo> checkout in CI.
SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
REPO="${SORCHA_REPO:-$(cd "$SCRIPT_DIR/../.." && pwd)}"
CONFIG="${CONFIG:-Release}"

WRAP="$REPO/mobile/$APP"
PROJ="$REPO/$PROJ_REL"
PUBOUT="$(mktemp -d)/pub"

log "building '$APP' ($CONFIG)"

log "dotnet publish"
dotnet publish "$PROJ" -c "$CONFIG" -o "$PUBOUT"

log "populate $WRAP/www"
rm -rf "$WRAP/www"
mkdir -p "$WRAP/www"
cp -R "$PUBOUT/wwwroot/." "$WRAP/www/"

log "rewrite base href $BASE_HREF -> /"
# Capacitor serves www/ from the filesystem root, so the hosted app's base href is wrong inside the shell.
# macOS/BSD sed in-place (the build node is a Mac).
sed -i '' "s#<base href=\"$BASE_HREF\"#<base href=\"/\"#" "$WRAP/www/index.html"
grep -i '<base' "$WRAP/www/index.html" || true

log "strip precompressed dupes"
find "$WRAP/www" \( -name '*.br' -o -name '*.gz' \) -delete
du -sh "$WRAP/www"

log "cap sync"
cd "$WRAP"
if [ -d android ] || [ -d ios ]; then
  npx --no-install cap sync
else
  echo "(no native platform added yet — skipping cap sync)"
fi

rm -rf "$(dirname "$PUBOUT")"
log "web build complete: $APP"
