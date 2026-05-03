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
  #
  # Verifies STANDARDS.md exists, contains a single Markdown table with the seven
  # required columns (Standard, Version, Body, Spec URL, Components, Status, Notes),
  # every Status cell is one of `full`/`partial`/`planned`, and every Components-cell
  # path resolves to a real path in the repository (FR-026 + spec 117 audit).

  local file="$REPO_ROOT/STANDARDS.md"
  local check="standards-md-parse"

  if [ ! -f "$file" ]; then
    log_fail "$check" "STANDARDS.md not found at repo root"
    return
  fi

  # Pull the table header line; require all seven canonical column names in order.
  local header
  header=$(grep -E '^\| *Standard *\| *Version *\| *Body *\| *Spec URL *\| *Components *\| *Status *\| *Notes *\|' "$file" | head -n 1)
  if [ -z "$header" ]; then
    log_fail "$check" "STANDARDS.md table header missing one or more required columns (Standard|Version|Body|Spec URL|Components|Status|Notes)"
    return
  fi

  # Iterate body rows. Skip the header line and the divider line beneath it.
  local rownum=0
  local errors=0
  while IFS= read -r line; do
    case "$line" in
      '|'*'|') ;;
      *) continue ;;
    esac
    rownum=$((rownum + 1))
    # Skip the header itself and the alignment divider row.
    case "$line" in
      *Standard*Version*Body*Spec\ URL*Components*Status*Notes*) continue ;;
      *---*---*---*---*---*---*---*) continue ;;
    esac

    # Split the row into cells. Strip leading/trailing pipes, then split on '|'.
    local trimmed=${line#|}
    trimmed=${trimmed%|}
    IFS='|' read -ra cells <<< "$trimmed"
    if [ "${#cells[@]}" -lt 7 ]; then
      log_fail "$check" "STANDARDS.md row $rownum has ${#cells[@]} columns; expected 7"
      errors=$((errors + 1))
      continue
    fi

    # Status is column 6 (index 5). Trim spaces and any backticks.
    local status="${cells[5]}"
    status=$(echo "$status" | sed -e 's/[[:space:]]*//g' -e 's/`//g')
    case "$status" in
      full|partial|planned) ;;
      *)
        log_fail "$check" "STANDARDS.md row $rownum has invalid Status '$status' (must be full|partial|planned)"
        errors=$((errors + 1))
        ;;
    esac

    # Components is column 5 (index 4). Skip checking when it's "n/a".
    local components="${cells[4]}"
    components=$(echo "$components" | sed -e 's/^[[:space:]]*//' -e 's/[[:space:]]*$//')
    if [ -n "$components" ] && [ "$components" != "n/a" ]; then
      # Components may be a comma-separated list of `path`-quoted entries. Extract every backtick-wrapped path.
      while IFS= read -r path; do
        [ -z "$path" ] && continue
        if [ ! -e "$REPO_ROOT/$path" ]; then
          log_fail "$check" "STANDARDS.md row $rownum references missing path: $path"
          errors=$((errors + 1))
        fi
      done < <(echo "$components" | grep -oE '`[^`]+`' | tr -d '`')
    fi
  done < "$file"

  if [ "$errors" = "0" ] && [ "$rownum" -gt 0 ]; then
    log_pass "$check ($rownum rows verified)"
  fi
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
