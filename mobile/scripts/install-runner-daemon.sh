#!/usr/bin/env bash
# install-runner-daemon.sh — host the self-hosted GitHub Actions runner as a system
# LaunchDaemon so it survives reboot with NO GUI login session (svc.sh's LaunchAgent
# can't bootstrap over SSH). Run this ONCE with sudo. Idempotent (re-bootstraps cleanly).
#
#   sudo ./install-runner-daemon.sh
#
# Env: RUNNER_USER (default stuart), RUNNER_DIR (default /Users/$RUNNER_USER/actions-runner)

set -euo pipefail
[ "$(id -u)" -eq 0 ] || { echo "Must run as root:  sudo $0"; exit 1; }

RUNNER_USER="${RUNNER_USER:-stuart}"
RUNNER_DIR="${RUNNER_DIR:-/Users/$RUNNER_USER/actions-runner}"
LABEL="com.sorcha.actions-runner"
PLIST="/Library/LaunchDaemons/$LABEL.plist"

[ -x "$RUNNER_DIR/run.sh" ] || { echo "Runner not found at $RUNNER_DIR — run setup-runner.sh first."; exit 1; }

cat > "$PLIST" <<EOF
<?xml version="1.0" encoding="UTF-8"?>
<!DOCTYPE plist PUBLIC "-//Apple//DTD PLIST 1.0//EN" "http://www.apple.com/DTDs/PropertyList-1.0.dtd">
<plist version="1.0">
<dict>
    <key>Label</key><string>$LABEL</string>
    <key>UserName</key><string>$RUNNER_USER</string>
    <key>WorkingDirectory</key><string>$RUNNER_DIR</string>
    <key>ProgramArguments</key>
    <array>
        <string>$RUNNER_DIR/run.sh</string>
    </array>
    <key>EnvironmentVariables</key>
    <dict>
        <key>HOME</key><string>/Users/$RUNNER_USER</string>
        <key>PATH</key><string>/opt/homebrew/bin:/opt/homebrew/sbin:/usr/local/bin:/usr/bin:/bin:/usr/sbin:/sbin</string>
    </dict>
    <key>RunAtLoad</key><true/>
    <key>KeepAlive</key><true/>
    <key>ProcessType</key><string>Interactive</string>
    <key>StandardOutPath</key><string>$RUNNER_DIR/_diag/daemon.out.log</string>
    <key>StandardErrorPath</key><string>$RUNNER_DIR/_diag/daemon.err.log</string>
</dict>
</plist>
EOF

chown root:wheel "$PLIST"
chmod 644 "$PLIST"

# (re)load
launchctl bootout "system/$LABEL" 2>/dev/null || true
launchctl bootstrap system "$PLIST"
launchctl enable "system/$LABEL"
launchctl kickstart -k "system/$LABEL" || true

echo "Installed + started $LABEL."
echo "Verify online:  gh api /orgs/Sorcha-Platform/actions/runners --jq '.runners[].status'"
