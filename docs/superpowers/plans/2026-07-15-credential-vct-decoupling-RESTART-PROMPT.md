# Restart prompt — execute the credential VCT decoupling

Paste everything below the line as your first message in a fresh Claude Code session at `C:\Projects\Sorcha`. It is self-contained: it names the skills to load, the memories that matter, the branch, the standards guardrails, and the execution method. The plan and design docs carry the detail; this prompt gets you into them correctly.

---

Execute the implementation plan at `docs/superpowers/plans/2026-07-15-credential-vct-decoupling.md` (design: `docs/superpowers/specs/2026-07-15-credential-vct-decoupling-design.md`).

## What this is
The Sorcha Wallet PWA verifier reports **"None of your credentials match this verifier's request for https://sorcha.dev/vc/assured-identity/v1"** even though the citizen holds an Assured Identity credential. Root cause: one field (`CredentialIssuanceConfig.CredentialType`) is overloaded to mean the SD-JWT `vct`, a legacy `type` claim, and the display label. The blueprint sets the bare name `AssuredIdentityCredential`; the verifier/matcher/tests expect the URI form. The fix decouples the three concerns, drops the non-standard `type` claim, converts **every** credential type to a canonical `VctUris` URI + authored `displayName`, and makes VCT matching case-sensitive (spec-conformant). Blueprints are sacrificial — the product owner authorised updating every one.

## Load these skills FIRST (before any code)
1. `superpowers:subagent-driven-development` — the execution method. Dispatch a fresh implementer subagent per task, review (spec-compliance + quality) between tasks, broad review at the end. Use a progress ledger.
2. `verifiable-credentials` — Sorcha's SD-JWT VC implementation, the `CredentialIssuer`/`CredentialVerifier`/`SdJwtService` types, the two trust rails, and the DID-anchoring three-address model. Read before touching issuance.
3. `sorcha-architecture` — Feature 135 (unified trust + DCQL), Feature 114/181 (citizen wallet, DCQL dialect). Load when touching credential verification/matching.
4. `blueprint-builder` — the `credentialIssuanceConfig` / `credentialRequirements` action fields you are converting across every blueprint.

## Standards guardrails (do NOT violate — verified against draft-ietf-oauth-sd-jwt-vc §3.2.2.1)
- `vct` is the **sole** type identifier; **there is no `type` claim** in SD-JWT VC. Do not write `claims["type"]`. Do not "keep it for safety."
- `vct` is a **case-sensitive** `StringOrURI` / Collision-Resistant Name; matching is **case-sensitive exact**. Do NOT make VCT matching case-insensitive — the blueprint-side matchers are being tightened *to* `Ordinal`, not loosened.
- VCT URIs are lowercase kebab-case: `https://sorcha.dev/vc/{type}/v1`.

## Project conventions that bite
- `dotnet test` takes ONE project and **`--filter` does not isolate tests** (Microsoft.Testing.Platform) — run the whole project and read `Failed: N, Passed: N` totals. Always `dotnet build` before `dotnet test` (stale DLLs → phantom fails). Record each project's baseline before editing.
- **Never `git add -A`.** The working tree carries the user's unrelated untracked work (storyboards, an E2E test file). Stage explicit paths only. Task 8 (blueprint conversion) is the highest risk here — list every path.
- **Never run two implementer subagents concurrently on this one checkout** — `git stash` is not file-scoped and will swallow the other agent's tree (this happened this session). One implementer at a time, or use worktree isolation.
- Every new file: `// SPDX-License-Identifier: MIT` / `// Copyright (c) 2026 Sorcha Contributors`, file-scoped namespace, test naming `MethodName_Scenario_ExpectedBehavior`, xUnit v3 + FluentAssertions.
- The PWA (`Sorcha.Wallet.Pwa`) is Blazor WASM — BCL only, no `Sorcha.Cryptography` reference.
- No EF migration is needed or wanted (`CredentialEntity.Type` is an existing column; only the value flowing into it changes).
- `claude-review` CI check fails ~30s on infra (token setup) on every PR right now — that's flaky-only red, not a real finding; the repo convention is to merge past it (admin merge) once the real checks (`build-and-test`, image builds) are green.

## Branch
Work on `feature/credential-vct-decoupling` — already created, the design + plan are committed on it (it branches from master at commit `3b620ab0`). Do NOT branch again. Confirm with `git rev-parse --abbrev-ref HEAD`.

## Deployment reality (for context, not part of the plan's code)
This ships to n1 as a Docker deploy (PWA + services load into the app at runtime — no app reinstall needed for the credential/verifier change). The user re-claims their credential afterwards (design §8: existing credentials carry the old bare-name vct and must be re-issued; no migration/alias). The native app is unaffected.

## The one guarantee to trust
Task 8's **parametrised conformance test** walks every blueprint under `demos/`, `walkthroughs/`, `blueprints/` and asserts each credential type's `vct` (issuance) and `type` (requirements) equal its `VctUris` constant. Write that test FIRST, let it fail with the full worklist, then convert until it's green. That test — not a hand-typed file list — is what proves the lockstep conversion missed nothing. If it passes and the full suites pass, the corpus is consistent.

Begin by loading the four skills, reading the plan and design docs, confirming the branch, then dispatch Task 1.
