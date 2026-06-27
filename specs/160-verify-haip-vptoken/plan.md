# Implementation Plan: HAIP returns raw vp_token on verifier result poll (PR B1)

**Branch**: `160-verify-haip-vptoken` | **Spec**: `./spec.md`
**Scope**: `src/Services/Sorcha.Haip.Service` only + its test project.

## Technical approach
Purely additive change across three files plus a test.

1. **Model** — `src/Services/Sorcha.Haip.Service/Models/VerifierModels.cs`
   Add two nullable string properties to `PresentationRequest`:
   - `SubmittedVpToken` — the raw `vp_token` posted by the holder.
   - `PresentationSubmission` — the OID4VP `presentation_submission` posted alongside it.
   (Nullable so existing serialized Redis records deserialize cleanly.)

2. **Store** — `src/Services/Sorcha.Haip.Service/Services/PresentationRequestStore.cs`
   Extend `MarkCompletedAsync` with optional `string? vpToken = null, string? presentationSubmission = null`
   params; set them on the request inside the existing compare-and-set loop (before re-serialize), so
   the raw token is persisted atomically with the result and shares the same TTL. No new key/TTL.

3. **Endpoint (capture)** — `Endpoints/VerifierEndpoints.cs` `HandleDirectPost`
   Pass the already-bound `vp_token` and `presentation_submission` form values into
   `MarkCompletedAsync(...)`.

4. **Endpoint (return)** — `Endpoints/VerifierEndpoints.cs` `GetVerificationResult`
   Add `vpToken = request.SubmittedVpToken` and `presentationSubmission = request.PresentationSubmission`
   to both response branches (pre- and post-result). Existing fields unchanged.

## Testing
- Extend the HAIP verifier tests: create a request via the store, mark completed with a sample
  `vpToken` + `presentationSubmission`, re-`GetAsync`, and assert both round-trip. If an
  endpoint-level harness exists, add a create → direct-post → poll test asserting the raw token is
  returned post-submission and null pre-submission.

## Risks / notes
- Backward-compat: new fields are additive + nullable; old Redis records and the Blueprint result
  consumer are unaffected (FR-004).
- No auth change (deferred to B3); no change to validation behaviour.
