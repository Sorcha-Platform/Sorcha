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
  milestone must nonetheless be independently demoable with a fallback (see §7).

---

## 2. The world

**Acme Identity Assurance Services (AIAS)** — a fictional, faintly self-important identity
assurance provider. Branding and copy lean into light humour (rejection reasons, the "AIAS is
reviewing your application" beat). AIAS is the org that runs the assurance and cyber workflows and
issues both credentials.

**Environment:** runs against a **freshly cleaned n1** (no Strathcarron / no prior demo orgs). All
provisioning (AIAS org, branding, blueprints, agents, presets) is scripted and repeatable (M5).

**Persona:** an anonymous attendee — referred to as **"Morag"** as a running placeholder; the live
demo can sign up an actual audience member. The verify story begins **once she holds the Assured
Identity VC** (that is Stage 0 for the verifier milestones).

---

## 3. The arc (end to end)

```
anonymous  ──signup (with photo)──▶  AIAS assurance gate  ──issue──▶  Assured Identity VC (carries the photo)
                                          │ autonomous agent                     │
                                          │ (Assure-ID mode)                     │ required to start
                                          ▼                                      ▼
                                     approve / reject                    Cyber questionnaire (5–10 Q)
                                     with a cheeky reason                        │ autonomous agent
                                                                                 │ (Cyber mode) scores
                                                                                 ▼
                                                                          AIAS Cyber Level VC
                                                                          (Fail/Bronze/Silver/Gold/Platinum)
                                                                                 │
                                                                                 ▼
                                              prove the level across 3 surfaces (holder = wallet)
                                              verdict shows the photo + selectively-disclosed level
```

---

## 4. Credential model (the spine)

**Two credentials.**

### 4.1 AIAS Assured Identity VC (base, with face)
- Issued after the AIAS assurance gate passes.
- Carries the **persona photo** (the portrait subfeature — see §6 risk).
- Reusable base identity; it is the **prerequisite to start the cyber questionnaire**.

### 4.2 AIAS Cyber Level VC (earned, the punchline)
- Issued after the questionnaire is scored.
- Carries a **`level`** claim derived from the score.
- **Selective disclosure**: the verify moments disclose *just the level* (and the photo comes from
  the base credential / persona), not the full questionnaire detail.

### 4.3 Level bands (locked)

| Score | Level | Credential issued? |
|------:|-------|--------------------|
| 100% | **Platinum** | yes |
| 85–99% | **Gold** | yes |
| 65–84% | **Silver** | yes |
| 50–64% | **Bronze** | yes |
| < 50% | **Fail** | no credential |

---

## 5. The autonomous agent(s)

Sorcha-agents, **autonomous** (apply rules, decide — no human-in-the-loop on stage). Generalizes
the prior AssuredIdentity demo's rules auto-approver.

Two **modes**, runnable independently (two agents, or one agent started in a chosen mode) so the
assurance flow and the cyber flow can each be exercised in isolation:

### 5.1 Assure-ID mode — the assurance gate
On a new signup application, automatically evaluate and decide approve/reject:

1. **Verified email** — table stakes (existing signup capability).
2. **Photo present** — a captured persona portrait exists.
3. **Address / postcode exists** — a real lookup against a public source (e.g. UK `postcodes.io`).
   Doubles as a humour hook: *"AIAS could not locate 'Hogwarts, Diagon Alley' on any map."*
4. **Profanity / "not too sweary" check** — submitted details pass a profanity filter; failures get
   a cheeky AIAS rejection reason.

Approve ⇒ issue the **Assured Identity VC**. Reject ⇒ a (funny, on-brand) rejection reason.

### 5.2 Cyber mode — the questionnaire scorer
On questionnaire submission, score the answers, map the percentage to a **level band** (§4.3), and
issue the **Cyber Level VC** (or record a Fail with no credential).

### 5.3 "Controllable / live" (M4)
The agent is the live, steerable element on stage — its control surface (start/stop, mode, and
visible decisioning) is its own milestone (M4). The exact control affordances are deferred to M4's
spec.

---

## 6. Known risk to investigate (not assume)

**The photo / persona-image subfeature.** The end-to-end photo path — capture at signup → store on
the persona → embed in the Assured Identity VC → render on the verifier verdict — is the one
"under-explored" area. Everything else is assembly of proven parts. M1's spec **must** begin by
establishing how far F092 (Consumer Persona) and F107 (x-file portrait token / id-cards) already
take us, and scope only the gap. The "photo on the verdict screen" is a strong conference visual and
should be protected as a deliverable.

---

## 7. Decomposition

Each milestone is independently demoable and gets its own spec → plan → build. Order is a
dependency order, not a rigid schedule.

| ID | Milestone | Status | Notes |
|----|-----------|--------|-------|
| **M0** | **Verifier fix** — authenticated HAIP transport into web/PWA/Verifier; surface real errors instead of "not configured" | **In progress** | Credential-agnostic; handed to prodexec (run `4e20c5fe7ddc`). Prerequisite for M3. |
| **M1** | **AIAS assurance + signup-with-photo** — org/branding, assurance gate rules, autonomous Assure-ID agent, Assured Identity VC with portrait | Not started | Begins with the §6 photo investigation. |
| **M2** | **Cyber questionnaire → leveled Cyber VC** — questionnaire workflow (gated on Assured ID VC), Cyber-mode scoring agent, leveled issuance | Not started | Questionnaire content + scoring detail in M2's spec. |
| **M3** | **Verify the level across 3 surfaces** — Verifier app (kiosk), web `/app` (online RP), PWA `/wallet` (peer/field check); verdict shows photo + selective level disclosure | Not started | Reuses M0. Holder is always the wallet. |
| **M4** | **Agent control surface** — the live, steerable stage element | Not started | Control affordances TBD in M4. |
| **M5** | **Repeatable conference timeline** — clean-n1 bootstrap + rehearsal script (Docker first, then n1) + per-stage fallbacks (incl. scripted-holder for flaky-wifi) | Not started | This is the "story that can be reliably repeated". |

### 7.1 Proof strategy (carried into M3 / M5)
- **Layer 1 — automated regression (Docker, CI gate):** per-surface Playwright tests asserting each
  verify surface reaches `qr-active` (never `not-configured`), plus one end-to-end protocol test
  driven by a **scripted holder** (Playwright dual-context with a real PWA wallet is a later
  upgrade).
- **Layer 2 — repeatable demo timeline (M5):** extend the existing `walkthroughs/` PowerShell
  pattern (the `CyberEssentialsUac` walkthrough already exercises HAIP verify) into a clean-n1
  bootstrap + rehearsal that prints a numbered step log + screenshots. Live demo uses a real
  phone/wallet; scripted-holder is the CI/fallback path.

### 7.2 The six verify checkpoints (per verify run, used by M3/M5)
0. A wallet holds the Cyber Level VC (precondition).
1. Verifier surface starts a session, renders the QR (`qr-active`, non-empty deep link).
2. `request-object` resolves and is correctly signed; `presentation_definition` matches the preset.
3. Holder presents `vp_token` (direct-post accepted).
4. Verifier polls → `Verified`, `vp_token` present.
5. Surface renders the verdict (photo + disclosed level).

---

## 8. Open items deferred to milestone specs
- **M2:** the actual 5–10 cyber-security questions, answer shape (weighting / multiple-choice), and
  the score→percentage mapping.
- **M2:** how the "must hold Assured ID VC to start" gate is enforced (present-the-VC vs.
  account-level check).
- **M4:** the agent control surface affordances (start/stop, mode switch, visible decisioning).
- **M1:** the precise assurance-rule thresholds and the rejection-copy tone.
- Persona naming / whether to sign up a live audience member.
