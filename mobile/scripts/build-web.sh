#!/usr/bin/env bash
# build-web.sh — the shared "web build" step for the Sorcha Wallet mobile lanes.
#
# Our app is Blazor WASM, NOT a JS app, so the web build is `dotnet publish` (not
# `npm run build`). This script is the single deterministic web step every lane begins
# with; it is headless-runnable and identical whether a human, a tag, or CI triggers it.
#
#   1. dotnet publish Sorcha.Wallet.Pwa  -> static wwwroot
#   2. copy wwwroot into the Capacitor wrapper's  www/
#   3. rewrite Blazor's PWA <base href="/wallet/"> to "/" (Capacitor serves www/ at root)
#   4. strip *.br/*.gz precompressed dupes the webview won't content-negotiate (slims APK)
#   5. npx cap sync  (only once a native platform has been added)
#
# Env: SORCHA_REPO (default ~/projects/Sorcha), CONFIG (default Release)

set -euo pipefail
log() { printf "\n\033[1;34m==> %s\033[0m\n" "$*"; }

# Self-locate the repo from this script's path ($REPO/mobile/scripts/build-web.sh), so the
# web step operates on whatever checkout it lives in — the dev clone locally, or the runner's
# _work/<repo> checkout in CI. SORCHA_REPO overrides if ever needed.
SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
REPO="${SORCHA_REPO:-$(cd "$SCRIPT_DIR/../.." && pwd)}"
CONFIG="${CONFIG:-Release}"
WRAP="$REPO/mobile/wallet"
PROJ="$REPO/src/Apps/Sorcha.Wallet.Pwa/Sorcha.Wallet.Pwa.csproj"
PUBOUT="$(mktemp -d)/pub"

log "dotnet publish ($CONFIG)"
dotnet publish "$PROJ" -c "$CONFIG" -o "$PUBOUT"

log "populate $WRAP/www"
rm -rf "$WRAP/www"
mkdir -p "$WRAP/www"
cp -R "$PUBOUT/wwwroot/." "$WRAP/www/"

log "rewrite base href -> /"
# macOS/BSD sed in-place
sed -i '' 's#<base href="/wallet/"#<base href="/"#' "$WRAP/www/index.html"
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
log "web build complete"
