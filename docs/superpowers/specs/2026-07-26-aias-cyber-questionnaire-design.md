# AIAS Cyber Questionnaire → leveled Cyber Level VC (M2) — design

**Date:** 2026-07-26
**Status:** Design agreed, ready for implementation planning
**Milestone:** M2 of the AIAS conference demo arc
**North-star:** `docs/superpowers/specs/2026-06-29-aias-conference-demo-design.md`

---

## 1. What this builds

The second credential beat of the AIAS conference demo. Morag already holds an **Assured
Identity VC** (M1, shipped and live on n1). She now presents it to start a short cyber-hygiene
questionnaire; an autonomous agent scores her answers into a band and issues an **AIAS Cyber
Level VC** carrying that level plus the portrait mapped forward from the credential she just
presented. M3 then verifies that one credential across three surfaces.

The questionnaire is deliberately light — "mostly for fun" — but it must produce a genuine
**spread** across the four bands rather than handing everyone Platinum.

### Already built, and load-bearing here

The north-star listed **present-and-map (F107 T035)** as the only new engine code M2 needed.
It shipped under Feature 174 / #1195 Phase 2 and is in production use:

- `StateReconstructionService` (~line 117) folds a sealed `PresentationOutcome` success tx's
  verified claims into the gated action's reconstructed data under the reserved
  `presentedCredential` key.
- `ActionExecutionService` (~line 684) exposes them as the `/presentedCredential/*` claim-source
  prefix, with a fail-closed strip so a client cannot smuggle a spoofed `presentedCredential`
  field through a submitted payload.

Note the pointer is singular `/presentedCredential/*`, not the `/presentedCredentials/*` the
north-star guessed at in §10. `demos/AIAS/blueprints/aias-device-registration.template.json` is
a working precedent for the whole pattern — presentation gate on action 1, eight claims mapped
from `/presentedCredential/*` on action 2.

### Goals

- A questionnaire that separates people across Bronze / Silver / Gold / Platinum.
- A leveled credential carrying `level` + `portrait`, selectively disclosable.
- Two visibly distinct rejections: score below 50%, and no portrait on the presented credential.
- Scoring that can be **retuned between rehearsals** without editing five copies of an expression.

### Non-goals

- Multi-credential presentation (deferred to spec 098 — verify stays single-credential).
- Agent control surface / steerability (M4).
- The three verify surfaces (M3).
- Reusing `walkthroughs/CyberEssentialsUac/` content. That walkthrough is an **organisational**
  posture assessment (assessor → company: device counts, admin accounts, revocation SLAs) with
  pass/fail auto-fail triggers. This is **personal** and **banded**. Useful as a tone reference
  only.

---

## 2. Architecture

Four workstreams. Only the fourth is demo-specific; the first three are platform capabilities
this demo is the first consumer of.

| # | Workstream | Where |
|---|---|---|
| 1 | `Slider` form control | `Sorcha.Blueprint.Models`, `Sorcha.UI.Components.User` |
| 2 | `OptionalClaims` on `CredentialRequirement` | `Sorcha.Blueprint.Models`, `Sorcha.Blueprint.Service` |
| 3 | Numeric external-check facts + `scored-questionnaire` check | `Sorcha.Agent` |
| 4 | Demo assets — new Cyber register, blueprint, agent config trio, provisioning, rehearsal | `demos/AIAS/` |

1–3 are independent of each other and can land in any order. 4 depends on all three.

---

## 3. The questionnaire

Eight questions, 24 points. Six `Selection` (0–3 each), two `Slider` (0–3 each).

| # | Question | 3 pts | 2 pts | 1 pt | 0 pts |
|---|---|---|---|---|---|
| 1 | How do you keep track of your passwords? | A password manager | Saved in my browser | A notebook by the desk | The same one everywhere, and hope |
| 2 | Is there a second step when you sign in to your email? | Yes — an app or a hardware key | Yes — a code by text message | Only when it nags me | No, just the password |
| 3 | How often do you change your passwords? | Only when I think one's been exposed | Once a year, whether it needs it or not | **Every 30 days, like clockwork** | Change them? |
| 4 | Your phone offers an update. What happens? | It installs automatically | I install it within a few days | "Remind me later", repeatedly | A red badge I've ignored since spring |
| 5 | An email says your bank account is locked. What do you do? | Ignore it and open the bank's app myself | — | **Check the sender address carefully** *(1)* / **Hover the link first** *(1)* | Click it — it looked legitimate |
| 6 | If your laptop vanished this afternoon, what would you lose? | Nothing — it backs up automatically | A day's work at most | I copied things to a USB stick once | Everything. Please don't say that |
| 7 | *(slider 0–10)* Accounts sharing a password | 0 | 1–2 | 3–5 | 6+ |
| 8 | *(slider 0–10)* People who know your streaming password | 0–1 | 2–3 | 4–6 | 7+ |

### The two traps

These are what produce the spread, and they are the comedic beat.

- **Q3** punishes calendar rotation. It *sounds* diligent; NCSC guidance has advised against it
  for years.
- **Q5** is sharper: both "careful" answers score 1, because inspecting a sender address or
  hovering a link is unreliable advice against a competent spoof. The only robust answer is
  out-of-band verification. Q5 has four options with two scoring 1 — deliberately asymmetric.

A confident, security-aware attendee loses points on both. That is the intent.

### Bands

These land exactly on the north-star's locked percentages (§4.3) with no rounding fudge.

| Points | % | Level | Credential |
|---:|---:|---|---|
| 24 | 100% | Platinum | ✓ |
| 21–23 | 87–96% | Gold | ✓ |
| 16–20 | 67–83% | Silver | ✓ |
| 12–15 | 50–63% | Bronze | ✓ |
| < 12 | < 50% | **Fail** | ✗ |

Answering honestly and reasonably well lands Silver or Gold. Platinum requires a perfect card
including both traps, so it stays rare without being unreachable.

---

## 4. Workstream 1 — the `Slider` control

A general-purpose control alongside the others in `Components/Forms/Controls/`, not a
demo-special one.

### Schema shape

```jsonc
"sharedPasswordCount": {
  "type": "integer",
  "title": "How many of your accounts share a password?",
  "minimum": 0,
  "maximum": 10,
  "x-slider": { "step": 1, "minLabel": "None", "maxLabel": "10 or more" }
}
```

**Dispatch is opt-in via `x-slider` presence** — never inferred from `type: integer` alone,
which would silently convert every existing numeric field in every blueprint into a slider.

**Range comes from standard `minimum`/`maximum`, not from inside `x-slider`.** They are real
JSON Schema keywords, so the validator enforces the range server-side and a hand-crafted
submission cannot post 9999. `x-slider` carries only what JSON Schema has no word for.
`x-*` keywords are already stripped before schema evaluation by
`ValidationEngine.StripXPrefixedKeysRecursive` and the mirrored engine `SchemaValidator.StripInPlace`,
so the extension is tolerated on both validation paths.

### Touch points

Follows the `x-address-lookup` → `PostcodeLookup` precedent exactly.

| File | Change |
|---|---|
| `src/Common/Sorcha.Blueprint.Models/Control.cs:124` | Add `Slider` to `ControlTypes`, **appended last** so no existing ordinal shifts |
| `FormSchemaService.InferControlFromSchema` | New branch on `x-slider`, placed **before** the `"number" or "integer" => Numeric` fallback |
| `Components/Forms/Controls/SliderRenderer.razor` | New — `MudSlider`, current value shown, end labels rendered |
| `Components/Forms/ControlDispatcher.razor` | New `case ControlTypes.Slider` |

### Renderer behaviour

- Writes an **integer** into `FormContext`, never a string. The scoring check compares
  numerically; a stringly-typed `"3"` would silently fail every band test.
- Seeds from `minimum` when the field is absent, rather than defaulting to 0 when 0 is outside
  the declared range.
- Stays keyboard-operable — `MudSlider` gives arrow-key stepping for free, and the demo should
  not ship a control usable only with a mouse.

---

## 5. Workstream 2 — `OptionalClaims` on `CredentialRequirement`

### The gap

`CredentialRequirement` exposes `RequiredClaims` only.
`RequirementDcqlMapper:54` maps just those into the ask:

```csharp
var requiredClaims = req.RequiredClaims?.Select(c => c.ClaimName).ToList() ?? [];
asks.Add(DcqlCredentialAsk.SdJwt(id, req.Type, requiredClaims));
```

`DcqlCredentialAsk.SdJwt(...)` leaves `OptionalClaims` null, so a blueprint author has no way to
request a claim the holder may withhold — even though `DcqlRequestBuilder:103` already consumes
`ask.OptionalClaims` and the DCQL dialect supports it.

### Why it blocks the portrait reject

Put `portrait` in `requiredClaims` and a portrait-less Assured Identity fails the OID4VP gate
itself, producing a generic protocol error. The agent never sees the presentation, so the
on-brand AIAS rejection never fires. Requesting `portrait` as **optional** is what lets a
portrait-less credential satisfy the gate and reach the agent, which then rejects it with a
reason a human can read.

### The change

1. Add `OptionalClaims` (`IEnumerable<ClaimConstraint>?`) to `CredentialRequirement`, mirroring
   `RequiredClaims`.
2. Thread it through `RequirementDcqlMapper` into the existing `DcqlCredentialAsk.SdJwt`
   optional parameter.
3. Verification is unaffected — an optional claim that is absent must not fail the presentation.

Purely additive: existing blueprints omit the property and behave identically.

---

## 6. Workstream 3 — agent scoring

### Numeric check facts

`ExternalCheckResult` today is `(string Name, bool Value, string? Detail = null)` — strictly
boolean. Widen it with an **optional trailing** parameter:

```csharp
public sealed record ExternalCheckResult(
    string Name, bool Value, string? Detail = null, double? Numeric = null);
```

Because the new parameter is optional and last, the four existing checks
(`EmailVerifiedCheck`, `FieldPresentCheck`, `PostcodeExistsCheck`, `ProfanityCheck`) and their
tests compile unchanged. The change is confined to the record, the runner's merge line, and the
new check.

`ExternalCheckRunner` merges numerically when present, boolean otherwise:

```csharp
merged[result.Name] = result.Numeric.HasValue
    ? JsonValue.Create(result.Numeric.Value)
    : JsonValue.Create(result.Value);
```

### The `scored-questionnaire` check

Reads a declarative table from `cyber.checks.json` and emits the total under `checks.cyberScore`.
Two sections, because Selection and Slider score differently:

```jsonc
{
  "checks": [
    { "name": "portraitPresent", "type": "field-present",
      "field": "/presentedCredential/portrait" },

    { "name": "cyberScore", "type": "scored-questionnaire",
      "answers": {
        "/passwordStorage": {
          "A password manager": 3,
          "Saved in my browser": 2,
          "A notebook by the desk": 1,
          "The same one everywhere, and hope": 0
        }
      },
      "ranges": {
        "/sharedPasswordCount": [
          { "max": 0, "points": 3 }, { "max": 2, "points": 2 },
          { "max": 5, "points": 1 }, { "points": 0 }
        ]
      } }
  ]
}
```

`answers` maps an exact answer string to points. `ranges` is an **ordered** list evaluated
top-down, where `max` is an **inclusive** upper bound — the first entry whose `max` is greater
than or equal to the submitted value wins, and an entry with no `max` is the catch-all. So
`{ "max": 2, "points": 2 }` matches values 1 and 2 given the preceding entry consumed 0. The
score is the sum across both sections.

Authoring the table once, beside the questions, is the whole point — **retuning the spread
after a rehearsal is a one-number edit.**

### No inevaluable state, by construction

All eight questions are `required` in the schema, so the validator guarantees all eight answers
are present before the agent sees the payload. An unrecognised answer string scores 0. There is
no sentinel value and no "could not score" branch.

If the check hard-faults, `ExternalCheckRunner`'s existing containment resolves it to boolean
`false`, which JSON Logic coerces to 0 — landing in the Fail band. Fail-closed, and in the right
direction: a broken scorer issues no credential.

### `cyber.rules.json`

Ordered, first-match-wins — the existing `assure-id.rules.json` idiom. The portrait pre-check is
first so it short-circuits before scoring.

| Order | Condition | Payload |
|---|---|---|
| 1 | `checks.portraitPresent == false` | `decision: rejected`, `reasonCode: no-portrait` |
| 2 | `checks.cyberScore < 12` | `decision: rejected`, `reasonCode: cyber-fail` |
| 3 | `checks.cyberScore < 16` | `decision: approved`, `level: "Bronze"` |
| 4 | `checks.cyberScore < 21` | `decision: approved`, `level: "Silver"` |
| 5 | `checks.cyberScore < 24` | `decision: approved`, `level: "Gold"` |
| 6 | always | `decision: approved`, `level: "Platinum"` |

Each rule's `payload` is static, which is exactly what `RulesDecisionEngine` supports — it
copies key/value pairs verbatim, so `level` rides along as an ordinary payload field that the
issuance config maps into the credential.

**Why not score inline in JSON Logic.** `Json.Logic` 6.1.0 supports arithmetic, so scoring in
the rules is possible with no code. But JSON Logic has no variable binding, so the eight-question
weighted sum would have to be duplicated verbatim into all five band conditions. Retuning one
question's points would mean editing the same expression in five places, and getting it subtly
different in one produces a scoring bug no test would obviously catch — punishing exactly the
activity this design exists to support.

---

## 7. Workstream 4 — demo assets

### Register topology — the questionnaire gets its own register

The cyber workflow is published to a **new, separate register**, not the AIAS Identity register
that hosts the assurance and device-binding workflows.

| | AIAS Identity register | AIAS Cyber register (new) |
|---|---|---|
| Hosts | `aias-assured-identity`, `aias-device-registration` | `aias-cyber-level` |
| Owner | AIAS issuer wallet | AIAS issuer wallet (same) |
| Agent | Assure-ID mode | Cyber mode |

Consequences worth stating, because several are load-bearing:

- **The agent config becomes cleanly separated.** The Assure-ID agent config is register-scoped
  and carries no blueprint id, so it auto-services any new blueprint on its register. Two
  registers means the Cyber agent services only cyber submissions and the two agents cannot
  pick up each other's work — which is a real improvement over two modes sharing a register.
- **Presentation crosses registers, and that is fine.** Morag presents an Assured Identity VC
  issued on the Identity register into a workflow on the Cyber register. Presentation trust is
  anchored on the issuer signature and DID resolution, not register membership; the F111
  lifecycle txs (`PresentationInitiated` / `PresentationOutcome`) are written to the register
  hosting the *originating action*, i.e. the Cyber register. Nothing in the gate consults the
  credential's originating register. **Verify in the first integration run** — this is the one
  assumption the separation introduces.
- **Register naming: 38-character limit.** `RegisterCreationOrchestrator.ValidateControlRecord`
  caps the name at 38 characters, and exceeding it fails in *finalize* — which previously
  surfaced as an unexplained 90-second seal timeout, not a clean error. `Acme Cyber Assurance`
  (20) is safely inside it.
- **The validator schema-registry collision largely goes away.** The registry is process-global,
  so a separate register does not by itself prevent it — but the questionnaire schema inlines
  none of the shared core primitives (`PersonName.v1`, `DateOfBirth.v1`, `PostalAddress.v1`)
  that caused the collision, so there are no duplicate `$id`s to clash. Republishing the cyber
  blueprint should not trip `VAL_SCHEMA_005`.
- **`state.json` gains a second register id**, alongside the existing `blueprintIds` map.

### Blueprint: `demos/AIAS/blueprints/aias-cyber-level.template.json`

Two actions, mirroring the proven device-registration shape.

**Action 1 — "Your cyber health check"** (citizen, `isStartingAction: true`)

- `credentialRequirements`: type `https://sorcha.dev/vc/assured-identity/v1`,
  `presentationSource: "SorchaWallet"`, `requiredClaims` = `givenName`, `familyName`;
  **`optionalClaims` = `portrait`** (workstream 2).
- `dataSchemas`: the eight questions, all `required`. Six `enum` (→ `Selection`), two
  `integer` + `x-slider` (→ `Slider`).
- `disclosures`: `/*` to both `citizen` and `aias-analyst` — the wildcard that carries
  `presentedCredential` through `ActionDisclosureResolver`'s clamp to the agent.

**Action 2 — "AIAS scores your answers"** (`aias-analyst`, `requiredPriorActions: [1]`)

- Agent submits `{ decision, level, reasonCode, verificationNotes }`.
- `credentialIssuanceConfig`:
  - `credentialType: "CyberLevelCredential"`,
    `vct: "https://sorcha.dev/vc/cyber-level/v1"`, `displayName: "AIAS Cyber Level"`
  - `targetAudience: "SorchaLocalWallet"`, `recipientParticipantId: "citizen"`
  - `issuanceCondition: { "==": [ { "var": "decision" }, "approved" ] }` — the F176 gate that
    stops a rejection minting a credential
  - `claimMappings`: `level` ← `/level`; `portrait` ← `/presentedCredential/portrait`;
    plus `givenName` / `familyName` ← `/presentedCredential/*` so the verdict screen can name
    the holder
  - `disclosable`: `["level", "portrait", "givenName", "familyName"]` — all four are
    *disclosable*, not all disclosed. The M3 verify moment asks for `level` + `portrait` (the
    verdict screen renders a face beside the band via the existing single-credential portrait
    path); the name claims stay withheld unless a surface explicitly asks for them. That is the
    selective-disclosure beat — the verifier learns the band and sees the holder, and nothing
    more.
- Routes: `approved-terminal` and `rejected-terminal`, the latter carrying `x-decision-notice`.

Claim-mapping pointers are **flat** (`/level`, `/presentedCredential/portrait`), following the
device-registration template's convention rather than the `/1/payload/...` form.

### Decision notices

Reuses F184 exactly, no new plumbing. The reject route's `x-decision-notice` carries
`reasonCodeField: "/reasonCode"` and a `reasons` catalogue:

| reasonCode | Citizen-facing message |
|---|---|
| `no-portrait` | AIAS cannot issue a Cyber Level without a face to put on it. Add a photo to your Assured Identity and come back. |
| `cyber-fail` | AIAS admires the honesty, but cannot certify this. Fix the shared passwords and try again. |

Approvals surface through the existing credential-received path, so only rejections need a
notice — the same reject-only rule F183 US2 established.

### Agent config trio

`demos/AIAS/agent/cyber.{config,rules,checks}.json`, alongside the existing `assure-id.*` trio.
Same agent binary in a second mode, per the north-star §5 — no new agent identity model.

**Gotcha to respect:** `AiasDemo.psm1` writes the agent config to a fixed shared path. A second
agent mode must not clobber the running Assure-ID agent's config. The cyber agent gets its own
config path and its own `state.json` entry.

### Provisioning

Extend `AiasDemo.psm1` per the north-star's cross-cutting rule (every milestone extends one
idempotent provisioning module). `Publish-AiasBlueprint` already publishes a **set** of
templates and `state.json` already carries a `blueprintIds` map (both from #1269), so adding a
third template is additive.

The genuinely new provisioning step is **creating the Cyber register**, owned by the same AIAS
issuer wallet, and publishing the cyber blueprint to it rather than to the Identity register.
This means `Publish-AiasBlueprint`'s "publish the set" loop needs a per-template register
target rather than one register for all templates — the one structural change in this
workstream. Register creation must be idempotent on re-run, like the rest of the module.

**Governance:** the publishing wallet needs a publish-governance role on the new register. The
register is created owned by the AIAS issuer wallet, which is what the Identity register already
does — but note that ownership alone was *not* sufficient historically (the F142 publish gate
checks the governance roster, not ownership). Confirm the roster grant seals before publishing,
rather than assuming ownership implies it.

**Validator schema-registry gotcha (reduced, not eliminated):** the validator's JsonSchema
registry is process-global and caches by schema text, so two blueprints inlining the same core
primitives with stable `$id`s collide with `VAL_SCHEMA_005: Overwriting registered schemas is
not permitted` — *regardless of which register they are on*. The cyber questionnaire inlines
none of those primitives, so it should not collide. If it ever does, the fix is
`docker restart sorcha-validator-service`, and provisioning should surface that explicitly
rather than letting it look like a submission failure.

---

## 8. Data flow

```
Morag (web /app)
  │  opens the cyber questionnaire — action 1 is presentation-gated
  ▼
F127 SorchaWallet gate → QR → phone wallet presents Assured Identity VC
  │  (givenName, familyName required; portrait optional)
  │  credential was issued on the IDENTITY register; this workflow is on the CYBER register
  ▼
PresentationOutcome success tx sealed on the CYBER register
  │  StateReconstructionService folds verifiedClaims → /presentedCredential
  ▼
Morag answers 8 questions → action 1 tx sealed
  │
  ▼
Agent polls pending actions → fetches disclosed data
  │  GET /api/workflows/{id}/actions/{id}/disclosures  (F176)
  │  → payload includes the 8 answers AND /presentedCredential/*
  ▼
ExternalCheckRunner: portraitPresent (bool) + cyberScore (numeric)
  │
  ▼
RulesDecisionEngine → first matching band rule
  │
  ├─ rejected → action 2 tx, no credential, F184 decision notice
  │
  └─ approved + level → action 2 tx
       │  issuanceCondition passes
       ▼
     CyberLevelCredential minted (level + portrait) → SorchaLocalWallet
       │
       ▼
     Lands in Morag's PWA via the F114 citizen inbox projector
```

---

## 9. Failure handling

| Failure | Behaviour |
|---|---|
| Presented credential has no portrait | Gate passes (portrait is optional); agent rejects, `no-portrait` notice |
| Score < 12 | Agent rejects, `cyber-fail` notice, no credential |
| `scored-questionnaire` check hard-faults | Runner contains → `false` → coerces to 0 → Fail band, no credential |
| Unrecognised answer string | Scores 0 for that question; total still computed |
| Answer missing | Cannot occur — all eight are schema-`required`, validator rejects the submission |
| Agent cannot fetch disclosed data | F176 hold — no submission, retried next poll |
| Slider value out of range | Rejected by `minimum`/`maximum` schema validation before the agent sees it |

Every path fails toward "no credential issued", never toward issuing an unearned one.

---

## 10. Testing

**Unit — `Sorcha.Agent.Tests`**
- `ScoredQuestionnaireCheck`: exact-match scoring, unrecognised answer → 0, range banding
  including boundaries (0, `max`, `max+1`), catch-all range, sum correctness.
- Band-boundary tests driven by the rules file: 11/12, 15/16, 20/21, 23/24 — the four
  transitions, asserted against the *actual* `cyber.rules.json` so a retune that breaks a
  boundary fails CI.
- `ExternalCheckRunner`: numeric fact merged as a JSON number; existing boolean checks
  unaffected.

**Unit — `Sorcha.UI.Components.User` tests (bUnit)**
- `SliderRenderer`: writes an integer not a string; seeds from `minimum`; honours `step`;
  renders end labels; keyboard stepping.
- `FormSchemaService`: `x-slider` → `ControlTypes.Slider`; integer **without** `x-slider` still
  → `Numeric` (the regression that matters).

**Unit — `Sorcha.Blueprint.Service.Tests`**
- `RequirementDcqlMapper`: optional claims land in the ask; a requirement with none behaves
  exactly as before.

**Rehearsal — `demos/AIAS/rehearse.ps1`**
Extend with three cyber paths asserted end to end:
1. High-scoring answers → Gold or Platinum credential delivered, `level` claim present.
2. Low-scoring answers → rejected, no credential, `cyber-fail` notice.
3. Portrait-less Assured Identity → rejected, `no-portrait` notice.

**Known rehearsal caveat:** the existing approval-delivery assertion times out at 60s, which is
too tight for a cold n1 — the credential lands correctly just past it. Verify via the wallet DB
rather than the script's exit code, or raise the timeout as part of this work.

---

## 11. Open risks

| Risk | Assessment |
|---|---|
| `presentedCredential` surviving the disclosure clamp to the agent | Expected to work — `ActionDisclosureResolver:234` clamps via the prior action's `disclosures`, and `/*` is a wildcard over all top-level keys including the injected one. The device-registration blueprint relies on the same path. **Verify explicitly in the first integration run** rather than assuming. |
| Optional-claim disclosure in the wallet | The PWA consent sheet must let the holder proceed while withholding an optional claim. F181 US2 built per-query optional toggles; confirm the single-ask path honours them. |
| Cross-register presentation | The questionnaire lives on its own register while the presented credential was issued on the Identity register. Presentation trust is anchored on issuer signature and DID resolution, not register membership, so this is expected to work — but it is the one new assumption the register separation introduces. **Verify in the first integration run.** |
| Publish governance on the new register | Ownership did not historically imply a publish-governance role (the F142 gate reads the governance roster). Provisioning must confirm the roster grant seals before publishing, or publish 403s in a way that previously looked like a seal timeout. |
| Answer strings as scoring keys | `Selection` has no separate display labels (`FormSchemaService.GetEnumValues:229` returns raw enum strings), so the answer sentence *is* the scoring key. Editing question copy silently breaks scoring. Mitigated by the band-boundary tests reading the real config; worth a check that every `answers` key exists in the blueprint's enum. |

---

## 12. Out of scope

- Per-question feedback breakdown. Deliberately rejected — the level plus one on-brand summary
  line reuses F184 as-is, where a breakdown would need new plumbing.
- Scoring in the blueprint. Rejected: the citizen's own submitted action would compute her own
  score, which is the wrong trust boundary for something a credential attests.
- Multi-credential verify (spec 098), agent control surface (M4), the three verify surfaces (M3).
