# Research — Feature 104 Credential Claim Action

**Date:** 2026-04-14
**Scope:** Resolves the four open planning questions identified in the design spec at `docs/superpowers/specs/2026-04-14-wave-14-credential-claim-action-design.md`. Each finding is backed by code references so the plan can commit to a concrete implementation without further investigation.

---

## Decision 1 — Pending action payload encryption

**Decision:** Store `Instance.PendingActionPayloads` as plaintext in MongoDB, mirroring the current handling of `Instance.AccumulatedData`. No new encryption layer in wave 14a.

**Rationale:**
- `Instance.AccumulatedData` is persisted plaintext today. Evidence: `src/Services/Sorcha.Blueprint.Service/Storage/EfCoreInstanceStore.cs` lines 129, 398, 432–433 serialize to JSON via `SerializeJson()` with no encryption step; `src/Services/Sorcha.Blueprint.Service/Models/Instance.cs` line 109 declares it as a plain `Dictionary<string, object>`.
- The existing encryption machinery (`EncryptionBackgroundService` / `IEncryptionPipeline`) is a **disclosure** pipeline: it encrypts payloads before they are sealed into register transactions (`EncryptionBackgroundService.cs` line 127). It is not a general-purpose at-rest encryption layer for instance state.
- The HAIP `pre_authorized_code` carried in the seed is short-lived (typically minutes to hours). It is a bearer token, not a long-lived secret. Storing it next to `AccumulatedData` matches the established trust boundary for instance state.
- Adding a new at-rest encryption layer exclusively for `PendingActionPayloads` would be inconsistent with `AccumulatedData` and would introduce a bespoke code path for one field. If at-rest encryption becomes a project-wide requirement, it should be added uniformly across instance state in a dedicated PR.

**Alternatives considered:**
- *Reuse the disclosure pipeline for at-rest encryption.* Rejected: wrong mechanism, wrong stage (disclosure encrypts for transmission to the register, not for MongoDB persistence).
- *Add a custom EF Core value converter for `PendingActionPayloads`.* Rejected for wave 14a as inconsistent with `AccumulatedData` and out of scope for a feature that is additive on top of the existing instance state model.
- *Defer storage of the offer URI and re-fetch from the HAIP issuer at render time.* Rejected: loses the offline-resume property that motivates the seed-payload design in the first place (FR-003).

**Implications for the plan:**
- `Instance.PendingActionPayloads` added as `Dictionary<int, JsonObject>` alongside `AccumulatedData`, same persistence semantics.
- No new secret-handling code paths.
- Threat model note documented in the plan: instance state is plaintext-at-rest in MongoDB and that is an existing project assumption.

---

## Decision 2 — Decline semantics on the register

**Decision:** Reuse the existing `RejectionConfig.IsTerminal = true` pattern already wired into action execution and the form renderer. The Verified Citizen v2 action 2 declares `rejectionConfig: { isTerminal: true }`; `CredentialClaimCard` wires its Decline button to the form's existing `OnReject` callback.

**Rationale:**
- `Action.RejectionConfig` already exists (`src/Common/Sorcha.Blueprint.Models/Action.cs` lines 171–173, `src/Common/Sorcha.Blueprint.Models/RejectionConfig.cs` lines 20–54). Blueprint authors declare a rejection branch on an action, and `IsTerminal = true` routes rejection to the terminal `InstanceState.Rejected`.
- `ActionExecutionService.cs` line 942 already writes `instance.State = InstanceState.Rejected` when a rejection is terminal. This goes through the normal action-execution transaction path and seals to the register with full audit trail.
- `SorchaFormRenderer.razor` line 159 already renders a Reject button conditionally on `Action?.RejectionConfig is not null`, and `OnReject` is an existing EventCallback on the renderer.
- `BlueprintLayoutService.cs` lines 206–208 already surfaces `RejectionConfig` to the UI layer.

**Alternatives considered:**
- *Cancel the pending action without writing a register transaction.* Rejected: no existing cancel path in `ActionExecutionService`; adding one would create a second code path for "action ended unsuccessfully" (reject vs cancel) with no clear semantic distinction. Violates DRY and weakens the audit trail (cancel would be invisible on the register).
- *Invent a new "declined credential offer" action kind.* Rejected: `RejectionConfig.IsTerminal` already expresses the exact semantics ("this action ended negatively and the instance is over"). Inventing new vocabulary for the same concept.

**Implications for the plan:**
- Wave 14b blueprint JSON for Verified Citizen v2 (action 2) sets `rejectionConfig.isTerminal = true`. No backend changes.
- `CredentialClaimCard` component binds its Decline button to the existing `OnReject` EventCallback plumbed through `SorchaFormRenderer`.
- Decline records `InstanceState.Rejected` on the register via the normal action-execution flow.
- FR-024 ("every declined claim appears on the register as a recorded outcome") is satisfied automatically.

---

## Decision 3 — Expiry transition mechanism

**Decision:** Client-side expiry transition for wave 14b. When the citizen opens a claim action whose `expires_at` is in the past, `CredentialClaimCard` shows an expired state with Claim disabled and fires a status-transition request to mark the action `Failed`. No server-side background sweep.

**Rationale:**
- No existing server-side expiry-sweep pattern for pending actions in the Blueprint Service. Existing hosted services (`EncryptionBackgroundService`, `OrphanChunkCleanupService`, `CoreSchemaSeedService`, `SchemaIndexRefreshService`) handle other concerns; none check action deadlines.
- `Action` has no `Deadline` or `ExpiresAt` field today — only `Route.BranchDeadline` exists (`src/Common/Sorcha.Blueprint.Models/Route.cs` lines 57–64), which applies to parallel branches, not to individual actions. Adding action-level expiry sweep would be a meaningful new subsystem.
- Client-side transition matches the existing pattern in `CredentialOfferQrCard` (wave 13) which polls HAIP offer status from the browser and transitions state on result. Reusing this pattern is strictly additive and requires no new hosted services.
- The failure mode for offline citizens — "you came back a week later and your offer expired" — is identical in both mechanisms: a client-side check fires on open, shows the expired UI, marks the action `Failed`. A server-side sweep would mark it earlier but a citizen who never returns still sees the same end state.

**Alternatives considered:**
- *Server-side expiry-sweep `IHostedService`.* Rejected for v1 as scope creep. It is a valid improvement for a later PR, especially once more than one blueprint uses offers with expiries. The plan explicitly keeps it out of scope and documents it as a follow-up in the out-of-scope section.
- *No expiry transition — let the action stay pending indefinitely.* Rejected: FR-017 requires a Failed state transition on expiry. The register audit trail benefits from an explicit failed outcome.

**Implications for the plan:**
- Wave 14b adds client-side `CredentialClaimCard` logic: on mount and on tick, compare `expires_at` to `DateTimeOffset.UtcNow`; if past, render expired state and call a status-transition endpoint.
- A new backend endpoint is required: mark a pending claim action as failed due to expiry. This endpoint is specific to claim actions (not a general-purpose "fail any pending action" — too broad and dangerous).
- Contract for this endpoint is documented in `contracts/action-claim-expire.http`.

---

## Decision 4 — Form validation with x-credential-offer fields

**Decision:** No changes to the validation pipeline are required. The engine's `ValidateActionDataAsync` already validates only the fields present in the submitted payload; the merge of `PendingActionPayloads[actionId]` into the submission before validation (already required by FR-007) is sufficient to satisfy schema validation without special-casing `x-credential-offer`.

**Rationale:**
- Schema validation runs against the submitted payload at `src/Services/Sorcha.Blueprint.Service/Services/Implementation/ActionExecutionService.cs` line 975 (`ValidateActionDataAsync` → `_executionEngine.ValidateAsync(data, action)`). It does not enforce presence of fields not included in the submitted dictionary unless the schema marks them `required`.
- Wave 14a's merge step (FR-007) already ensures that when the citizen submits `{ "claimed_at": "..." }`, the merge with `instance.PendingActionPayloads[actionId]` produces `{ "credentialOffer": { ... }, "claimed_at": "..." }` *before* validation runs. The merged payload then satisfies the schema's `required: ["credentialOffer"]` constraint naturally.
- `x-persona` and `x-file` extensions confirm the pattern: they populate payload fields before submission rather than rewriting the validation pipeline. `PersonaAutofillResolver.cs` pre-fills matching fields from the user's profile; `x-file` validates file constraints at submission time on an already-populated file reference (`FileSchemaExtension.cs` lines 93–112). Neither skips validation; neither special-cases the validator.
- The UI-layer "form validation is skipped for fields under `x-credential-offer`" guidance in the design spec was about not running *client-side* form field validation on a field the user cannot edit — not about skipping the engine's schema validation. The engine runs on the merged payload and sees a complete, valid `credentialOffer` object.

**Alternatives considered:**
- *Skip schema validation entirely for actions with any `x-credential-offer` field.* Rejected: unnecessary, unsafe (loses validation of the sibling `claimed_at` field), and inconsistent with existing extension handling.
- *Mark `credentialOffer` as optional in the schema.* Rejected: the claim action is meaningless without the offer. Marking optional creates a false state ("action submitted with no offer") that would silently seal broken data to the register.

**Implications for the plan:**
- `ActionExecutionService.SubmitActionExecuteAsync` is updated to merge `instance.PendingActionPayloads[actionId]` into the submitted data **before** calling `ValidateActionDataAsync`. This is already required by FR-007 and is the load-bearing ordering constraint.
- The renderer-side work in wave 14b is purely presentational: render the card, don't run client-side form-field validation on the read-only fields. The engine handles the rest.
- No new validation code paths.

---

## Cross-cutting findings

- **Existing wave 13 service:** `IHaipLocalReceiveService` (`src/Apps/Sorcha.UI/Sorcha.UI.Core/Services/Credentials/HaipLocalReceiveService.cs`) is reused verbatim. It already implements the full OpenID4VCI flow against the citizen's wallet address and writes to the local credential store. Wave 14b's `CredentialClaimCard` is the second consumer of this service (after wave 13's `CredentialOfferQrCard`).
- **Existing wave 13 component:** `CredentialOfferQrCard` handles the QR + local-receive button for external-wallet scenarios. Wave 14b reuses it as an embedded sub-view inside `CredentialClaimCard` when the citizen chooses "Scan with external wallet."
- **Open participant late-binding:** Confirmed intact (`ActionExecutionService.cs` lines 196–216 and 309–332 per CLAUDE.md). The claim action's sender participant (same as action 0's open applicant) will automatically carry the citizen's wallet address through the binding mechanism already in place. No new work required here.
- **Blueprint validation for output mapping:** New blueprint publish-time validation is in scope — mapping target paths must correspond to schema fields on the target action, and mapping source paths should be resolvable at route-evaluation time against the documented source document shape (submitted payload, calculations, HAIP mint output).

---

## Ready for Phase 1

All four open questions resolved with concrete code-backed decisions. No `NEEDS CLARIFICATION` markers remain. Phase 1 can proceed to data model, contracts, and quickstart.
