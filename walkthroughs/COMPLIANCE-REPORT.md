# Walkthrough Compliance Report

Run started: 2026-05-22 10:06:41Z | Generated: 2026-05-22 10:13:09Z
Post-deploy compliance for F136 (tiered-audience JWT). gateway = local Docker `:latest` (setup.ps1 then run.ps1).
AssuredIdentity verified **PASS** on n1 (end-to-end, SC-001 budget OK) in a separate run.

**5 passed, 4 failed, 0 timed out** of 9 completed (gateway).

| Walkthrough | Stage | Status | Duration | Exit | Notes |
|---|---|---|---|---|---|
| RegisterCreationFlow | run | **PASS** | 00:01 | 0 | Register ID: e87df5beee4d430c8a997ece85ce25e8 /   Steps: 2/2 passed /   Duration: 1.2s /   RESULT: PASS |
| WalletVerification | run | **PASS** | 00:01 | 0 | Steps: 4/4 passed /   Duration: 1s /   RESULT: PASS |
| FormCoverage | run | **PASS** | 01:19 | 0 | [i] Rounds passed : 5 / 5 / [i] Total duration: 78.82s / [i] Avg per round : 15.75s  (min 11.42s / max 31.03s) / [OK] All rounds passed |
| PayloadTests | run | **PASS** | 00:11 | 0 | ║  Rounds:      1                          ║ / ║  Steps:       7/7                      ║ / ║  Duration:    00:11.218                  ║ / ║  Status: ... |
| ConstructionPermit | run | **PASS** | 01:27 | 0 | [OK] Scenario C: Scenario C: Rejection (REJECTED, 3/3, 24.5s) /   Duration: 86s /   RESULT: PASS |
| ForestryCertification | run | **FAIL** | 00:01 | 1 | Invoke-RestMethod: Response status code does not indicate success: 401 (Unauthorized). |
| TradeFinance | setup | **FAIL(setup)** | 00:01 | 1 | Invoke-RestMethod: Response status code does not indicate success: 401 (Unauthorized). |
| SelfBuildHouse | run | **FAIL** | 02:30 | 1 | Planning: REFUSED (5/5), Warrant: N/A (-), 29.9s /   Duration: 148.4s /   RESULT: FAIL |
| DistributedRegister | run | **FAIL** | 00:32 | 1 | Register: be0dd18740504cd3884f646af4fb127e /   Steps: 2/4 /   Duration: 31.9s /   RESULT: FAIL |

