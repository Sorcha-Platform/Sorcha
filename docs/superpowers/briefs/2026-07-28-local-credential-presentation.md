# Brief: make local credential presentation work — and stop shipping this class of error

**Created:** 2026-07-28
**Tracking issue:** [#1330](https://github.com/Sorcha-Platform/Sorcha/issues/1330)
**Status:** superseded — see docs/superpowers/specs/2026-07-28-local-credential-presentation-design.md (§4 unknowns established 2026-07-28; Route A chosen)

Paste this whole file as the opening prompt of a fresh session. It is written to be read cold.

---

## 1. Load these first

**Skills** (via the Skill tool, before touching anything):

| Skill | Why |
|---|---|
| `superpowers:systematic-debugging` | This is a defect hunt. Root cause before fixes; the Iron Law applies. |
| `verifiable-credentials` | SD-JWT VC, selective disclosure, key binding, `PresentationRequestService`, the three-address model. Non-obvious and you will get it wrong from first principles. |
| `sorcha-architecture` | F111 lifecycle, F127 gates, the endpoint surface. |
| `blazor` | The UI half lives in Blazor WASM components. |
| `superpowers:brainstorming` | **Only if** the answer turns out to be a design choice (see §6). Do not skip straight to writing-plans. |

**Memories to recall by description** (they are topic files, not in `MEMORY.md`):

- `seam-bugs-nothing-verifies-the-join` — **read this one first.** It is the trap class this whole brief exists to close. Seven logged instances, including the three from 2026-07-28.
- `f127-presentation-gate-transport` — what shipped in #1327/#1329, what is still unverified.
- `citizen-oid4vp-device-cnf` — holder-cnf vs device-cnf; one assurance, two bindings. Directly bears on whether a web-app presentation can be key-bound.
- `credential-vct-decoupling` — `vct` is the sole machine identity, matched **case-sensitively**.
- `ui-ambient-httpclient-is-anonymous` — before theorising about auth, read the request headers.
- `aias-conference-demo` — the journey this blocks.

---

## 2. The goal

A citizen **signed in on `/app`**, whose **own wallet already holds** a matching credential, and who **has consented** by selecting it, should satisfy a credential gate **without a QR code and without a second device**.

Today they are shown a QR to scan with a phone. That is the bug.

The cross-device QR path must keep working for citizens whose credential is only on a phone. This is an *addition*, not a replacement.

---

## 3. What is verified true

Every claim here was read from the code or observed live on 2026-07-28. Cited so you can re-check rather than trust.

**The server already supports this.**
- `ActionExecutionService.cs:316` — `hasSubmittedPresentations = request.CredentialPresentations is { Count: > 0 }`
- `ActionExecutionService.cs:318` — the cross-device lifecycle starts **only** when `!hasSubmittedPresentations`
- `ActionExecutionService.cs:392` — otherwise `request.CredentialPresentations ?? []` goes straight to `ICredentialVerifier.VerifyAsync`
- `ActionSubmissionRequest.cs:65` — `public List<CredentialPresentation>? CredentialPresentations { get; init; }`

So a **valid** presentation submitted with the action means no QR at all.

**The client cannot currently produce one.**
- `CredentialGatePanel.razor` (~line 207) builds a `CredentialPresentationInfo` with `RawPresentation = string.Empty` and `KeyBindingProof = null`.
- A comment there previously claimed `ActionExecutionService` fills `RawPresentation` in server-side. **It does not.** The comment has been corrected in place; the correction is the evidence trail.
- `ActionExecuteRequest` (client) has **no** `CredentialPresentations` property. `ActionSubmissionViewModel` has none either.

**Live state on n1** (2026-07-28, presentation `e558be04-28b7-4359-a0ad-962b6bcd9ffb`):
```json
{"state":"awaiting-presentation","expiresAt":"2026-07-28T16:09:20Z"}
```
The gate polls `/api/presentations/{id}/status` correctly. It waits because nothing ever presents.

**What was tried and reverted.** PR #1329 threaded the selection through. Reverted before deploy: an empty `RawPresentation` fails verification, converting *"a QR you can't complete"* into *"submission rejected: credential verification failed"* — worse, and it reads as though the citizen's credential is bad. Commit `5c0ce81e`.

---

## 4. What is NOT known — investigate before designing

Do not assume any of these. Each is a real fork in the road.

1. **Can a signed SD-JWT VP be produced for a web-app citizen at all?** The wallet service exposes `POST /presentations/request`, `POST /{requestId}/submit`, `GET /{requestId}/result` (`PresentationEndpoints.cs`) and `POST /presentations/sign-kb` (`CitizenWalletEndpoints.cs`). **Read what each actually does.** Do not infer from the names — that is the mistake this brief exists to prevent.

> **ANSWERED:** /presentations/request|submit|result are a legacy in-memory OID4VP mini-flow that VERIFIES a caller-built vpToken — nothing in it builds one. sign-kb signs a client-built KB-JWT input with the slot-108 holder key.

2. **Whose key signs it?** The web citizen's wallet is server-custody. `citizen-oid4vp-device-cnf` says holder-cnf is the web root and device-cnf is a copy. Which binding does a gate require, and does the verifier accept a holder-cnf presentation with no device binding?
3. **Does `PresentationRequestService` do selective disclosure from a credential id + chosen claims?** Documented as such in the `verifiable-credentials` skill. Confirm against the code.
4. **What does `CredentialMatcher` return?** `CredentialMatchResult` shape decides what the panel can pass on.
5. **Is `presentationSource: SorchaWallet` even right for the AIAS Cyber blueprint?** `SorchaInternal` routes to the synchronous verifier. If the intended journey is web-first, the blueprint may be asking for the wrong thing. That is a **design** question — see §6.
6. **Does the verifier require key binding for this `vct`?** `TrustPolicy` / `RevocationCheckPolicy` on the requirement may change what a valid presentation must carry.

---

## 5. How to work — this part is not optional

This session's author contradicted themselves **nine times** in one day. Every instance had the same cause: **asserting from partial evidence, or trusting a stated contract instead of reading the consumer.** The countermeasures below are derived from those specific failures.

**Read the consumer, not the producer.** The trap that caused this bug was a comment asserting server behaviour that did not exist. Before believing any contract — comment, doc, XML summary, skill file, or this brief — open the code on the *other* side of the seam. `seam-bugs-nothing-verifies-the-join` has seven worked examples.

**Never conclude from a signal that has two causes.** Real misfires from this session:
- `total_cost_usd: 0` was read as "auth failed". A subscription token also reports `0`.
- An OpenAPI schema lacking a field was read as "not deployed". That endpoint documents no success response at all.
- One green CI run was read as "the pin fixed it". The next run failed identically.

State the evidence and the inference separately, and say which is which.

**Make every new guard fail before you trust it.** Two guards written this session passed immediately and were worthless:
- A corpus test that walked only root-level blueprints, silently skipping every `BlueprintTemplate` — half the corpus. It was green against files it had never opened. Fixed by asserting a **floor on items walked**.
- A CI gate that validated config data but never the loop consuming it, so it could not see a format change that broke that loop.

Perturb the thing, see RED, restore. Every time.

**Prefer running it once over any amount of review.** Four defects survived nine review rounds on AIAS M2 and died within minutes of first live execution. The two defects in #1329 were found by one click-through that no test could reach. **Budget for a live run on n1 before claiming done.**

**When you change a value's format, grep its consumer.** Widening a config value to a list without updating the loop that read it made every multi-path entry match *nothing* — strictly worse than before, and silent.

**Verify before acting on a claim, including from this brief.** Two review claims this session were overstated. If something here contradicts the code, **the code wins** — and correct this file.

---

## 6. If the answer turns out to be a design choice

There is a plausible outcome where the fix is not "build a presentation client-side" but "this gate should offer both routes" or "the blueprint should declare a different `presentationSource`". If you reach that fork:

**Stop and use `superpowers:brainstorming`.** Do not pick silently. The UX question — does the citizen see *"use this device"* and *"scan with your phone"* side by side, or does the platform choose? — is a product decision with security implications (a locally-satisfiable gate is a weaker gate if the credential is not key-bound).

Present options with trade-offs and get a decision before implementing.

---

## 7. Suggested task order

1. **Establish the facts in §4** by reading code. Write down what each endpoint actually does, with file:line. No fixes yet.
2. **Answer: can we build a verifiable presentation for a web citizen today?** Yes / no / only with X.
3. **If yes** — prove it outside the UI first. A test or a scripted call that produces a presentation which `CredentialVerifier.VerifyAsync` accepts. **This is the pivotal evidence.** Everything downstream is plumbing already mapped in §3.
4. **If no** — identify precisely what is missing, then §6.
5. Re-apply the threading from `5c0ce81e` (revert it back in) *only once* step 3 passes. The four copy sites and the wire test are in that commit and were correct.

> **CORRECTED 2026-07-28:** step 5 presumed the sync inline route. Investigation showed the sync path never checks key binding (CredentialVerifier sets no nonce/audience) and skips the F111 register record, while the async path was already proven completable server-custody with no device (rehearse.ps1 Complete-SorchaWalletPresentation, green on n1). 5c0ce81e stays reverted; the fix is the local completion of the async lifecycle.

6. **Live-verify on n1.** Submit the AIAS Cyber questionnaire signed in as a citizen holding an Assured Identity credential. Expected: no QR, submission completes.
7. Update `seam-bugs-nothing-verifies-the-join` with whatever new instance this turns up.

---

## 8. Definition of done

- [ ] Citizen on `/app` with a matching credential satisfies the gate with **no QR and no second device**.
- [ ] Proven by a **completed submission on n1**, not by a green test suite.
- [ ] Cross-device QR still works for phone-only citizens (do not regress #1327).
- [ ] Every new guard mutation-tested: perturbed, RED observed, restored.
- [ ] The `CredentialGatePanel` comment describes what the code does — and if it changes, both sides checked.
- [ ] #1330 closed with the evidence, not with an assertion.

---

## 9. Do not repeat these

Concrete errors from 2026-07-28, listed so they are not re-made:

| Claim made | Why it was wrong |
|---|---|
| "35 dead dark-mode rules" | 8 of the matches were `obj/bin` build artifacts. Real count was 5. |
| "The action pin fixed claude-review" | One green run. The next failed identically. |
| "The HAIP poll is unbounded" | `MaxPollTicks = 150` × 2s = 5 minutes. |
| "Blueprint and client disagree on enum wire form" | Asserted before testing; the round-trip works. |
| "The OAuth token is expired" | Inferred from `$0` cost, which subscriptions also report. |
| "v1.0.180 broke claude-review" | Pinned to v1.0.179; fails identically. Written into a workflow comment where it still misleads. |
| "The server populates RawPresentation" | Trusted a TODO comment. It was false, and caused this issue. |

The pattern is one thing: **a confident claim built on a signal with more than one explanation.**
