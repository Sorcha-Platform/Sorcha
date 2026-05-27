# T010 — `scripts/sorcha-setup.sh` audit

**Spec**: 117-ai-discoverability · **Task**: T010 · **Date**: 2026-05-02

Audit of `scripts/sorcha-setup.sh` for prerequisite-check completeness, exit-code discipline, silent-failure paths, and remediation hints. Findings inform T092 (Phase 7 US5 refactor).

## Baseline

| Property | State |
|---|---|
| File exists | ✅ `scripts/sorcha-setup.sh`, 424 lines |
| Shebang | ✅ bash |
| `set -euo pipefail` at top | ✅ line 20 |
| Total prerequisite checks today | 4 (Docker, Compose, daemon, Git) |

## Prerequisite-check inventory

| Lines | Check | Pass criterion | Failure behaviour | Remediation hint | Verdict |
|---|---|---|---|---|---|
| 140–147 | Docker installed | `command -v docker` returns 0 | Set `missing=1`, exit 1 at line 179 | "Get it at https://docker.com/products/docker-desktop" | ✅ adequate |
| 150–159 | Docker Compose v2 | `docker compose version` OR standalone `docker-compose` | Set `missing=1`, exit 1 | None explicit | ⚠ accepts v1 fallback; no version gate |
| 162–167 | Docker daemon running | `docker info` exits 0 | Set `missing=1`, exit 1 | "Start Docker Desktop first." | ✅ adequate (message is Desktop-centric — a Linux user with `dockerd` running won't trip this, but the hint is wrong if they do) |
| 170–174 | Git (optional) | `command -v git` returns 0 | WARN only, no exit | None | ⚠ optional with no install link |
| — | Ports 80/443/8080 free | — | — | — | ❌ **not implemented** (FR-032 requires) |
| — | OpenSSL OR Python3 | — | — | — | ❌ **not implemented** — used at lines 121–127 for JWT key gen but not pre-flighted |
| — | PowerShell 7.5+ (warning) | — | — | — | ❌ not applicable in bash setup; defer to PS-equivalent setup script if one exists |

## Silent-failure inventory

Six paths where a failure can escape without a non-zero exit:

1. **Lines 315–321 — image pull**. `docker compose pull 2>/dev/null` redirects stderr; the wrapper warns but does not exit. Stale or missing images can survive setup.
2. **Lines 328–335 — compose up**. Success path returns `true` (line 329), losing exit information. No container-health verification afterwards.
3. **Lines 340–357, 419 — health check**. `wait_for_health()` returns 1 on timeout, but `main()` (line 419) calls it without checking the return code. User sees the success summary even if `/api/health` never came up.
4. **Line 346 — health curl**. `curl -sf http://localhost/api/health > /dev/null 2>&1` silences both success and failure output; no distinction between port closed / 404 / timeout.
5. **Lines 267–268 — `.env` backup**. `cp` exit code unchecked; warn issued without verifying the original file existed.
6. **Lines 199–216, 121–127 — JWT key generation**. `generate_jwt_key()` calls openssl/python3/urandom without pre-flight; if all three are unavailable, an empty string is returned and the failure surfaces only at runtime in the gateway.

## Error-message format compliance

The spec mandates the form `[sorcha-setup] missing prerequisite: <name> (≥ <version>); install via <link>`. Current script messages don't match:

| Line | Current message | Issue |
|---|---|---|
| 145 | `[ERROR] Docker is not installed. Get it at https://docker.com/products/docker-desktop` | Has link, wrong prefix, no version |
| 157 | `[ERROR] Docker Compose is not available` | No link, no version |
| 165 | `[ERROR] Docker daemon is not running. Start Docker Desktop first.` | Linux-blind hint |
| 173 | `[WARN] Git not found (optional, needed for development)` | No link, "optional" framing dilutes the signal |

## Recommended T092 helper-function structure

```bash
check_docker_installed()         # extract 140–147
check_docker_daemon_running()    # extract 162–167
check_docker_compose_v2()        # extract + tighten 150–159; gate on version ≥ 2.0
check_port_available()           # NEW — covers 80, 443, 8080 (FR-032)
check_openssl_or_python3()       # NEW — pre-flight JWT-gen deps
check_git_installed()            # promote 170–174 from optional warn
validate_image_pull()            # wrap 315–321; check exit code
validate_service_startup()       # wrap 328–335; inspect container state
validate_health_endpoint()       # wrap 346–357; parse curl response, return non-zero on failure
remediate_<check_name>()         # one per check; exact format from FR-033
```

Each helper:

1. Exits with non-zero on critical failure (matching FR-032).
2. Emits a single-line message in the spec's mandated format.
3. Includes a remediation hint via the matching `remediate_<name>` (URL or `apt`/`brew`/`winget` invocation).

## T092 effort estimate

| Aspect | Size |
|---|---|
| Lines to rewrite | ~120 of 424 (~28%) |
| New functions | 6–8 |
| Suggested bats tests (T091) | ~12 (positive + negative per check, plus health-endpoint failure modes) |
| Refactor effort | **Medium** — half a day for a focused pass |

## Critical gaps (in order of impact)

1. **HIGH** — Health check timeout ignored; setup reports success on a broken stack (lines 340–357, 419). Single-line fix to gate `main()` on `wait_for_health` return code.
2. **HIGH** — Port availability not checked (FR-032 explicit requirement). Adds ~20 lines of new code.
3. **HIGH** — Docker Compose v2 not version-gated; v1 fallback silently accepted (lines 150–159). ~10 lines.
4. **MEDIUM** — Image pull exit code ignored (lines 315–321). 5 lines.
5. **MEDIUM** — JWT-gen dependency not pre-flighted (lines 119–129). 15 lines.
6. **LOW** — Error message format does not match FR-033 mandated shape. 10 lines of message rewrites.

## Phase 7 implication

The script is structurally sound (`set -euo pipefail` at top, clean phases, no bashisms beyond intent). T092 is additive plus a few exit-code corrections — not a rewrite. T091 (bats tests) and T090 (clean-VM CI) gate it.
