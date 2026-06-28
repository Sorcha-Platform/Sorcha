---
name: prodexec
description: Use when the user addresses prodexec — a message starting with "prodexec ...", or asking to have prodexec build/add/fix/run a feature, or to release/approve/answer/lock/confirm/cancel/check the status of a prodexec run. Drives the already-deployed prodexec orchestrator on the `tiny` host over SSH. Do NOT use for changing prodexec itself.
---

# prodexec — drive the deployed orchestrator

**prodexec** ("Production Executive") is a self-hosted, **already-deployed** autonomous
feature-lifecycle orchestrator running on the host **`tiny`**. It takes a feature/fix request
and drives it idea→merged-on-green using Claude Code as the worker, pausing only at a few human
gates. **This skill is a thin CLIENT** — it submits work to the running instance and reports
back. **Never implement or modify prodexec** (that lives in `C:\Projects\prodexec`, its own repo).

## The one rule: FIRE AND RETURN — never block or poll

Starting a run **enqueues** it via the conductor and returns **immediately** with a run id; the
daemon executes it in the background, crash-proof (token limits / API failures / reboots resume).
So: **submit → report the run id → tell the user how to watch → STOP.** Do **not** wait for a run
to finish, do **not** loop polling `list`/`metrics`, do **not** tail logs. Progress surfaces in
Slack (each project posts to `#prodexec-<project>`, e.g. `#prodexec-sorcha`) and via `prodexec list`.

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
| Start new work ("prodexec add a logout endpoint to sorcha", "have prodexec fix the flaky auth test") | `run <project> "<concise title>"` |
| …and "auto" / "no gate" / "fire it off immediately" | `run <project> "<title>" --auto-lock` |
| Release / approve a parked run at the lock gate | `lock <run_id> [--decision locked\|abandon]` |
| Answer a run at the answer gate | `answer <run_id> "<text>"` |
| Resolve the blast-radius gate | `confirm <run_id> [--decision proceed\|abandon]` |
| Resolve the review gate | `review-retry <run_id> [--decision retry\|abandon]` |
| Status ("what's prodexec doing?", "status of <id>") | `list [--limit N]` or `metrics <run_id>` |
| Stop a run | `cancel <run_id>` |

- **`<project>` is a registered config name (e.g. `sorcha`), NOT a path.** Infer it from the
  request; if absent and ambiguous, run `list` (or read `~/.prodexec/config.toml`) and ask which.
- Turn a free-form request into **one concise feature title** scoped to a **single focused fix** —
  prodexec thrashes on multi-fix bundles; split bundles into separate runs.
- For gate commands, if the user gives no run id, look it up with `list` and confirm which run.

## Default run flow (the lock gate)

`run` **parks at the human lock gate by default** and does not execute until released. After
submitting, tell the user: the run id, that it's **parked awaiting release**, and how to release
(`prodexec lock <id>` here, or the one-tap button in the project's Slack channel). Only add
`--auto-lock` when the user **explicitly** says "auto" / "no gate" / "run immediately".

## Watching ≠ the truth

- `list` shows the DBOS workflow status. **`SUCCESS` ≠ merged** — the workflow returns
  `done`/`paused_error` as a normal value, so DBOS marks it SUCCESS either way. For "did it land?",
  read `metrics <run_id>` (`terminal_status` = `done` vs `paused_error`, `first_ci_outcome`,
  `review_fix_rounds`). A `paused_error` park (red CI / error) is terminal — relaunch, don't resume.
- `list`/`metrics`/`run` are thin DBOSClient surfaces (no executor) and are safe to run anytime.

## Output discipline

Always show the **actual command you ran** and its **raw output**, then one line of plain-English
summary (e.g. "Enqueued run `a1b2c3…`, parked at the lock gate — release with `prodexec lock a1b2c3`
or the button in #prodexec-sorcha").

## Example

> User: "prodexec fix the flaky InclusionProofEndpointTests in sorcha"

```
ssh stuart@tiny 'cd ~/prodexec && ~/.local/bin/uv run prodexec run sorcha "Fix flaky InclusionProofEndpointTests (SystemWalletSigning:ValidatorId)"'
# → started run a1b2c3d4e5f6
```

Then: "Enqueued run `a1b2c3d4e5f6` for `sorcha`, **parked at the lock gate**. Release it with
`prodexec lock a1b2c3d4e5f6` (or tap the button in **#prodexec-sorcha**); progress streams there
and in `prodexec list`."
