# AIAS Conference Demo — North-Star Design

**Date:** 2026-06-29
**Status:** North-star (program-level). Each milestone below gets its own spec → plan → implementation cycle.
**Owner:** Stuart

---

## 1. Purpose

A self-contained, repeatable **tech-conference demo** that shows Sorcha's end-to-end value in
one coherent, slightly tongue-in-cheek story: an anonymous person signs up, is **assured** by a
fictional provider, earns a **leveled** credential by taking a quiz, and then **proves that level**
across three different relying-party surfaces — all with a live, autonomous agent doing the
decisioning on stage.

This document fixes the **narrative, the credential model, the agent's role, and the
decomposition**. It is deliberately not an implementation plan; it is the spine every milestone
hangs off.

### Non-goals
- Real KYC / real identity assurance. The "assurance" here is plausible-looking theatre with a few
  genuinely-real checks, not a production identity-proofing pipeline.
- A fixed delivery date. There is no hard deadline; this is being designed ahead of need. Every
  milestone must nonetheless be independently demoable with a fallback (see §9).
- **Multi-credential ("additive") verify.** Root-solved as a ~L4/multi-week change spanning ~15–20
  files (single `vp_token`, single-SD-JWT validator, single-credential verdict). It is already on
  the roadmap as **spec 098** and is explicitly **out of scope** here — the demo verifies a single
  credential (see §4.4).

---

## 2. The world

**Acme Identity Assurance Services (AIAS)** — a fictional, faintly self-important identity
assurance provider. Branding and copy lean into light humour (rejection reasons, the "AIAS is
reviewing your application" beat). AIAS is the org that runs the assurance and cyber workflows and
issues both credentials.

**Environment:** runs against a **freshly cleaned n1** (no Strathcarron / no prior demo orgs).
Docker-first for build + proof; n1 is the rehearsal gate. All provisioning is scripted and
repeatable (§8).

**Persona:** an anonymous attendee — referred to as **"Morag"** as a running placeholder; the live
demo can sign up an actual audience member. The verify story begins **once she holds the Cyber
Level VC**.

---

## 3. The arc (end to end)

```
anonymous ─signup─▶ AIAS assurance gate ─issue─▶ Assured Identity VC (carries the photo)
                         │ autonomous agent                  │ present to start (gate)
                         │ (Assure-ID mode)                  ▼
                         ▼                            Cyber questionnaire (5–10 Q)
                    approve / reject                         │ autonomous agent (Cyber mode):
                    with a cheeky reason                     │  - scores answers
                                                             │  - rejects if presented Assured ID
                                                             │    has NO portrait
                                                             ▼
                                              AIAS Cyber Level VC  (level + portrait)
                                              portrait mapped from the presented Assured ID VC
                                                             │
                                                             ▼
                              prove across 3 surfaces (holder = wallet); single-credential verify;
                              verdict shows photo + selectively-disclosed level
```

---

## 4. Credential model (the spine)

**Two credentials issued; one credential verified.**

### 4.1 AIAS Assured Identity VC (base, with face)
- Issued after the AIAS assurance gate passes.
- Carries the **persona photo** (portrait claim, **optional** at application time — see §4.4).
- The **prerequisite to start the cyber questionnaire**: the cyber workflow's first action requires
  *presenting* this VC.

### 4.2 AIAS Cyber Level VC (earned, the punchline — and the only credential verified at the demo)
- Issued after the questionnaire is scored.
- Carries **both** a **`level`** claim (derived from the score) **and** the **`portrait`** claim,
  the latter **mapped from the presented Assured Identity VC** (present-and-map, §4.4).
- **Selective disclosure**: the verify moments disclose *just the level*; the portrait renders on the
  verdict via the existing single-credential portrait path.

### 4.3 Level bands (locked)

| Score | Level | Credential issued? |
|------:|-------|--------------------|
| 100% | **Platinum** | yes |
| 85–99% | **Gold** | yes |
| 65–84% | **Silver** | yes |
| 50–64% | **Bronze** | yes |
| < 50% | **Fail** | no credential |

### 4.4 Photo architecture (root-solved 2026-06-29)

Three investigations settled how the photo reaches the verdict:

- **Capture → embed → render all EXIST** (F107): `FileRenderer` (camera `capture="user"` *and*
  upload), `PhotoTokenResizer` → 240×320 JPEG ≤20KB token, `embedAs` → credential claim, and
  `VerdictTrailPanel` already paints a 70×88px portrait on the verdict. The "webcam-with-upload
  fallback" is the existing control behaviour — nothing to build.
- **Verify stays single-credential.** Multi-credential (present both VCs) is ~L4/multi-week and is
  deferred to spec 098 (§1). So the **Cyber Level VC carries the portrait itself** and the verify
  moment presents that one credential.
- **Present-and-map** gets the portrait into the Cyber VC: the cyber workflow's first action
  *requires presenting the Assured Identity VC* (the gate), and its disclosed `portrait` is mapped
  into the issued Cyber VC. This needs **one small engine enabler — F107 task T035** (capture a
  verified presentation's `VerifiedClaims` into workflow state under `/presentedCredentials/*` so
  `claimMappings` can reference them). ~200 LOC in `ActionExecutionService` + tests, 1–2 days, no
  schema change, low risk; the driving-licence walkthrough wants it too. **Lands in M2.**
- **Portrait optional on Assured ID, mandatory for cyber.** If the presented Assured Identity VC has
  no portrait, the **Cyber-mode agent rejects** — a real, visible on-stage rejection reason. The
  T035 enabler is what lets the agent *inspect* the presented claims to enforce this.
- **No persona persistence.** Not needed under this model, and it avoids storing a biometric at rest.

---

## 5. The autonomous agent(s)

Sorcha-agents, **autonomous** (apply rules, decide — no human-in-the-loop on stage). Reuses the
existing `Sorcha.Agent` CLI (dual SignalR + polling listeners, pluggable `RulesDecisionEngine`
[JSON Logic] / `AiDecisionEngine` [Claude]). **"Two agents" is one agent run in two modes / configs**
— no new agent code; the build is AIAS-specific rules + an external-check hook (§5.1).

### 5.1 Assure-ID mode — the assurance gate
On a new signup application, automatically decide approve/reject against:

1. **Verified email** — table stakes (existing signup capability).
2. **Photo present** — a captured persona portrait exists *(soft — portrait is optional here; it is
   the cyber stage that hard-requires it).*
3. **Address / postcode exists** — a real lookup against a public source (e.g. UK `postcodes.io`).
   Humour hook: *"AIAS could not locate 'Hogwarts, Diagon Alley' on any map."*
4. **Profanity / "not too sweary" check** — submitted details pass a profanity filter; failures get
   a cheeky AIAS rejection reason.

The postcode + profanity + email-verified checks need an **external-check hook** on the rules engine
(today it evaluates JSON Logic over the payload only) — the one genuine engine extension for M1.
Approve ⇒ issue the **Assured Identity VC**. Reject ⇒ a (funny, on-brand) reason via a real reject
route (the demo blueprint is always-approve today).

### 5.2 Cyber mode — the questionnaire scorer
On questionnaire submission: **reject if the presented Assured Identity VC carries no portrait**
(§4.4); otherwise score the answers, map the percentage to a **level band** (§4.3), and issue the
**Cyber Level VC** (level + mapped portrait) — or record a Fail with no credential.

### 5.3 "Controllable / live" (M4)
The agent is the live, steerable element on stage — its control surface (start/stop, mode, and
visible decisioning) is its own milestone (M4). The exact control affordances are deferred to M4's
spec.

---

## 6. Risk register (root-solved)

The one "under-explored" area — the photo path — was investigated and **resolved** (§4.4): capture,
embed, and verdict-render all exist; the only new code is the small F107-T035 present-and-map
enabler in M2. Remaining open items are ordinary milestone-spec detail (§10), not risks.

---

## 7. Decomposition

Each milestone is independently demoable and gets its own spec → plan → build. Order is a
dependency order, not a rigid schedule.

| ID | Milestone | Status | Notes |
|----|-----------|--------|-------|
| **M0** | **Verifier fix** — authenticated HAIP transport into web/PWA/Verifier; surface real errors instead of "not configured" | **In progress** | Credential-agnostic; handed to prodexec (run `4e20c5fe7ddc`). Prerequisite for M3. |
| **M1** | **AIAS assurance + signup-with-photo** — org + (hardcoded) branding, assurance gate rules + external-check hook, autonomous Assure-ID agent with a real reject route, Assured Identity VC with portrait | Not started | Photo capture/embed already exist (F107). Lean — mostly assembly + the rules-engine external-check hook. |
| **M2** | **Cyber questionnaire → leveled Cyber VC** — questionnaire workflow gated on *presenting* the Assured ID VC; **F107-T035 present-and-map enabler**; Cyber-mode scoring agent (reject if no portrait); leveled issuance carrying level + mapped portrait | Not started | Questionnaire content + scoring detail in M2's spec. T035 (~200 LOC) is the only new engine code. |
| **M3** | **Verify the level across 3 surfaces** — Verifier app (kiosk), web `/app` (online RP), PWA `/wallet` (peer/field check); **single-credential** verify of the Cyber VC; verdict shows photo + selective level disclosure | Not started | Reuses M0. No multi-credential work (deferred to spec 098). Holder is always the wallet. |
| **M4** | **Agent control surface** — the live, steerable stage element | Not started | Control affordances TBD in M4. |
| **M5** | **Repeatable conference timeline** — consolidate the per-milestone provisioning into one clean-n1 bootstrap + rehearsal + per-stage fallbacks (incl. scripted-holder for flaky wifi) | Not started | See §8 — provisioning is built incrementally, M5 consolidates + rehearses. |

### 7.1 Proof strategy (carried into M3 / M5)
- **Layer 1 — automated regression (Docker, CI gate):** per-surface Playwright tests asserting each
  verify surface reaches `qr-active` (never `not-configured`), plus one end-to-end protocol test
  driven by a **scripted holder** (Playwright dual-context with a real PWA wallet is a later
  upgrade).
- **Layer 2 — repeatable demo timeline (M5):** extend the `walkthroughs/` PowerShell pattern (the
  `CyberEssentialsUac` walkthrough already exercises HAIP verify) into a clean-n1 bootstrap +
  rehearsal that prints a numbered step log + screenshots. Live demo uses a real phone/wallet;
  scripted-holder is the CI/fallback path.

### 7.2 The six verify checkpoints (per verify run, used by M3/M5)
0. A wallet holds the Cyber Level VC (precondition).
1. Verifier surface starts a session, renders the QR (`qr-active`, non-empty deep link).
2. `request-object` resolves and is correctly signed; `presentation_definition` matches the preset.
3. Holder presents `vp_token` (direct-post accepted).
4. Verifier polls → `Verified`, `vp_token` present.
5. Surface renders the verdict (photo + disclosed level).

---

## 8. Cross-cutting principle: repeatable, reboot-proof provisioning

The network **will** be wiped and rebuilt; setup must be **"re-run one script,"** not a manual
ceremony. Therefore **every milestone extends a single idempotent provisioning module** (the proven
`demos/AssuredIdentity/*.psm1` + `walkthroughs/` pattern, aligned with the `network-bootstrap` and
`walkthrough-builder` skills) that rebuilds the *entire* AIAS demo from a clean network — org +
branding + blueprint(s) + agent config(s) + verify presets — Docker-first, then n1. Provisioning is
**not** deferred to M5; M5 only consolidates and rehearses what each milestone already contributed.
Each milestone's "done" includes its slice of the bootstrap + a test/rehearsal hook.

---

## 9. Demo robustness

No hard date, but every milestone stays independently demoable and every live step has a fallback —
most importantly a **scripted-holder** path so a flaky-wifi phone never blocks the verify moment on
stage.

---

## 10. Open items deferred to milestone specs
- **M2:** the actual 5–10 cyber-security questions, answer shape (weighting / multiple-choice), and
  the score→percentage mapping.
- **M2:** exact JSON Pointer shape for `/presentedCredentials/*` and the claim-mapping wiring (T035).
- **M4:** the agent control surface affordances (start/stop, mode switch, visible decisioning).
- **M1:** precise assurance-rule thresholds, the external-check hook contract, and rejection-copy tone.
- Persona naming / whether to sign up a live audience member.
