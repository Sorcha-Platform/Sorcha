# Restart prompt — execute the device-bound credential re-issuance (#1195 Phase 2)

Paste everything below the line as your first message in a fresh Claude Code session at `C:\Projects\Sorcha`. It is self-contained: it names the skills to load, the memories that matter, the branch, the standards guardrails, and the execution method. The plan and design docs carry the detail; this prompt gets you into them correctly.

---

Execute the implementation plan at `docs/superpowers/plans/2026-07-16-device-bound-reissuance.md` (design: `docs/superpowers/specs/2026-07-16-device-bound-reissuance-design.md`). **Read both in full before any code.**

## What this is
#1195 **Phase 2**. Phase 1 (merged #1197) made the citizen *present* standards-cleanly (`cnf` = device key, OID4VP `direct_post`, no delegation) but assumed the AIAS application ran in the wallet PWA. It doesn't — **the Assured Identity is created in the web app, where there is no device key**. `cnf` is frozen at mint, so one credential can't be both web- and device-signable.

The model: **one assurance, two bindings.** The web-issued **root** credential stays holder-`cnf` (server-custody presentable, web/remote). A second AIAS **device-registration blueprint**, driven by a **"Bind to device" button on the wallet ID card**, presents the root (proving entitlement + supplying the claims) and mints a **device-`cnf`** copy bound to the phone's non-extractable P-256 key. A `DeviceBoundCredentialPolicy` caps this at **3 devices per user, evicting the oldest** via the Token Status List (F114) + F118 inbox notice. The wallet **selects which credential to present per surface** (device copy in person, root on the web). This is 8 tasks.

**First task is a correction:** revert the AIAS *apply* blueprint's starting field `sorcha-device-key` → `sorcha-holder-key` (Phase 1 put it there for the demo shortcut; the root must be holder-`cnf`).

## Load these skills FIRST (before any code)
1. `superpowers:subagent-driven-development` — the execution method. Fresh implementer subagent per task; two-stage review (spec-compliance + quality) between tasks; a progress ledger at `.superpowers/sdd/progress.md`. **One implementer at a time on this checkout.**
2. `verifiable-credentials` — Sorcha's SD-JWT VC implementation (`SdJwtService`, `cnf` binding, issuance), OID4VCI proof-of-possession, the Token Status List. Read before touching issuance or the policy.
3. `sorcha-architecture` — Feature 114 (citizen wallet, device registry, status-list publisher/worker, inbox writers), Feature 118 (inbox), Feature 137 (holder-key source field → `cnf`). Load for the policy + wallet surfaces.
4. `blueprint-builder` — the **Open Participants / late binding** and **credential-bootstrapped application** patterns (the device-registration blueprint uses both), `credentialIssuanceConfig` / `credentialRequirements`.

## Standards guardrails (do NOT violate)
- `cnf` is the SD-JWT VC holder-binding key and is **immutable after mint**. A device copy is a **re-issue** (AIAS re-signs), not an edit. Standard OID4VCI: the device proves possession of its key, the issuer binds it.
- **Delegation stays OFF every presentation path.** Phase 1 removed it; do not reintroduce it. Entitlement for the device copy is proven by *presenting the root*, not by a delegation JWT.
- The device copy's **identity claims come from the verified root presentation** (AIAS-signed, tamper-evident) — never from client-supplied payload. Request full disclosure of the root in the bind flow.
- `vct` stays the canonical URI `https://sorcha.dev/vc/assured-identity/v1`, case-sensitive (per #1187). Both credentials share it.
- Revocation = a **Token Status List** bit flip (reuse F114), not deletion.

## Project conventions that bite
- `dotnet test` takes ONE project and **`--filter` does not isolate** (Microsoft.Testing.Platform) — run the whole project, read `Failed: N, Passed: N`. Always `dotnet build` before `dotnet test`. Record each project's baseline before editing tests.
- **Never `git add -A`.** The tree carries the user's untracked work (`walkthroughs/_storyboards/`, `tests/Sorcha.UI.E2E.Tests/Docker/StoryboardWalkthroughTests.cs`, a modified `.gitignore`) that MUST stay untracked. Stage explicit paths only.
- **Never run two implementer subagents concurrently on this checkout** — `git stash` is not file-scoped and swallows the other agent's tree. One at a time, or worktree isolation.
- Every new `.cs`: `// SPDX-License-Identifier: MIT` / `// Copyright (c) 2026 Sorcha Contributors`, file-scoped namespace, test naming `MethodName_Scenario_ExpectedBehavior`, xUnit v3 + FluentAssertions, Moq.
- The PWA (`Sorcha.Wallet.Pwa`) is Blazor WASM — BCL only, no `Sorcha.Cryptography`/Newtonsoft; `JsonElement` not `JsonNode`. User feedback via `IInlineFeedback`, NEVER `ISnackbar` (CI gate).
- Components.User `RootNamespace` = `Sorcha.UI.Core` regardless of folder.
- `claude-review` CI fails ~28s on an infra token-exchange on every PR right now — **flaky-only red**, no findings; merge past it once the real checks (`build-and-test`, image builds) are green.
- The device key is phone-only (`IDeviceKeyService`, non-extractable P-256). Anything binding to it runs in the PWA; the `sorcha-device-key` control (shipped #1197) writes the device public JWK to a `/…/holderJwk` slot; `IDeviceKeyProvider` is the shared seam (PWA impl wins over the null default).

## Investigation points the plan anchors (confirm by grep before coding — the proven #1195 pattern)
- The exact `presentationSource` enum value for an internally-held Sorcha credential (Task 2).
- Where the mint distinguishes a device copy (P-256 `cnf`) from the web root (Ed25519 `cnf`), and whether it lives in Wallet.Service or Blueprint.Service (Tasks 4/5). Project memory says live issuance is the **Wallet Service direct-issue** path.
- The claim-source pointer prefix `/presentedCredential/*` — Task 3 defines it and threads verified-presentation claims into the issuance source doc; Task 2's `claimMappings` reuse it.
- The present-surface signal (in-person/offline vs web/remote) for Task 7's selection; default = prefer a signable device copy, fall back to the root.

## Branch
Work on `feature/device-bound-reissuance` — already created; the design + plan + this prompt are committed on it (branches from master at the #1197 merge). Do NOT branch again. Confirm with `git rev-parse --abbrev-ref HEAD`.

## Deployment reality (context, not code)
Ships to n1 as a Docker deploy: client images (`sorcha-wallet-pwa`, `sorcha-ui-web`) + `wallet-service` (the policy/mint). The mobile wallet loads the PWA remotely (`mobile/wallet/capacitor.config.json` `server.url = n1.sorcha.dev/wallet`), so a deploy reaches TestFlight/Play on next load — but Blazor WASM caches hard (force-close/reopen). Full E2E needs a phone: apply on web → **Bind to device** in the wallet → present in person → verify. Per-service recreate command + genesis notes are in the `n1-deploy` skill.

## Success = the four SCs in design §10
Web apply → holder-`cnf` root (server-custody presentable); "Bind to device" → device-`cnf` copy that standard-verifies in person with no delegation; ≤3 device copies with oldest-evicted + notified; wallet auto-selects the right credential per surface.

Begin by loading the four skills, reading the plan and design docs, confirming the branch, initialising the SDD ledger, then dispatch Task 1 (the apply-blueprint correction).
