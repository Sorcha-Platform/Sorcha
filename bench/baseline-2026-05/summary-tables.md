# Validator Baseline — Top Findings

Generated: 2026-05-11 21:58:40Z
Sequential snapshots aggregated: 90

## Top 10 rules by total time spent


Code                         Evaluations Emissions TotalTime MaxTime
----                         ----------- --------- --------- -------
VAL_CHAIN_DOCKET                     720         0 2.60 s    23.83 ms
VAL_CHAIN_PREDECESSOR_LOOKUP         720         0 2.26 s    19.40 ms
VAL_BP_003                           450         0 1.08 s    7.45 ms
VAL_CHAIN_FORK                       360         0 693.07 ms 10.60 ms
VAL_SIG_VERIFY                       720         0 89.42 ms  443.95 µs
VAL_SCHEMA_004                       540         0 36.65 ms  811.81 µs
VAL_BP_002                           360         0 3.56 ms   49.59 µs
VAL_BP_RESOLVE                       540         0 1.56 ms   417.30 µs


## Top 10 rules by p99 latency


Code                         MaxP99    Evaluations MaxObserved
----                         ------    ----------- -----------
VAL_CHAIN_DOCKET             23.83 ms          720 23.83 ms
VAL_CHAIN_PREDECESSOR_LOOKUP 19.40 ms          720 19.40 ms
VAL_CHAIN_FORK               10.60 ms          360 10.60 ms
VAL_BP_003                   7.45 ms           450 7.45 ms
VAL_SCHEMA_004               811.81 µs         540 811.81 µs
VAL_SIG_VERIFY               443.95 µs         720 443.95 µs
VAL_BP_RESOLVE               417.30 µs         540 417.30 µs
VAL_BP_002                   49.59 µs          360 49.59 µs


## Section breakdown


Section              Calls TotalTime MaxP99    MaxObserved
-------              ----- --------- ------    -----------
Total                  720 6.79 s    38.90 ms  38.90 ms
Chain                  720 4.85 s    26.10 ms  26.10 ms
BlueprintConformance   720 1.09 s    7.47 ms   7.47 ms
SequenceReplay         540 581.27 ms 31.29 ms  31.29 ms
Schema                 720 115.62 ms 1.63 ms   1.63 ms
Signatures             720 92.34 ms  454.56 µs 454.56 µs
PayloadHash            720 35.06 ms  500.41 µs 500.41 µs
CryptoPolicy           720 2.87 ms   23.75 µs  23.75 µs
FileReferences         720 2.65 ms   27.16 µs  27.16 µs
Structure              720 1.53 ms   32.44 µs  32.44 µs
Timing                 720 875.10 µs 25.57 µs  25.57 µs
GovernanceRights       720 808.89 µs 19.64 µs  19.64 µs


## Per-walkthrough end-to-end validation latency (median of per-run percentiles)


Walkthrough     Runs MedianP50 MedianP95 MedianP99
-----------     ---- --------- --------- ---------
AssuredIdentity   90 11.98 ms  12.05 ms  12.05 ms



