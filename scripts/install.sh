#!/usr/bin/env bash
# SPDX-License-Identifier: MIT
# Copyright (c) 2026 Sorcha Contributors
# =============================================================================
# Sorcha one-line installer.
#
# Downloads Sorcha and runs the interactive setup (prerequisite checks, config
# questions, .env + JWT key generation, image pull, service start, bootstrap).
#
#   curl -fsSL https://raw.githubusercontent.com/Sorcha-Platform/Sorcha/master/scripts/install.sh | bash
#
# Prefer to read it before running (recommended for any curl|bash):
#   curl -fsSL https://raw.githubusercontent.com/Sorcha-Platform/Sorcha/master/scripts/install.sh -o sorcha-install.sh
#   less sorcha-install.sh && bash sorcha-install.sh
#
# Environment overrides:
#   SORCHA_DIR   target directory to clone into      (default: ./sorcha)
#   SORCHA_REF   branch or tag to install            (default: master)
#
# Any extra arguments are passed straight through to scripts/sorcha-setup.sh
# (e.g. `--quiet` for a non-interactive, all-defaults install).
# =============================================================================
set -euo pipefail

REPO_URL="https://github.com/Sorcha-Platform/Sorcha.git"
REF="${SORCHA_REF:-master}"
DIR="${SORCHA_DIR:-sorcha}"

say()  { printf '\033[0;36m[sorcha-install]\033[0m %s\n' "$1"; }
die()  { printf '\033[0;31m[sorcha-install] ERROR:\033[0m %s\n' "$1" >&2; exit 1; }

# --- Prerequisites the installer itself needs (setup checks the rest) --------
command -v git >/dev/null 2>&1 \
  || die "git is required. Install it and re-run: https://git-scm.com/downloads"
command -v docker >/dev/null 2>&1 \
  || die "Docker is required. Install Docker Desktop / Engine and re-run: https://docs.docker.com/get-docker/"

# --- Locate or fetch a Sorcha checkout ---------------------------------------
if [ -f "scripts/sorcha-setup.sh" ]; then
  say "Existing Sorcha checkout detected here — running setup in place."
  TARGET="$(pwd)"
elif [ -d "$DIR/.git" ] && [ -f "$DIR/scripts/sorcha-setup.sh" ]; then
  say "Using existing clone at ./$DIR (fetching latest $REF)."
  git -C "$DIR" pull --ff-only >/dev/null 2>&1 || say "(could not fast-forward — keeping your local checkout as-is)"
  TARGET="$(cd "$DIR" && pwd)"
else
  [ -e "$DIR" ] && die "Target ./$DIR already exists but is not a Sorcha clone. Set SORCHA_DIR to another path."
  say "Cloning Sorcha ($REF) into ./$DIR ..."
  git clone --depth 1 --branch "$REF" "$REPO_URL" "$DIR" \
    || die "git clone failed. Check your network and that '$REF' is a valid branch/tag."
  TARGET="$(cd "$DIR" && pwd)"
fi

cd "$TARGET"
say "Handing off to the interactive setup (scripts/sorcha-setup.sh)."
echo ""

# When this installer was piped from curl, our stdin is the pipe — not a
# keyboard — so the setup's prompts must read from the controlling terminal.
# If there is no terminal at all (e.g. CI), fall back to an all-defaults run.
if [ -r /dev/tty ]; then
  exec bash scripts/sorcha-setup.sh "$@" </dev/tty
else
  say "No interactive terminal detected — running with defaults (--quiet)."
  exec bash scripts/sorcha-setup.sh --quiet "$@"
fi
