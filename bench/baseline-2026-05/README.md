# Validator Performance Baseline — May 2026

**Status:** TOOLING-READY. Capture has not been run on a quiescent machine yet.
**Branch:** `bench/validator-baseline-2026-05`
**Goal:** Honest pre-thesis baseline of validator performance so the eventual
programmable-rules work (spec `specs/121-programmable-validation-rules/`;
companion architectural memo `Project Sorcha/Validator2/2026-05-09-programmable-validation-thesis.md`
held in shared memory outside this repo) has numbers to compare against — not opinion.

---

## What got captured

Per-rule and per-section telemetry from `Sorcha.Validator.Service`, gated
behind `Validator:Benchmark:Enabled` so the instrumentation can stay on master
indefinitely without runtime cost. When disabled, `RuleTelemetry.TimeRule` /
`TimeSection` return `default` structs and the JIT elides the using-block.

| Layer | Mechanism | Storage |
|---|---|---|
| Per-rule emission counts | `CreateError` choke point in both `ValidationEngine` and `RightsEnforcementService` | `walkthrough-runs/{wt}/seq-NNN.json → telemetry.rules.{CODE}.emissions` |
| Per-rule timing (I/O-heavy paths) | `using var _ = RuleTelemetry.TimeRule("VAL_XYZ_NNN")` wraps | `…rules.{CODE}.{p50,p95,p99,maxNanos,totalNanos}` |
| Per-section timing (12 sections) | `RuleTelemetry.TimeSection("Structure")` etc. at top of each section method | `…sections.{Name}.…` |
| End-to-end per-validation | `Total` section wraps `ValidateTransactionAsync` | `…sections.Total.…` |
| Per-walkthrough wall time | Stopwatch around `run.ps1` invocation | `seq-NNN.json → wallTimeMs` |
| Microbenchmarks | BenchmarkDotNet on pure-compute methods + telemetry overhead | `microbenchmarks/` |
| Runtime counters (alloc rate, GC, CPU, working set) | `dotnet-counters collect` against the validator's diagnostic socket | `dotnet-counters/validator.csv` |
| Environment | git SHA, .NET ver, CPU, RAM, OS | `env.json` |

### What we explicitly do *not* claim to capture per-rule

Single-`if` rules (most of `VAL_STRUCT_*`, `VAL_TIME_*`, `VAL_REPLAY_*`) are
cheap enough that wrapping each emission site with a timing scope would add
*more* overhead than it measures. These rules are captured at:

- **Section level** (the section timer covers the whole if-block group)
- **Emission count level** (every `CreateError` call increments a counter)

Per-rule timing histograms exist for the rules whose evaluation cost varies
meaningfully per call: `VAL_SIG_VERIFY`, `VAL_SCHEMA_004` (JSON Schema eval),
`VAL_BP_RESOLVE`, `VAL_BP_002` (signer auth), `VAL_BP_003` (route
reachability), `VAL_CHAIN_PREDECESSOR_LOOKUP`, `VAL_CHAIN_FORK`,
`VAL_CHAIN_DOCKET`, `VAL_REV_002`, `VAL_REV_003`, `VAL_REV_005`, `VAL_PERM_006`.
This is the honest line — see the post-baseline thesis comparison for which
rules turn out to need finer-grained timing instrumentation.

---

## Methodology

| Walkthrough | Why | Iterations | Concurrent N |
|---|---|---|---|
| AssuredIdentity | Canonical 10-step, exercises HAIP presentation lifecycle (F119 carve-out path) | 100 | 10 |
| ConstructionPermit | Mixed governance + workflow, 5 actors / 6 actions / 1 register | 100 | 10 |
| PayloadTests | Exercises `VAL_FILE_*` family (large multi-chunk payloads) | 100 | 10 |

**Warmup:** first 10 sequential runs of each walkthrough are discarded before
percentile aggregation. Concurrent runs use the same warmup runs (drained
serially first).

**Build:** Release config only. Debug numbers are not honest baseline.

**Quiescent machine:** No other apps. Browser closed, IDEs closed, scheduled
tasks paused. Plugged in (don't run on battery thermal throttling).

The exact env will be captured to `env.json` at run time — git SHA, .NET
version, OS, CPU model, core count, RAM. The future-thesis comparison MUST
re-run on the same machine in the same conditions or note the difference.

---

## How to run the capture

### Prerequisites (on the capture machine)

```powershell
# .NET 10 SDK
dotnet --version  # ≥ 10.0

# dotnet-counters (for runtime counter capture)
dotnet tool install --global dotnet-counters

# Docker Desktop with WSL2 (or Linux Docker)
docker --version

# Verify benchmark project builds
dotnet build bench/Sorcha.Validator.Benchmarks -c Release
```

### Steps

```powershell
# 1. Bring up the stack with the benchmark overlay
docker compose `
    -f docker-compose.yml `
    -f bench/baseline-2026-05/docker-compose.benchmark.yml `
    up -d --force-recreate validator-service

# 2. Wait for validator-service to settle and bootstrap any walkthroughs
#    that need fresh state. Ensure the dev system register is bootstrapped.
#    (The walkthroughs do this themselves on first run.)

# 3. Set the service token so the harness can call /api/internal/benchmark/*.
#    The bootstrap admin token works; alternatively mint a service principal.
$env:SORCHA_BENCH_SERVICE_TOKEN = "<your-token>"

# 4. SMOKE TEST first (≈ 1 minute) — proves the harness wires up end-to-end
pwsh bench/baseline-2026-05/run-baseline.ps1 -SmokeOnly

# 5. Inspect bench/baseline-2026-05/walkthrough-runs/AssuredIdentity/seq-001.json
#    Confirm telemetry.rules has populated entries. If not, the smoke run
#    found a wiring bug — fix before committing to the full sweep.

# 6. Full sweep (3-5 hours, leave the machine alone)
pwsh bench/baseline-2026-05/run-baseline.ps1

# 7. Summarise
pwsh bench/baseline-2026-05/summarise.ps1
#  → summary-tables.md, per-rule-telemetry.json
```

### Halting / resuming

`run-baseline.ps1` has no resume — it overwrites the output folder. Re-run from
zero. Halting mid-sweep with Ctrl+C and re-running is fine; just expect
duplicate counters folder content from the previous run.

---

## What the future-thesis comparison should look like

When the programmable-rules thesis (or any validator perf change) ships,
re-run this exact tooling on the same machine, same git checkout flow, same
walkthroughs. The comparison report should answer:

1. Did p99 end-to-end validation get faster, slower, or stay flat per
   walkthrough?
2. Did any single rule's p99 spike >2× ? Why?
3. Did allocation rate change?
4. Did the rule-eval fraction of total time shift? (This is the key thesis
   question — programmable rules ADD interpretation cost. We need to know
   what budget we have.)
5. Are there any new rules in the post-thesis run that didn't exist in
   baseline? (Expected: YES — `validationPolicyVersion` checks, etc.)

A regression watcher can keep the instrumentation gated-on in a CI nightly
and fail the build if any of (1)/(2)/(3) regresses by >10%.

---

## Reproducibility caveats

- **Bloom filter / cache state.** Validator caches blueprints, validator
  rosters, etc. After a long-running stack the cache is hot; after `compose
  down -v` it's cold. Cold-cache numbers will be worse — use the same warmup
  count for comparison.
- **MongoDB / Redis.** Local volumes accumulate data across runs. For the
  most-honest baseline, `docker compose down -v` BEFORE the run; for
  comparing-to-current-state, leave them. Document which.
- **Walkthrough determinism.** The walkthroughs are not 100% deterministic
  (UUIDs, timestamps, random ordering in some places). Per-run variance is
  normal; we look at percentiles, not single-run numbers.
- **Network.** All services on one Docker bridge; no cross-host network.
  When deployed to n1.sorcha.dev, network latency to MongoDB shifts the
  Chain section materially. Baseline is local-only.

---

## File layout

```
bench/baseline-2026-05/
├── README.md                          ← this file
├── docker-compose.benchmark.yml       ← overlay enabling Validator:Benchmark
├── run-baseline.ps1                   ← top-level orchestrator
├── run-walkthrough.ps1                ← drives one walkthrough N times
├── summarise.ps1                      ← post-capture aggregation
├── env.json                           ← captured at run time (git SHA + machine)
├── summary-tables.md                  ← captured at summarise time
├── per-rule-telemetry.json            ← aggregated raw, all sequential runs
├── walkthrough-runs/
│   ├── AssuredIdentity/
│   │   ├── seq-011.json … seq-100.json    (post-warmup)
│   │   └── concurrent-N10.json
│   ├── ConstructionPermit/…
│   └── PayloadTests/…
├── microbenchmarks/                   ← BenchmarkDotNet artifacts
├── dotnet-counters/                   ← runtime counter CSVs
├── bench-diag/                        ← diagnostic socket mount point
└── raw/                               ← any ad-hoc captures
```

---

## Top finding

**Validator is chain-bound. Rule evaluation cost is rounding error.**

Captured 2026-05-11 on a 32-core / 64 GB Windows box, .NET 10.0.203, single-node
docker-compose stack. Aggregated 90 post-warmup sequential `AssuredIdentity`
runs (10 actions per run = 720 per-transaction validations).

| Metric | Value |
|---|---|
| End-to-end validation p50 | **11.98 ms** |
| End-to-end validation p99 | 12.05 ms (single observation) — max observed 38.90 ms |
| Chain section share of Total | **71%** (4.85 s of 6.79 s aggregate) |
| Crypto + schema share of Total | ~2% (signatures 92 ms, schema 116 ms, payload hash 35 ms) |
| Programmable-rule budget headroom | ~2 ms per validation before changing the headline |

Per-rule top spenders (90 runs × 720 evals):

| Rule | Total | p99 | Evaluations |
|---|---:|---:|---:|
| `VAL_CHAIN_DOCKET` | 2.60 s | 23.83 ms | 720 |
| `VAL_CHAIN_PREDECESSOR_LOOKUP` | 2.26 s | 19.40 ms | 720 |
| `VAL_BP_003` (route reachability) | 1.08 s | 7.45 ms | 450 |
| `VAL_CHAIN_FORK` | 693 ms | 10.60 ms | 360 |
| `VAL_SIG_VERIFY` | 89 ms | 444 µs | 720 |
| `VAL_SCHEMA_004` (JSON Schema) | 37 ms | 812 µs | 540 |
| `VAL_BP_002` (signer auth) | 3.6 ms | 50 µs | 360 |
| `VAL_BP_RESOLVE` | 1.6 ms | 417 µs | 540 |

Single-if rule families captured at section level only (Structure 1.5 ms,
Timing 0.88 ms, GovernanceRights 0.81 ms across all 720 evaluations). These
groups together account for less than 0.1% of validator time — wrapping them
with per-rule timers is not worth the instrumentation overhead.

### What this means for the programmable-rules thesis

1. **The headline budget is 2 ms per validation.** That's what programmable
   rules can spend on interpretation without making validation visibly slower
   to the user (10% of p50). Any design that exceeds this needs explicit
   justification.
2. **Chain access patterns matter 35× more than rule logic.** A change that
   reduces `VAL_CHAIN_DOCKET` + `VAL_CHAIN_PREDECESSOR_LOOKUP` by 10% buys
   ~1.2 ms — more headroom than the entire programmable rule overhead can
   afford. Cache the predecessor lookup or batch the docket fetch and the
   thesis's perf risk evaporates.
3. **Crypto is not a bottleneck.** `VAL_SIG_VERIFY` p99 is 444 µs — replacing
   it with a slower programmable-rule-driven crypto verifier still wouldn't
   crack the top three.
4. **VAL_BP_003 is the one rule worth re-examining first.** 7.45 ms p99 on
   only 450 evaluations means individual evaluations can be expensive
   (graph traversal scaling with blueprint size). Programmable rewrites of
   route-reachability should benchmark this rule specifically.

### Reproducibility

- Sequential 90 runs, 10 warmup discarded.
- Microbenchmarks (`bench/Sorcha.Validator.Benchmarks`) ran separately —
  confirmed zero-overhead-when-disabled claim (TimeRule_Empty_x16 with
  TelemetryEnabled=false: ~4.7 ns vs ~1062 ns enabled — 226× faster, but the
  enabled cost is 1 µs amortised over the 12 ms validation = 0.008%).
- Concurrent N=10 burst captured to `walkthrough-runs/AssuredIdentity/concurrent-N10.json`.
- Sweep wall time: ~2 h 39 min sequential + ~15 min concurrent + ~3 min microbench
  ≈ 3 h total.

### Reproducibility caveats not yet addressed in this run

- **`dotnet-counters` was skipped.** The `DOTNET_DiagnosticPorts=suspend=false`
  config in `docker-compose.benchmark.yml` does not actually prevent runtime
  startup suspension in .NET 10 — the validator hangs at boot when the diag
  socket is mounted. Working around this needs either an in-process counter
  collector or a different diagnostic-port mode. Telemetry-by-rule numbers
  above are accurate; allocation rate / GC pause numbers are NOT captured.
- **`ConstructionPermit` and `PayloadTests`** are not in this baseline.
  Their `run.ps1` param shapes (`-Scenario A/B/C/all` and `-FileSize/-Rounds`)
  differ from `AssuredIdentity`, and they need their own setup runs. The
  harness's walkthrough list is intentionally narrowed to AssuredIdentity for
  v1; expand once those harness paths are validated.
