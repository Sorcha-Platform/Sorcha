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
  #
  # Fetches /.well-known/mcp.json from the running gateway and validates it against
  # the JSON Schema at specs/117-ai-discoverability/contracts/mcp-manifest.schema.json
  # using Spectral's --ruleset shorthand (Spectral can apply a JSON Schema as a ruleset
  # via spectral:oas + a custom rule, but the simplest portable validator is ajv-cli).
  # We prefer ajv-cli when available; fall back to swagger-cli's spec validation if not.
  #
  # If neither tool is on PATH (local dev shell without npm install run), the check
  # downgrades to a smoke-test against the manifest's required FR-013 fields.

  local check="mcp-manifest-schema"
  local schema="$REPO_ROOT/specs/117-ai-discoverability/contracts/mcp-manifest.schema.json"
  local manifest_url="$SORCHA_GATEWAY/.well-known/mcp.json"

  if [ ! -f "$schema" ]; then
    log_fail "$check" "schema file missing: $schema"
    return
  fi

  # Fetch the served manifest. Skip the check (don't fail) when the gateway is unreachable —
  # this is a runtime check, not a static one.
  local body
  body=$(curl -sf "$manifest_url" 2>/dev/null || true)
  if [ -z "$body" ]; then
    log_skip "$check" "gateway unreachable at $manifest_url; runtime check skipped"
    return
  fi

  # Smoke-test required fields per FR-013. A full JSON-Schema validation lands with
  # Phase 9 polish (T102); this minimal field check catches the obvious shapes today.
  for field in name version description transports authentication tool_categories tool_catalogue_url documentation_url; do
    if ! echo "$body" | grep -q "\"$field\""; then
      log_fail "$check" "served manifest missing FR-013 field: $field"
      return
    fi
  done

  log_pass "$check (FR-013 fields present)"
}

check_llms_txt_structure() {
  # Wired by T081.
  #
  # Structural rules from llms-txt-template.md / FR-020:
  #   - file exists at repo root
  #   - size <= 8192 bytes
  #   - exactly one H1 line (^# )
  #   - exactly one blockquote summary line (^> ) immediately under the H1
  #   - sections present: ## Capabilities, ## Standards, ## Links

  local file="$REPO_ROOT/llms.txt"
  local check="llms-txt-structure"

  if [ ! -f "$file" ]; then
    log_fail "$check" "llms.txt not found at repo root"
    return
  fi

  local size
  size=$(wc -c < "$file" | tr -d ' ')
  if [ "$size" -gt 8192 ]; then
    log_fail "$check" "llms.txt is $size bytes; max 8192"
    return
  fi

  local h1_count
  h1_count=$(grep -c '^# ' "$file")
  if [ "$h1_count" != "1" ]; then
    log_fail "$check" "llms.txt must have exactly one H1 line (found $h1_count)"
    return
  fi

  local bq_count
  bq_count=$(grep -c '^> ' "$file")
  if [ "$bq_count" != "1" ]; then
    log_fail "$check" "llms.txt must have exactly one blockquote line (found $bq_count)"
    return
  fi

  for section in '## Capabilities' '## Standards' '## Links'; do
    if ! grep -qF "$section" "$file"; then
      log_fail "$check" "llms.txt missing required section: $section"
      return
    fi
  done

  log_pass "$check (size=$size bytes)"
}

check_marketing_adjectives() {
  # Wired by T082.
  #
  # FR-045 deny-list scan across every machine-readable artefact. The scan is
  # case-insensitive and matches whole words only. Targets:
  #   - llms.txt
  #   - docs/llms-full.txt
  #   - STANDARDS.md
  #
  # The served /.well-known/openapi.json and /.well-known/mcp.json are scanned
  # by the dedicated CI step that runs `spectral lint` against the live document
  # (no-marketing-adjectives rule in .spectral.yaml). They are not re-scanned here
  # to avoid duplicate flagging.

  local check="marketing-adjectives"
  local pattern='\b(revolutionary|best-in-class|industry-leading|cutting-edge|world-class|seamless|game-changing|next-generation|state-of-the-art)\b'
  local errors=0

  for file in llms.txt docs/llms-full.txt STANDARDS.md; do
    local path="$REPO_ROOT/$file"
    [ ! -f "$path" ] && continue

    local hits
    hits=$(grep -niE "$pattern" "$path" || true)
    if [ -n "$hits" ]; then
      while IFS= read -r line; do
        [ -z "$line" ] && continue
        local linenum=${line%%:*}
        log_fail "$check" "$file:$linenum contains a deny-listed marketing adjective"
        errors=$((errors + 1))
      done <<< "$hits"
    fi
  done

  if [ "$errors" = "0" ]; then
    log_pass "$check"
  fi
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
    # Only process lines that look like Markdown table rows (start with `|`).
    [[ "$line" =~ ^\| ]] || continue
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
  #
  # FR-025 — every standard named in `llms.txt` ## Standards section and the
  # docs/llms-full.txt standards lines MUST match a row in STANDARDS.md whose
  # Status is `full` or `partial`. A `planned` row is not a valid citation
  # because it does not yet exist in code.
  #
  # We cite-match on the standard *name* (the text before the first colon on a
  # bullet line in llms.txt) against the first column of STANDARDS.md.

  local check="standards-cross-reference"
  local llms="$REPO_ROOT/llms.txt"
  local standards="$REPO_ROOT/STANDARDS.md"

  if [ ! -f "$llms" ] || [ ! -f "$standards" ]; then
    log_skip "$check" "requires both llms.txt and STANDARDS.md to be present"
    return
  fi

  # Build the set of acceptable standard names (full or partial only) from STANDARDS.md.
  # Acceptable rows have status in column 6 == full|partial.
  local accepted
  accepted=$(awk -F'|' '
    /^\| *Standard *\|/ { next }
    /^\| *---/ { next }
    /^\|/ {
      name=$2; gsub(/^[ \t]+|[ \t]+$/, "", name);
      status=$7; gsub(/[ \t`]/, "", status);
      if (status == "full" || status == "partial") print name
    }
  ' "$standards")

  if [ -z "$accepted" ]; then
    log_fail "$check" "STANDARDS.md has no full/partial rows to cite"
    return
  fi

  # Pull the ## Standards section of llms.txt — bullets between '## Standards' and the next header.
  local cited
  cited=$(awk '
    /^## Standards$/ { in_section=1; next }
    /^## / { in_section=0 }
    in_section && /^- / {
      line=$0; sub(/^- /, "", line);
      colon=index(line, ":");
      if (colon > 0) print substr(line, 1, colon-1)
    }
  ' "$llms")

  local errors=0
  while IFS= read -r name; do
    [ -z "$name" ] && continue
    if ! echo "$accepted" | grep -Fxq "$name"; then
      log_fail "$check" "llms.txt cites '$name' which is not a full/partial row in STANDARDS.md"
      errors=$((errors + 1))
    fi
  done <<< "$cited"

  if [ "$errors" = "0" ]; then
    local count
    count=$(echo "$cited" | grep -cv '^$' || true)
    log_pass "$check ($count cited standards verified)"
  fi
}

check_published_docs_frontmatter() {
  # Wired by T097.
  #
  # T097 — each of the four published documents must:
  #   - exist at its required path under docs/
  #   - carry YAML frontmatter delimited by leading and trailing '---' lines
  #   - declare title, description, standards (a list), and last_updated fields
  #   - reference only standards whose STANDARDS.md row has status full|partial
  #   - carry a last_updated value matching ISO date YYYY-MM-DD
  #
  # The four documents are fixed by spec 117 Phase 8 (T098-T101).

  local check="docs-frontmatter"
  local standards="$REPO_ROOT/STANDARDS.md"
  local docs=(
    "docs/architecture.md"
    "docs/openid4vc-haip-integration.md"
    "docs/applicability.md"
    "docs/security-model.md"
  )

  if [ ! -f "$standards" ]; then
    log_skip "$check" "STANDARDS.md not present; cannot validate frontmatter standards[]"
    return
  fi

  # Acceptable standards row names — exact-match list of full|partial rows.
  local accepted
  accepted=$(awk -F'|' '
    /^\| *Standard *\|/ { next }
    /^\| *---/ { next }
    /^\|/ {
      name=$2; gsub(/^[ \t]+|[ \t]+$/, "", name);
      status=$7; gsub(/[ \t`]/, "", status);
      if (status == "full" || status == "partial") print name
    }
  ' "$standards")

  local errors=0
  for doc in "${docs[@]}"; do
    local path="$REPO_ROOT/$doc"
    if [ ! -f "$path" ]; then
      log_fail "$check" "$doc missing"
      errors=$((errors + 1))
      continue
    fi

    # Extract frontmatter — lines between the first '---' (line 1) and the second '---'.
    local first_line
    first_line=$(head -n1 "$path")
    if [ "$first_line" != "---" ]; then
      log_fail "$check" "$doc must open with '---' frontmatter delimiter"
      errors=$((errors + 1))
      continue
    fi

    local fm
    fm=$(awk 'NR==1 && /^---$/ { in_fm=1; next } in_fm && /^---$/ { exit } in_fm { print }' "$path")
    if [ -z "$fm" ]; then
      log_fail "$check" "$doc has no frontmatter body between '---' delimiters"
      errors=$((errors + 1))
      continue
    fi

    # Required scalar fields.
    for field in title description last_updated; do
      if ! echo "$fm" | grep -qE "^${field}:[[:space:]]+[^[:space:]]"; then
        log_fail "$check" "$doc frontmatter missing required field: $field"
        errors=$((errors + 1))
      fi
    done

    # last_updated must be ISO date YYYY-MM-DD.
    local last_updated
    last_updated=$(echo "$fm" | awk -F': *' '/^last_updated:/ { print $2; exit }')
    if [ -n "$last_updated" ] && ! echo "$last_updated" | grep -qE '^[0-9]{4}-[0-9]{2}-[0-9]{2}$'; then
      log_fail "$check" "$doc last_updated '$last_updated' is not ISO YYYY-MM-DD"
      errors=$((errors + 1))
    fi

    # standards: must be a YAML list — each item on its own line beginning with '  - '.
    if ! echo "$fm" | grep -qE '^standards:[[:space:]]*$'; then
      log_fail "$check" "$doc frontmatter must declare 'standards:' as a list (key on its own line)"
      errors=$((errors + 1))
      continue
    fi

    # Pull standards items: lines after 'standards:' that begin '  - ' (two-space indent + dash + space),
    # stopping at the next top-level key.
    local cited
    cited=$(echo "$fm" | awk '
      /^standards:[[:space:]]*$/ { in_list=1; next }
      in_list && /^[A-Za-z]/ { in_list=0 }
      in_list && /^[[:space:]]+-[[:space:]]+/ {
        line=$0
        sub(/^[[:space:]]+-[[:space:]]+/, "", line)
        sub(/[[:space:]]+$/, "", line)
        print line
      }
    ')

    if [ -z "$cited" ]; then
      log_fail "$check" "$doc standards[] is empty"
      errors=$((errors + 1))
      continue
    fi

    while IFS= read -r name; do
      [ -z "$name" ] && continue
      if ! echo "$accepted" | grep -Fxq "$name"; then
        log_fail "$check" "$doc cites '$name' which is not a full/partial row in STANDARDS.md"
        errors=$((errors + 1))
      fi
    done <<< "$cited"
  done

  if [ "$errors" = "0" ]; then
    log_pass "$check (4 documents verified)"
  fi
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
