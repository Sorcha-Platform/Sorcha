---
name: prodexec
description: Use when the user addresses prodexec — a message starting with "prodexec ...", or asking to have prodexec build/add/fix/run a feature, or to answer/confirm/review-retry/cancel/check the status of a prodexec run. Drives the already-deployed prodexec orchestrator on the `tiny` host over SSH. Do NOT use for changing prodexec itself.
---

# prodexec — drive the deployed orchestrator

**prodexec** ("Production Executive") is a self-hosted, **already-deployed** autonomous
feature-lifecycle orchestrator running on the host **`tiny`**. It takes a feature/fix request
and drives it idea→merged-on-green using Claude Code as the worker, pausing only at a few human
gates. **This skill is a thin CLIENT** — it submits work to the running instance and reports
back. **Never implement or modify prodexec** (that lives in `C:\Projects\prodexec`, its own repo).

## The one rule: FIRE AND RETURN — never block or poll

A run starts **immediately** (auto-go) and the daemon executes it in the background, crash-proof
(token limits / API failures / reboots resume). So: **submit → report the run id → tell the user
how to follow it → STOP.** Do **not** sit and wait for a run to finish or hand-roll a polling
loop. Progress surfaces in Slack (each project posts to `#prodexec-<project>`, e.g.
`#prodexec-sorcha`), via `prodexec watch <id>` (live), and via `prodexec metrics <id> --json`
(one-shot). When you do need to react to completion, prefer `watch` over a manual poll loop.

## Transport (PowerShell tool only — never the Bash tool / Git Bash)

`tiny` SSH **must** go through Windows OpenSSH via the **PowerShell tool**. Git Bash has no agent
socket → auth fails. `prodexec` is not on the non-login SSH PATH, so use the `uv run` form:

```
ssh stuart@tiny 'cd ~/prodexec && ~/.local/bin/uv run prodexec <args>'
```

Single-quote the remote command (so the local shell doesn't expand `$`). The feature title is
double-quoted **inside** the single quotes. If connectivity or a command fails, **stop and report
what broke** — do not fabricate run ids or behavior.

## Intent → command

| The user means… | Command (`… uv run prodexec` prefix omitted) |
|---|---|
| Start new work ("prodexec add a logout endpoint to sorcha", "fix the flaky auth test") | `run <project> "<concise title>" --json` |
| …and "wait for me before it runs" / "manual hold" | `run <project> "<title>" --require-lock --json` then `lock <run_id>` to release |
| Answer a run parked at the answer gate | `answer <run_id> "<text>"` (empty text is rejected) |
| Resolve a structural-change (blast-radius) gate | `confirm <run_id> [--decision proceed\|abandon]` |
| Resolve the review gate | `review-retry <run_id> [--decision retry\|abandon]` |
| Status / "did it land?" / "what's it doing?" | `metrics <run_id> --json` (one-shot) · `watch <run_id>` (live) · `list --json` (all runs) |
| Stop / abandon a run (any stage) | `cancel <run_id>` |

- **`<project>` is a registered config name (e.g. `sorcha`), NOT a path.** Infer it from the
  request; if absent and ambiguous, run `list --json` (or read `~/.prodexec/config.toml`) and ask which.
- Turn a free-form request into **one concise feature title** scoped to a **single focused fix** —
  prodexec thrashes on multi-fix bundles; split bundles into separate runs.
- For gate commands, if the user gives no run id, look it up with `list --json` and confirm which run.
- `--auto-lock` is a **deprecated no-op** (auto-go is the default now) — don't pass it.

## Default run flow — auto-go

`run` **starts immediately**. Do NOT tell the user to "lock" it or that it's "parked awaiting
release" — that's the old behaviour. The only gate a normal run can hit unprompted is the
**blast-radius gate** on a structural/dependency change (see Gates). To stop a run, `cancel <id>`
at any stage. Only use `--require-lock` when the user **explicitly** wants a manual hold before
work begins; then release with `lock <id>` (or the Slack button).

## Status — use the JSON contract, never prose-parse

`run`, `list`, and `metrics` accept **`--json`** (stable, versioned: top-level `schema_version`,
currently `1`). JSON goes to **stdout only**; banners/logs go to stderr. **Never** grep/regex the
human output — read the JSON.

- **"Did it land?"** is exactly:
  ```
  ssh stuart@tiny 'cd ~/prodexec && ~/.local/bin/uv run prodexec metrics <id> --json' | <parse> .merged
  ```
  `merged: true` ⇔ the run reached terminal `done` (merged-on-green). Do not infer from text.
- `metrics <id> --json` fields: `schema_version`, `workflow_status`, `terminal_status`
  (`null | running | done | failed | paused_error | paused_rate_limited`), `first_ci_outcome`
  (`green|red|undetermined`), `review_fix_rounds`, `pr_number`, `pr_url`, `merged` (bool),
  `auto_recoveries` (int — human round-trips the resilience layer saved).
- `run <project> "…" --json` → `{schema_version, run_id, state:"enqueued", lock_required}` returned
  **immediately**. Capture `run_id` from the JSON — do **not** scrape "enqueued run …".
- `list --json` → `{schema_version, runs:[{run_id, workflow_status}]}`.
- `workflow_status` ≠ merged: a DBOS workflow returns `done`/`paused_error` as a normal value, so
  the raw workflow shows success either way. Trust `merged` / `terminal_status`, not workflow state.

### Live streaming — `watch`

`prodexec watch <id> [--interval N] [--timeout S]` emits **newline-delimited JSON events** and
exits at the first terminal/parked state (or after `--timeout`, default ~1 day). Event kinds:
`phase` (phase advanced), `status` (run status changed), `ci` (first CI outcome known),
`recovery` (a resilience auto-recovery fired), `terminal` (finished/parked — stream stops).
Recommend `watch` for following a run; `metrics --json` for a one-shot check.

## Runs self-heal — don't pre-warn about routine failures

Before parking, prodexec **auto-recovers** (bounded, idempotent): transient git/CI/network errors
retry with backoff; artifact-file merge conflicts (`CLAUDE.md`, `.specify/**`, `*FormatWhen*.razor`)
auto-resolve to base; a behind-base branch rebases and retries the merge; a malformed PR title
regenerates. The `auto_recoveries` metric counts the round-trips saved. **Do not pre-warn the user
about these as likely failures** — they're handled. (Mechanism: deterministic retryable-pattern
fast-path, else a cheap Haiku "stuck vs. silly" adjudicator restricted to a safe action set, with
fail-safe-to-human-park on low confidence / non-allowlisted action / exhausted budget.)

## Failures park cleanly — they don't crash

A recoverable problem that exhausts auto-recovery lands at **`terminal_status: "paused_error"`** — a
normal "needs attention" state with a logged rationale, **not** a crash. Causes: a genuine code
conflict, a real CI failure, or an exhausted recovery budget. (`paused_rate_limited` is the
rate-limit variant.) There is no crash/reconcile path to reason about. A `paused_error` is terminal
for that attempt — diagnose + relaunch (or hand-fix the PR), don't try to "resume" it.

## Gates — structural/needs-human pauses

These are distinct from `paused_error`; resolve them with the matching command:

| `workflow_status` | Meaning | Resolve |
|---|---|---|
| `AWAITING_BLAST_CONFIRM` | structural/dependency change (deterministic + cheap-model two-layer classifier) | `confirm <id>` (or `--decision abandon`) |
| `AWAITING_REVIEW` | review wants another pass | `review-retry <id>` (or `--decision abandon`) |
| `AWAITING_ANSWER` | the run asked a question | `answer <id> "<text>"` (empty answers rejected) |

If the user has said to keep momentum, auto-resolve these to proceed (see
[[feedback_prodexec_auto_release_locks]] / [[feedback_prodexec_gate_idle_pattern]]); merge is still
gated by CI + review.

## Per-project config (`~/.prodexec/config.toml`)

Knobs are per-project / per-section (no longer hardcoded). Read them when behaviour is in question:
- `default_branch` — base branch per `[[projects]]` (e.g. `master`; no longer hardcoded).
- `merge_method` — `squash` | `merge` | `rebase`.
- `[ci]` — `poll_interval_s`, `poll_max_attempts`.
- `[resilience]` — `retryable_error_patterns`, `recovery_max_attempts`.
- `[merge] scaffolding_allowlist` — auto-resolve-to-base conflict allowlist (default `.specify/**`,
  `CLAUDE.md`, `*FormatWhen*.razor`).

## Output discipline

Always show the **actual command you ran** and its **raw (JSON) output**, then one line of
plain-English summary.

## Example

> User: "prodexec fix the flaky InclusionProofEndpointTests in sorcha"

```
ssh stuart@tiny 'cd ~/prodexec && ~/.local/bin/uv run prodexec run sorcha "Fix flaky InclusionProofEndpointTests (SystemWalletSigning:ValidatorId)" --json'
# → {"schema_version":1,"run_id":"a1b2c3d4e5f6","state":"enqueued","lock_required":false}
```

Then: "Enqueued run `a1b2c3d4e5f6` for `sorcha` — it's running now (auto-go). Follow it live with
`prodexec watch a1b2c3d4e5f6`, check `prodexec metrics a1b2c3d4e5f6 --json` for a one-shot status,
or watch **#prodexec-sorcha**. Cancel anytime with `prodexec cancel a1b2c3d4e5f6`."
