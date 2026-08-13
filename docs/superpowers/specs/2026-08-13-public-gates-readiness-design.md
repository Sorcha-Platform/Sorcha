# Public Gates Readiness — design

**Date:** 2026-08-13
**Status:** Approved design; implementation plan to follow.
**Owner:** Stuart (reviews PRs). Execution: autonomous agents + fresh sessions.

## Why

Sorcha is already a public repository (MIT, Issues, Discussions, Wiki, a one-line
installer, `llms.txt`). What it is *not* yet is **safe and legible enough to invite strangers
and unpredictable autonomous agents to use, test, and give feedback on without the maintainer
babysitting it.** Stuart's direct input to the project is slowing down; the goal is to reach a
state where external humans and AIs can pick Sorcha up, exercise it across every entry point,
and return useful feedback — with the maintainer reviewing PRs rather than driving the work.

### Entry points in scope (all four)

1. **Self-host** via the one-line installer (`scripts/install.sh` / `install.ps1` →
   `sorcha-setup.sh`) — each tester gets an isolated local stack.
2. **Shared demo node** `n1.sorcha.dev` — zero-setup sandbox; **will** be hit by anonymous
   humans and agents.
3. **Docs-first** — read the concept via README, `docs/`, `llms.txt`, sample blueprints.
4. **MCP / agent-driven** — autonomous agents connect to the MCP server and drive workflows.

### Decisions taken during brainstorming (2026-08-13)

- **n1 must be fully hardened before any public invitation** — treat the live signing-oracle
  critical (#1397) and abuse-resistance as *blocking gates*, so the node is safe to be poked by
  anonymous agents and the maintainer can walk away from it.
- **Feedback lands in GitHub Issues (templated) + Discussions** — both are agent-legible and
  triageable on the maintainer's schedule. No in-product feedback or telemetry in this round.
- **Every work item is an agent-runnable chunk** — self-contained, acceptance-tested, no tribal
  knowledge required, PR-reviewable. Stuart reviews; agents (or fresh Claude sessions) execute.
- **#1397 is sequenced as blocking item WS0-1, not fixed ahead of the plan.**

## Non-goals (YAGNI for this round)

- In-product feedback widgets or usage telemetry.
- A marketing landing page / public roadmap site.
- An auto-resetting *ephemeral* demo node beyond a documented (optionally scheduled) reset.
- Closing every open backlog issue. Presentability is about honesty, not completeness — known
  limitations (e.g. #1380) are stated openly, not hidden.

## Architecture of the work: five workstreams, gated

```
WS0  Safety Gate  ── BLOCKING ──┐  (nothing is publicly invited until WS0 is green)
                                │
        once WS0 is underway, these run in PARALLEL (disjoint files, different agents):
                                │
WS1  First-Run Experience ──────┤
WS2  Agent / AI On-Ramp ────────┤
WS3  Feedback Loop ─────────────┤
WS4  Presentability Polish ─────┘
```

The gate is real: WS0 is the only thing that makes n1 safe to share. WS1–WS4 may be built and
merged before WS0 lands (they don't touch the security surface), but **the public invitation —
announcing n1 as an open sandbox — must not go out until WS0 is verified green.**

---

## WS0 — Safety Gate (BLOCKING)

**Goal:** n1 is safe to be hit by anonymous humans and agents; no repo artefact hands an
attacker signing authority; abuse self-limits; mess self-heals.

### WS0-1 — Close the #1397 signing oracle
**Context:** verified live 2026-08-12. Chain: repo-committed `ServiceAuth__ClientSecret` values
in `docker-compose.yml` (`blueprint-service-secret`, `wallet-service-secret`, +6 siblings) are
accepted at the *externally reachable* `POST /api/service-auth/token` → mints a real service
token → that token signs arbitrary bytes with the validator/SSR-owner wallet at
`sorcha:docket-signing` via `/api/v1/wallets/{addr}/sign` (`isPreHashed:true` makes it a blind
oracle). User tier already enforces ownership (403); **service tier does not**. `/api/internal/*`
is already gateway-invisible externally (404) — the model to copy.

**Fixes (all three; defence in depth):**
1. Take `/api/service-auth/token` off the public gateway route — internal-only, exactly as
   `/api/internal/*` already is.
2. Do not ship usable service-principal secrets in the repo. Compose reads them from an
   env/secret file that is generated per-deploy (the installer already generates config); the
   committed values become non-functional placeholders.
3. Enforce ownership on the service-tier `/sign` endpoint — a service token must not sign an
   arbitrary system wallet it has no relationship to.

**Done-criteria:** the exact repro in #1397, re-run against n1 from the public internet, returns
401/403/404 at step 1 or step 2 — not a signature. A regression test asserts the service-tier
sign path rejects a wallet the principal doesn't own. Close #1397 with the live evidence.

### WS0-2 — Production rate limits on n1
**Context:** there is **no `appsettings.Production.json`**; CLAUDE.md §8 says defaults are
100k/min "relaxed for pre-release" and must be tightened there. n1 runs those defaults today.
**Task:** add `appsettings.Production.json` (per service or shared) binding `RateLimiting` to
sane public values (login/register/TOTP/wallet-op policies especially), deploy to n1, verify a
burst gets throttled.
**Done-criteria:** a scripted burst against a public endpoint on n1 returns 429 at the
documented threshold; values are in the repo, not hand-set on the box.

### WS0-3 — n1 reset / re-genesis runbook (self-healing)
**Context:** re-genesis + AIAS re-provision tooling exists (`demo-deploy`, `n1-deploy`,
`network-bootstrap` skills; `rehearse.ps1`). **Task:** capture a single documented procedure to
wipe → re-genesis n1 (+ tiny coordination inside the `VAL_TIME_002` window) → re-provision AIAS →
`rehearse.ps1` green, and decide whether to schedule it (e.g. weekly) or run on demand after
abuse. Include the T069 test-register cleanup (#1403) as part of the sweep.
**Done-criteria:** the runbook exists in `docs/`, has been executed once end-to-end with a green
rehearsal recorded, and the schedule decision is written down.

### WS0-4 — Secret-scan CI gate
**Task:** a CI check (e.g. gitleaks or a repo-tuned grep gate matching the existing
`scripts/check-*.ps1` gate style) that fails the build if a usable secret pattern lands. Seed its
allowlist with the now-placeholder compose values so the ratchet only tightens.
**Done-criteria:** a deliberately-added fake secret reds CI; the gate is documented alongside the
other CI gates.

---

## WS1 — First-Run Experience

**Goal:** a stranger goes from zero to one completed real workflow in ~15 minutes, on self-host
or n1, without reading the source.

### WS1-1 — `SECURITY.md`
Root `SECURITY.md` with a coordinated-disclosure contact and scope. Table stakes for a public
security-focused project; its absence is conspicuous. **Done:** file present, linked from README,
GitHub "Security policy" shows green.

### WS1-2 — The golden-path walkthrough
One canonical, end-to-end tested walkthrough: install → sign in → create/instantiate a blueprint
→ complete one action → see the immutable record. Reuse the actor-based walkthrough framework
(`walkthrough-builder` skill) so it's executable and can't silently rot. **Done:** the
walkthrough runs green in CI (or a documented manual run), and README's "Try it in one line"
links straight to it.

### WS1-3 — Seed sample content
A fresh stack should not be empty. Ship (or auto-seed on first run) a demo blueprint + a little
data so the UI has something to show. **Done:** a newly installed stack presents at least one
explorable workflow without the user authoring anything.

---

## WS2 — Agent / AI On-Ramp

**Goal:** an autonomous agent can discover what Sorcha is, connect, authenticate, and drive a
workflow — from machine-readable entry points alone.

### WS2-1 — Refresh `llms.txt` + add `AGENTS.md`
`llms.txt` is 3 months stale; regenerate it against current features. Add a root `AGENTS.md`
(the emerging convention) pointing agents at: the MCP quickstart, the golden-path, the issue
templates, and the "what's real vs demo" honesty page. **Done:** both files current; `AGENTS.md`
links resolve.

### WS2-2 — External-agent MCP quickstart
A standalone doc: how an *external* agent points the MCP server at a node (self-host or n1),
authenticates, and drives one workflow to completion — with a copy-pasteable example. Grounded in
the 36-tool MCP server that already ships. **Done:** the example, run against a fresh stack,
completes a workflow; doc linked from `AGENTS.md` and README's AI-integration row.

### WS2-3 — Machine-pickable task surface
Ensure the "good first issue" / "agent-friendly" labelled issues (WS4-3) carry enough structure
(inputs, done-criteria) that an agent can pick one cold. This is the issue-hygiene half of WS4-3,
called out here because it's the agent-facing payoff.

---

## WS3 — Feedback Loop

**Goal:** structured, triageable feedback from humans and agents, landing where the maintainer
checks on their own schedule.

### WS3-1 — Issue templates
`.github/ISSUE_TEMPLATE/` (none exist today): `bug_report`, `feature_request`, and a
`feedback`/first-impressions form, plus `config.yml` routing open-ended questions to Discussions.
Shape fields so an agent fills them correctly (repro, entry-point used, node vs self-host,
version from footer/`/.well-known/openapi.json`). **Done:** "New issue" shows the chooser;
templates capture version + entry point.

### WS3-2 — Discussions seeding
Categories (Q&A, Ideas, Show-and-tell, Feedback) + a pinned post: "how to give feedback, what
we're looking for, what's demo-grade." **Done:** categories exist; pinned post published.

---

## WS4 — Presentability Polish

**Goal:** the project reads clearly to an outsider and states its own limits honestly.

### WS4-1 — README / docs external-reader pass
Read as a newcomer, not a contributor: remove or explain internal codenames, verify every
top-level link, ensure the DAD model and the four entry points are immediately graspable. **Done:**
a reviewer with no prior context can explain what Sorcha is and pick an entry point.

### WS4-2 — "What's real vs demo" honesty page
A `docs/` page stating maturity plainly: what's production-shaped, what's demo-grade, and known
limitations testers should know (e.g. #1380 org-key custody, rate-limit posture, replication model
needing explicit subscription). Prevents "surprise" feedback and builds trust. **Done:** page
exists, linked from README + `AGENTS.md`.

### WS4-3 — Backlog labelling for external contributors
Label a curated set of open issues `good first issue` / `help wanted` / `agent-friendly`, each
brought up to the inputs+done-criteria bar. **Done:** at least ~10 issues labelled and
self-contained; the label set is documented in `CONTRIBUTING.md`.

---

## Testing & verification strategy

- **WS0 is verified by live re-execution, not by green unit tests.** The #1397 lesson (and the
  broader F189 seam-bug pattern) is that a passing suite proves little about a deployed node.
  WS0-1's done-criterion is the *live repro failing*; WS0-2's is a *live burst throttling*.
- **WS1-2 and WS2-2 are verified by running the flow against a fresh stack**, not by asserting the
  doc exists.
- WS3/WS4 are verified by inspecting the rendered GitHub surfaces.

## Sequencing & ownership

1. **WS0 first and blocking.** Per the brainstorm, Stuart may choose to personally own the riskiest
   n1/secret/genesis steps; WS0-1/WS0-4 (code + CI) are agent-runnable with PR review.
2. WS1–WS4 dispatch in parallel to agents once WS0 is underway — disjoint files, low conflict risk.
3. **The public invitation is itself the terminal task**, gated on WS0 verified green + WS1
   (SECURITY.md + golden path) + WS3 (templates) merged.

## Open questions deferred to implementation

- WS0-1 fix #2 (per-deploy secret generation) vs #1 (gateway route removal) — which lands first;
  they're independent and either alone breaks the #1397 chain, so build the cheaper one first and
  treat the other as defence-in-depth.
- WS0-3 schedule cadence (on-demand vs weekly cron) — decide when the runbook is proven.
