# Feature 106 — Deferred E2E tests (T047 / T048)

**Status**: deferred from Wave F to a follow-up session that has a running
Sorcha docker stack available.

Wave F shipped the highest-value thin slice: the
`InboundCredentialDetectorShapeTests` fixture in
`tests/Sorcha.Wallet.Service.Tests/Services/InboundCredentialDetectorShapeTests.cs`.
This locks in the write/read contract between the Wave A engine branch and
the Wave B detector so any future drift surfaces as a fast unit failure
rather than a mysterious Playwright timeout 30 seconds later.

The following two scenarios still need to run against the real stack before
the MVP can be signed off. Both are blocked on having `docker-compose up -d`
healthy and the feature branch deployed on at least one node (n1 or local).

## T047 — Playwright single-node happy path with 30-second wall-clock assertion

**Goal**: SC-002 — the credential arrives in the holder's PENDING tab within
30 seconds of the issuer's approval transaction sealing.

**Shape** (mirrors `CouncilCredentialFlowTests` + `HaipWalkthroughScreenshotTests`
in `tests/Sorcha.UI.E2E.Tests/Docker/`):

1. Run `walkthroughs/HaipVerifiedCitizen/setup.ps1` to create the issuer org
   + publish the blueprint.
2. Register a public user via Playwright, create a wallet, submit the
   Verified Citizen application through the wizard, sign out.
3. Sign in as the government assessor, call the Action 2 execute endpoint
   via CLI (not UI — we're measuring the Wave A → Wave B latency, not UI
   click overhead).
4. **Start a `Stopwatch`** immediately after the Action 2 execute POST
   returns a 200.
5. Sign back in as the citizen, poll the MyCredentials PENDING tab every
   500ms via `page.WaitForSelectorAsync`. Record the elapsed time when the
   pending credential card appears.
6. Assert `stopwatch.Elapsed.TotalSeconds <= 30` (SC-002).
7. Run 5 iterations, compute the 95th percentile, assert P95 <= 30s.

**Dependencies**:
- Existing `MultiUserTestBase` + page object framework
- Running `docker-compose up -d` stack
- Feature 106 branch deployed (all waves merged)

## T048 — FR-017 signature verification integration test

**Goal**: the accept/decline register transaction submitted by the holder is
signed by the holder's wallet key, and the issuer's side verifies the
signature before transitioning the instance to Completed / Rejected.

**Shape**:

1. Arrange: minted SorchaLocalWallet credential in PendingAcceptance state.
2. Act: client calls the PATCH endpoint (Active or Declined).
3. Assert: the resulting register transaction's `Signature` field verifies
   against the holder wallet's public key via
   `IWalletUtilities.VerifySignature` — fetch the sealed transaction from
   the register, extract the SigningData, and verify in the test.
4. Negative: tamper the transaction bytes, assert VerifySignature returns
   false.

**Dependencies**:
- Running Wallet Service + Register Service (Validator Service)
- Client code path for the parallel Action 3 execute (deferred from Wave E
  per its commit message — SC-003 still TBD)

## Why deferred

Both tests are thick integration tests that exercise code paths across 4+
services and require the live stack. Writing them blind (without being able
to run them) produces code that *looks* right but never actually passes —
and the time spent debugging them after the fact typically exceeds the
time to write them from scratch with the stack running.

The Wave F round-trip shape tests cover the single highest-risk drift
(writer ↔ reader contract mismatch) as a fast unit test. Everything else
in Wave F is architecturally in place and ready for a live run in a
dedicated session.

## Pickup checklist (for the follow-up session)

- [ ] `docker-compose up -d` and verify all 13 containers healthy
- [ ] `git checkout 106-register-native-credentials`
- [ ] Rebuild docker images from the feature branch
- [ ] Deploy to n1 via `scripts/n1-deploy.ps1` (see `network-bootstrap` skill)
- [ ] Write T047 as a new file
  `tests/Sorcha.UI.E2E.Tests/Docker/Feature106HolderFlowTests.cs` following
  the `CouncilCredentialFlowTests` structure
- [ ] Write T048 as an integration test in
  `tests/Sorcha.Wallet.Service.Tests/Feature106AcceptSignatureTests.cs` (or
  wherever the current signature-verification test lives)
- [ ] Run both, capture output, drop screenshots into
  `.planning/debug-trace/106-wave-f/`
- [ ] Commit + PR
