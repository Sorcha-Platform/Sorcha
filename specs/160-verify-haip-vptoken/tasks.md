# Tasks: HAIP returns raw vp_token on verifier result poll (PR B1)

**Branch**: `160-verify-haip-vptoken` | **Plan**: `./plan.md`

- [x] **T001** — Add `SubmittedVpToken` (string?) and `PresentationSubmission` (string?) to
  `PresentationRequest` in `src/Services/Sorcha.Haip.Service/Models/VerifierModels.cs`, with XML doc
  summaries.
- [x] **T002** — Extend `PresentationRequestStore.MarkCompletedAsync` with optional
  `vpToken` / `presentationSubmission` params; set them on the request inside the CAS loop before
  re-serialize. (`src/Services/Sorcha.Haip.Service/Services/PresentationRequestStore.cs`)
- [x] **T003** — In `HandleDirectPost`, pass the bound `vp_token` + `presentation_submission` to
  `MarkCompletedAsync`. (`Endpoints/VerifierEndpoints.cs`)
- [x] **T004** — In `GetVerificationResult`, add `vpToken` + `presentationSubmission` to both
  response branches. (`Endpoints/VerifierEndpoints.cs`)
- [x] **T005** — Test: round-trip a `vpToken` + `presentationSubmission` through
  create → MarkCompletedAsync → GetAsync (and an endpoint-level poll test if a harness exists);
  assert raw token returned post-submission, null pre-submission, existing fields intact.
- [x] **T006** — `dotnet build` + run the HAIP test project green; XML doc summaries on all new
  public members (no build warnings).
