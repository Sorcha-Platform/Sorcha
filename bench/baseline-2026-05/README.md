# Validator Performance Baseline — May 2026

**Status:** TOOLING-READY. Capture has not been run on a quiescent machine yet.
**Branch:** `bench/validator-baseline-2026-05`
**Goal:** Honest pre-thesis baseline of validator performance so the eventual
programmable-rules ([thesis](../../Project%20Sorcha/Validator2/2026-05-09-programmable-validation-thesis.md))
work has numbers to compare against — not opinion.

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

(Populated after the capture by `summarise.ps1`. Until then, this section
reads "TBD — capture not yet run.")

> TBD — capture not yet run.
