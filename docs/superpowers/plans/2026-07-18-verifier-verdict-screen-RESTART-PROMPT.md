# Restart prompt — build the verifier verdict screen (both treatments) + `age_over_18` issuance

Paste everything below the line as your first message in a fresh Claude Code session at `C:\Projects\Sorcha`. It is self-contained: it names the skills to load, the memories that matter, the branch, the design, the standards guardrails, and the execution method.

---

Build the verifier verdict screen per the **approved design** at `docs/superpowers/specs/2026-07-18-verifier-verdict-screen-design.md`. **Read it in full first**, and open the visual mockup it links (the Artifact URL) — that mockup IS the visual spec for the two screens.

## What this is
The web verifier (`Sorcha.Verifier`, web `/verify`) today shows a bare **"Verification Complete"** and throws away everything the credential carries. The rich verdict UI already exists — `VerdictViewModel` + `VerdictTrailPanel.razor` (F155) — but is **wired into nothing (zero consumers)**. This work wires that shared component into **both** the web verifier and the PWA doorstep verifier, renders the two preset-adaptive treatments (Confirm identity / Age over 18?), and adds the one hard dependency the age screen needs: AIAS must issue an `age_over_18` boolean.

Approved design decisions on record: **portrait + verdict lead, four-layer trust trail a tap away** (progressive); **keep the photo on the age screen**; **wire both surfaces now** (one shared component, no divergence). Open refinements (portrait size, issuer logo vs name, "18+" numeral vs words, photo-free online age variant) are deferred — build the design as speced.

## Load these skills FIRST (before any code)
1. `superpowers:writing-plans` — the design is DONE; the next step is to turn it into an implementation plan. Produce the plan first.
2. `superpowers:subagent-driven-development` — then execute the plan task-by-task (fresh implementer per task, two-stage review, `.superpowers/sdd/progress.md` ledger). This is how the whole #1195 arc + P0 were executed this session.
3. `verifiable-credentials` — SD-JWT VC claims, issuance, the `age_over_18` derivation (EUDI/ISO 18013-5 `age_over_NN` pattern).
4. `sorcha-architecture` — F155 Open Verifier PWA (`VerdictViewModel`/`VerdictTrailPanel`, the four `ValidationLayer`s + `LayerStatus`), F135 unified trust, F127 SorchaWallet presentation.
5. `sorcha-ui` + `blazor` + `frontend-design` — the MudBlazor components, `Sorcha.UI.Components.User` conventions (RootNamespace `Sorcha.UI.Core` regardless of folder), the shared-component library rules.

## Scope (4 pieces — see design §4)
1. **Design/polish the shared `VerdictTrailPanel`** per the mockup — identity treatment, age treatment (preset-adaptive header + disclosure set), and the **fail / warn** states (warn = reduced-assurance offline path; never render as a plain pass).
2. **Wire it into `Sorcha.Verifier`** (web `/verify`) — replace the hardcoded success message at `src/Apps/Sorcha.Verifier/Components/Pages/Index.razor:~40` with the panel driven by the real `VerificationOutcome` → `VerdictViewModel`.
3. **Wire it into the PWA doorstep verifier** — the `RealVerifierEngine` result surface (`Sorcha.Wallet.Pwa`). Same component.
4. **AIAS issuance: add `age_over_18`** — derive the boolean from `dateOfBirth` at issue time. Without it the "Age over 18?" preset has no claim to match ("none of your credentials match").

## Investigation anchors (confirm by grep before coding — verified this session)
- Bare message today: `src/Apps/Sorcha.Verifier/Components/Pages/Index.razor` (~line 40, "Verification Complete / presented and verified successfully").
- Orphaned rich component: `src/Apps/Sorcha.UI/Sorcha.UI.Components.User/Components/Verify/VerdictTrailPanel.razor` + `Models/Verification/VerdictViewModel.cs` (carries `PortraitBase64`, `Disclosed` name→value pairs, `Withheld`, `IssuerDisplayName`, `AgeOver18`, `RegisterAnchorId`, the `Layers`). **`grep -rln VerdictTrailPanel` returned zero consumers** — confirm it's still orphaned, then wire it in.
- Presets: `src/Apps/Sorcha.UI/Sorcha.UI.Components.User/Services/User/Verification/DefaultPresetCatalogue.cs` — "Age over 18?" `RequiredClaims: [age_over_18, portrait]`; "Confirm identity" `RequiredClaims: [fullName, portrait]` + optional `dateOfBirth`.
- **`age_over_18` derivation — the key investigation.** The AIAS credential's claims (givenName/familyName/dateOfBirth/fullName/portrait) are set at issuance. Project memory: **live issuance = the Wallet Service direct-issue path, NOT the Engine CredentialIssuer** (see the `credential-vct-decoupling` memory — a plan already missed this once). Find where the issued claim set is assembled and add a derived `age_over_18` computed from `dateOfBirth`. The AIAS apply blueprint is `demos/AIAS/blueprints/aias-assured-identity.template.json` — decide whether the derivation belongs in issuance code (compute from DOB) or a blueprint claim mapping; issuance code is likely (a boolean derived from a date isn't a straight field copy). Existing credentials won't have it → re-claim needed to pick it up (fine — testing).
- The four-layer trail: `VerificationOutcome.Layers` (`ValidationLayer` { LivePresentation, IssuerSignature, Revocation, RegisterAnchor }, `LayerStatus` { Pass, Fail, Unverified }). RegisterAnchor is the on-demand/network layer (`IRegisterAnchorClient`).
- Warn path: `RealVerifierEngine` maps `Accepted + IssuerSignature==NotVerified` → `VerifyOutcome.Warn` (offline PWA, no issuer resolution). The warn treatment must be visually distinct.

## Standards / conventions that bite (all hit this session)
- **`dotnet test` takes ONE project; `--filter` does NOT isolate** (Microsoft.Testing.Platform) — run the whole project, read `Failed: N, Passed: N`; `dotnet build` before `dotnet test`; record each project's baseline before editing.
- **Never `git add -A`.** The tree carries the user's untracked work (`walkthroughs/_storyboards/`, `StoryboardWalkthroughTests.cs`, modified `.gitignore`) that MUST stay untracked. Stage explicit paths.
- License header (`// SPDX-License-Identifier: MIT` / `// Copyright (c) 2026 Sorcha Contributors`) + file-scoped namespace on every new `.cs`; `Sorcha.UI.Components.User` RootNamespace is `Sorcha.UI.Core`; test naming `MethodName_Scenario_ExpectedBehavior`; xUnit v3 + FluentAssertions + Moq (+ bUnit for components).
- PWA (`Sorcha.Wallet.Pwa`) is Blazor WASM — BCL only, `JsonElement` not `JsonNode`; user feedback via `IInlineFeedback`, never `ISnackbar` (CI gate).
- `claude-review` CI fails ~30s on an infra token-exchange on every PR — **flaky-only red**, no findings; merge past it once `build-and-test` + image builds are green. Branch protection blocks a PR that's **behind** master — update the branch (merge master in) before merging.

## Branch
Work on `feature/verifier-verdict-screen` — already created; the design doc + this prompt are committed on it (branch off master). Confirm with `git rev-parse --abbrev-ref HEAD`. Do NOT branch again.

## Deploy (context, after all green + reviewed)
Ships to n1 as a Docker deploy (use the `n1-deploy` skill; per-service `pull` + `up -d --force-recreate --no-deps <svc>`, no `down -v`): **`sorcha-verifier`** (the web verify screen + shared engine), **`sorcha-wallet-pwa`** + **`sorcha-ui-web`** (the shared `VerdictTrailPanel` in Components.User), and **`wallet-service`** if the `age_over_18` derivation lands in issuance. Then re-claim the AIAS credential to pick up `age_over_18` and run an age verification.

## Session state you're inheriting (all on master unless noted)
- **#1195 Phase 2 (device-bound re-issuance): MERGED (#1200) + deployed n1.** Two on-device fixes also merged+deployed (#1202: bind instance-id Guid `:N`→`:D` 404, and replaced-credential empty-vct).
- **P0 passkey-signup verification email: MERGED (#1203) + deployed** (tenant-service) — passkey signup now sends the verify email. Live-verify pending (a real passkey signup should receive it).
- **This verifier work: DESIGNED, not built.** Design committed on `feature/verifier-verdict-screen` (`2b703c63`).
- **Still open (from `aias-onboarding-test-findings` memory): P1** single-device companion-first UX (bounced to Safari, unscannable QR), **P2** web client shows `v2.0.7-dev` instead of unified version, **P3** credential-card disambiguation (two same-type cards look identical). Pick these up after the verifier work or as the user directs.
- The user's own n1 test account (`stuart@stuartfraser.net`, PlatformUser `82af8b37…`) was purged + re-signed-up via passkey, then **email admin-verified in the DB** to unblock (passkey signup hadn't verified it — that's what P0 fixed).

Begin by loading the skills, reading the design doc + mockup, confirming the branch, then use `superpowers:writing-plans` to produce the implementation plan, then execute it via `superpowers:subagent-driven-development`. Push ahead through all pieces without pausing for check-ins.
