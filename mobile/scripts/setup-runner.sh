#!/usr/bin/env bash
# setup-runner.sh — install/register the self-hosted GitHub Actions runner on this Mac.
#
# Idempotent: re-running re-uses the existing runner dir, re-registers with --replace, and
# (re)starts the service. The org registration token is fetched via `gh` at run time, so no
# token is ever stored or passed around. Requires `gh` authenticated with admin:org.
#
# Labels: self-hosted (implicit) + macos + sorcha-build  -> matches runs-on in build.yml.
#
# Env: ORG (default Sorcha-Platform), RUNNER_NAME (default macmini-sorcha-build)

set -euo pipefail
log() { printf "\n\033[1;34m==> %s\033[0m\n" "$*"; }
warn(){ printf "  \033[1;33m!!\033[0m %s\n" "$*"; }

# Register at the REPOSITORY level, not org level. An org-level runner (Default group,
# visibility=all) reported "online" but GitHub never routed repo jobs to it — the job sat
# on "Waiting for a runner..." indefinitely. Repo-level registration attaches the runner
# directly to the repo and dispatches immediately. Set SCOPE=org + ORG to override.
SCOPE="${SCOPE:-repo}"
REPO="${REPO:-Sorcha-Platform/Sorcha}"
ORG="${ORG:-Sorcha-Platform}"
RUNNER_NAME="${RUNNER_NAME:-macmini-sorcha-build}"
LABELS="sorcha-build"          # self-hosted is auto-added; match runs-on on this unique label
RUNNER_DIR="$HOME/actions-runner"

if [ "$SCOPE" = "repo" ]; then
  REG_API="/repos/$REPO/actions/runners/registration-token"
  RUNNER_URL="https://github.com/$REPO"
else
  REG_API="/orgs/$ORG/actions/runners/registration-token"
  RUNNER_URL="https://github.com/$ORG"
fi

command -v gh >/dev/null || { echo "gh not found"; exit 1; }
gh auth status >/dev/null 2>&1 || { echo "gh not authenticated (gh auth login)"; exit 1; }

log "Latest runner version"
VER="$(gh api repos/actions/runner/releases/latest --jq .tag_name | sed 's/^v//')"
TARBALL="actions-runner-osx-arm64-${VER}.tar.gz"
echo "  runner v$VER"

mkdir -p "$RUNNER_DIR"; cd "$RUNNER_DIR"

if [ ! -f "$RUNNER_DIR/config.sh" ]; then
  log "Download runner $VER"
  curl -fsSL -o "$TARBALL" "https://github.com/actions/runner/releases/download/v${VER}/${TARBALL}"
  tar xzf "$TARBALL"
  rm -f "$TARBALL"
else
  log "Runner already downloaded ($(cat ./.runner 2>/dev/null | grep -o '\"agentName\": *\"[^\"]*\"' || echo unconfigured))"
fi

log "Register ($SCOPE: ${RUNNER_URL}; token via gh, --replace for idempotency)"
TOKEN="$(gh api -X POST "$REG_API" --jq .token)"
[ -n "$TOKEN" ] || { echo "failed to obtain registration token"; exit 1; }
./config.sh --unattended \
  --url "$RUNNER_URL" \
  --token "$TOKEN" \
  --name "$RUNNER_NAME" \
  --labels "$LABELS" \
  --replace

log "Install + start launchd service"
if ./svc.sh status >/dev/null 2>&1; then
  ./svc.sh start || true
else
  if ./svc.sh install 2>/tmp/svc-install.log; then
    ./svc.sh start || true
  else
    warn "svc.sh install failed (often: no GUI login session over SSH). Detail:"
    cat /tmp/svc-install.log
    warn "Runner is REGISTERED but not running as a service. Options:"
    warn "  a) run it from a GUI Terminal on the Mac:  cd $RUNNER_DIR && ./svc.sh install && ./svc.sh start"
    warn "  b) run foreground for now:                 cd $RUNNER_DIR && ./run.sh"
    warn "  c) a reboot-surviving LaunchDaemon needs sudo — ask before doing that."
  fi
fi

log "Service status"
./svc.sh status 2>&1 | head -15 || true
