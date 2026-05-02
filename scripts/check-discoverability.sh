#!/usr/bin/env bash
#
# check-discoverability.sh — orchestrator for the AI-discoverability CI gate.
#
# Spec 117 (AI Discoverability) introduces this script. The CI workflow at
# .github/workflows/ai-discoverability-check.yml runs it after booting the
# gateway. It can also be run locally against a running gateway:
#
#     ./scripts/check-discoverability.sh
#
# Each sub-check exits non-zero with a single-line message naming the
# offending file and reason. The orchestrator aggregates results and exits
# non-zero if any sub-check failed.
#
# Phases of spec 117 wire each sub-check progressively:
#   - Phase 1 (this commit): scaffold, hard-stub each check with a clear
#     "not yet implemented" message keyed by task ID.
#   - Phase 3 task T014: spectral lint against served /.well-known/openapi.json
#   - Phase 4 task T035: JSON-schema validation of /.well-known/mcp.json
#   - Phase 5 task T081: llms.txt structure check
#   - Phase 5 task T082: marketing-adjective deny-list
#   - Phase 5 task T085: standards cross-reference
#   - Phase 6 task T086: STANDARDS.md table parse
#
# Override the gateway URL with $SORCHA_GATEWAY (defaults to http://localhost).

set -euo pipefail

SORCHA_GATEWAY="${SORCHA_GATEWAY:-http://localhost}"
REPO_ROOT="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
FAILURES=0

log_pass() { printf '[discoverability] PASS  %s\n' "$1"; }
log_fail() { printf '[discoverability] FAIL  %s — %s\n' "$1" "$2" >&2; FAILURES=$((FAILURES + 1)); }
log_skip() { printf '[discoverability] SKIP  %s — %s\n' "$1" "$2"; }

check_spectral_lint() {
  # Wired by T014.
  log_skip "spectral-lint" "not yet implemented (lands with task T014)"
}

check_swagger_validate() {
  # Wired by T014.
  log_skip "swagger-validate" "not yet implemented (lands with task T014)"
}

check_mcp_manifest_schema() {
  # Wired by T035.
  log_skip "mcp-manifest-schema" "not yet implemented (lands with task T035)"
}

check_llms_txt_structure() {
  # Wired by T081.
  log_skip "llms-txt-structure" "not yet implemented (lands with task T081)"
}

check_marketing_adjectives() {
  # Wired by T082.
  log_skip "marketing-adjectives" "not yet implemented (lands with task T082)"
}

check_standards_md_parse() {
  # Wired by T086.
  log_skip "standards-md-parse" "not yet implemented (lands with task T086)"
}

check_standards_cross_reference() {
  # Wired by T085.
  log_skip "standards-cross-reference" "not yet implemented (lands with task T085)"
}

check_published_docs_frontmatter() {
  # Wired by T097.
  log_skip "docs-frontmatter" "not yet implemented (lands with task T097)"
}

main() {
  echo "[discoverability] running against gateway: $SORCHA_GATEWAY"
  echo "[discoverability] repo root: $REPO_ROOT"
  echo

  check_spectral_lint
  check_swagger_validate
  check_mcp_manifest_schema
  check_llms_txt_structure
  check_marketing_adjectives
  check_standards_md_parse
  check_standards_cross_reference
  check_published_docs_frontmatter

  echo
  if [ "$FAILURES" -eq 0 ]; then
    echo "[discoverability] all checks passed (or are not yet implemented per phase plan)"
    exit 0
  else
    echo "[discoverability] $FAILURES check(s) failed"
    exit 1
  fi
}

main "$@"
