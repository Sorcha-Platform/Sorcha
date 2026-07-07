# Quickstart: Validate Feature 176 (agent decides on disclosed data)

**Feature**: 176-agent-disclosed-payload | **Date**: 2026-07-07

This is the end-to-end witness for the feature (spec SC-006). It proves the autonomous agent decides on the
**real** disclosed application: a valid application is approved and a credential delivered; an invalid one
(non-existent postcode) is rejected with no credential.

## Prerequisites

- A running Sorcha stack (Docker `docker-compose up -d`, or `n1`) with the AIAS demo provisioned:
  `./demos/AIAS/run-demo.ps1 -Target <docker|n1> -Force`.
- The Assure-ID agent running against that stack (built from this branch), authenticated as
  `assure-id-agent@…` with `AGENT_EMAIL`/`AGENT_PASSWORD` set:
  `dotnet run --project src/Apps/Sorcha.Agent -- run --config demos/AIAS/agent/assure-id.config.json --state demos/AIAS/state.json`

## Automated end-to-end (the regression)

```powershell
./demos/AIAS/rehearse.ps1 -Target <docker|n1>
```

**Expected — PASS** (previously FAILED before this feature):

- **Approval case** (valid postcode e.g. `SW1A 1AA`, verified email, portrait): agent records `approved`;
  an `AssuredIdentityCredential` is delivered to the applicant's wallet.
- **Rejection case** (postcode `ZZ99 9ZZ`): agent records `rejected` with the on-brand reason; **no credential
  is issued**.

## Manual spot-checks

1. **Disclosed-data endpoint** returns the applicant's fields to the agent's participant:
   ```bash
   # as the agent (assure-id-agent), for a pending verify action's instance/action:
   curl -sk "$GATEWAY/api/workflows/$INSTANCE/actions/$ACTION/disclosures" -H "Authorization: Bearer $AGENT_TOKEN"
   # → disclosedFields contains name/address/email/emailVerified/portrait; recipientResolved=true
   ```
2. **Agent evaluates real data** — the agent log shows non-empty payload fields:
   ```
   External checks evaluated for Verify Assured Identity Application:
     {"emailVerified":true,"photoPresent":true,"postcodeExists":false,"profane":false}
     (from payload fields: [name, address, email, emailVerified, portrait])
   ```
   (For the bad-postcode case `postcodeExists:false` → the reject rule fires.)
3. **Fail-closed** — make the disclosed-data endpoint temporarily unreachable for one application and confirm
   the agent **holds** it (no approve/reject, no credential) and logs the hold reason; restore and confirm it
   is then decided correctly.
4. **Disclosure boundary** — confirm the endpoint returns only fields disclosed to the agent's participant
   (no field the applicant did not disclose to `verification-analyst`).

## Success criteria mapping

| Check | Spec |
|---|---|
| `rehearse.ps1` PASS (approve valid, reject invalid) | SC-001, SC-002, SC-003, SC-006 |
| Fail-closed hold on unavailable data | SC-004 |
| Agent log shows the evaluated facts from real fields | SC-005 |

## Notes

- Drive the demo from a host without HTTPS interception when targeting `n1` (see the AVG-TLS note); the agent
  itself can run anywhere that can reach the gateway.
- This quickstart replaces ad-hoc diagnosis: a red `rehearse.ps1` with `postcodeExists:false` yet an approval
  is the exact signature this feature fixes.
