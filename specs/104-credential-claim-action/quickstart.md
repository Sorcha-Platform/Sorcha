# Quickstart — Feature 104 Credential Claim Action

**Audience:** Developers reviewing or implementing wave 14a/14b. Assumes familiarity with Sorcha's blueprint model and wave 13's HAIP local receive flow.

**Goal:** Demonstrate the end-to-end credential claim flow using a minimal example blueprint, so that anyone can validate the feature quickly after it lands.

---

## Prerequisites

- Docker stack running (`docker-compose up -d`) with wave 14a + 14b images deployed.
- A citizen account and an assessor account, each with a Sorcha wallet.
- A register with the Verified Citizen v2 v3 blueprint published.

---

## 1. Demonstrate the engine primitive (wave 14a) in isolation

Wave 14a is the engine's payload carry-forward primitive. It ships with zero user-visible features — but you can verify it against a minimal two-action blueprint that isn't the credential claim flow.

Create a test blueprint `examples/templates/output-mapping-smoke.json`:

```json
{
  "id": "output-mapping-smoke",
  "title": "OutputMapping smoke test",
  "description": "Minimal two-action blueprint exercising Route.OutputMapping",
  "version": 1,
  "participants": [
    { "id": "author",   "name": "Author",   "description": "Writes data" },
    { "id": "reviewer", "name": "Reviewer", "description": "Sees pre-seeded data" }
  ],
  "actions": [
    {
      "id": 0,
      "title": "Write",
      "sender": "author",
      "isStartingAction": true,
      "dataSchemas": [{
        "type": "object",
        "properties": { "note": { "type": "string" } },
        "required": ["note"]
      }],
      "routes": [{
        "id": "to-reviewer",
        "nextActionIds": [1],
        "isDefault": true,
        "outputMapping": {
          "/payload/note": "/carriedNote"
        }
      }]
    },
    {
      "id": 1,
      "title": "Acknowledge",
      "sender": "reviewer",
      "dataSchemas": [{
        "type": "object",
        "properties": {
          "carriedNote":   { "type": "string" },
          "acknowledged":  { "type": "boolean" }
        },
        "required": ["carriedNote", "acknowledged"]
      }],
      "routes": []
    }
  ]
}
```

Publish and create an instance, then:

1. Submit action 0 as the author with `{ "note": "hello from action 0" }`.
2. Query the reviewer's pending actions. The pending action should include a `prepopulatedPayload` of `{ "carriedNote": "hello from action 0" }`.
3. Submit action 1 as the reviewer with `{ "acknowledged": true }`.
4. Query the sealed transaction for action 1. The payload should be `{ "carriedNote": "hello from action 0", "acknowledged": true }` — demonstrating the merge-with-seed behaviour.

**Success criteria:** The reviewer never typed `carriedNote`; it arrived prepopulated from the author's action. The sealed transaction contains both fields.

---

## 2. Demonstrate the credential claim feature (wave 14b)

This is the full user-facing flow from the spec.

### 2a. Author approves a Verified Citizen application

1. As a citizen, navigate to `/new-submissions`, start the Verified Citizen v2 blueprint, fill in the applicant form (action 0), and submit.
2. Log out. Log in as the assessor.
3. Open `/my-actions`. The Verified Citizen application appears as a pending review. Open it.
4. Approve the application (action 1). Submit.

**What happens under the hood:**
- `ActionExecutionService` mints the HAIP credential offer via `HaipCredentialMinter`.
- The engine evaluates action 1's route `OutputMapping`, which writes `/haip/credential_offer_uri`, `/haip/display`, `/haip/expires_at` into action 2's prepopulated payload.
- `Instance.PendingActionPayloads[2]` now contains the full `credentialOffer` object.
- Action 2 becomes pending for the citizen (sender-locked to their wallet via late-binding).

### 2b. Citizen claims the credential

1. Log out. Log back in as the citizen.
2. Open `/my-actions`. A new pending action appears: **"Claim your Verified Citizen credential"**.
3. Open it. `CredentialClaimCard` renders with:
   - Header: "Verified Citizen Credential"
   - Subtitle: "Issued by Government of Exampleland"
   - Description: "Confirms your verified identity for future online services"
   - Issuer logo + name
   - Expiry: e.g., "Expires 15 Apr 2026 11:32"
   - Primary button: **Claim credential**
   - Secondary button: **Decline**
   - Link: **Scan with external wallet**

4. Click **Claim credential**.
5. Observe in devtools network: request to HAIP issuer metadata, request to wallet signing endpoint, request to HAIP credential endpoint, request to wallet credential store.
6. Snackbar: "Verified Citizen Credential received and stored in your local wallet."
7. Navigation to `/my-credentials`. The new credential appears in the list.
8. Open `/my-transactions`. A new sealed transaction for action 2 appears with `claimed_at` in the payload.

**Success criteria (P1 user story):**
- Credential is in the citizen's `/my-credentials`.
- Credential is **not** in the assessor's `/my-credentials`.
- Register shows three sealed actions for the instance: application submitted, approved, claimed.

### 2c. Test the external wallet path (P2)

Repeat 2a. At step 2b.3, instead of clicking Claim:

1. Click **Scan with external wallet**.
2. A QR code renders in place.
3. Scan the QR with an external HAIP-compliant wallet (for development, use the HAIP walkthrough's simulator).
4. Once the external wallet completes the exchange, `CredentialClaimCard`'s HAIP offer status poll detects `Exchanged` state.
5. Action 2 auto-completes with `claimed_at`, snackbar confirms success, navigation to `/my-credentials`.
6. The credential is **not** in the citizen's Sorcha `/my-credentials` list (it is in the external wallet instead).

### 2d. Test retry on transient failure (P2)

1. Temporarily stop the HAIP service container: `docker-compose stop haip-service`.
2. Repeat 2a.
3. At step 2b.4, click Claim. Request to HAIP fails; snackbar shows error "Could not receive locally: <error>". Action 2 stays pending.
4. Start HAIP: `docker-compose start haip-service`.
5. Click Claim again. Success.

**Success criteria:** The citizen never starts a new application, the same action 2 completes on retry.

### 2e. Test decline path (P1)

1. Repeat 2a.
2. At step 2b.4, click **Decline** instead of Claim.
3. Confirm decline in the dialog.
4. Action 2 transitions to Rejected on the register via `RejectionConfig.IsTerminal`.
5. The citizen is navigated away. `/my-actions` no longer shows action 2.
6. `/my-credentials` does not contain the credential in any wallet.

**Success criteria:** Decline records a sealed Rejected outcome on the register.

### 2f. Test expiry (P3)

For testing, publish a dev version of the Verified Citizen v2 blueprint whose HAIP offer expiry is configured to 2 minutes.

1. Repeat 2a.
2. After step 2b.3, do not click anything for 3 minutes.
3. Reload `/my-actions` and open action 2.
4. `CredentialClaimCard` renders expired state: Claim button disabled, explanation visible.
5. `CredentialClaimCard` fires `POST .../claim-expired`. Action transitions to Failed on the register.
6. `/my-actions` no longer shows action 2.

**Success criteria:** Expired offers are never successfully claimed; the action is marked Failed with reason `expired`.

---

## 3. Walkthrough scripts

The existing `walkthroughs/HaipVerifiedCitizen/` and `walkthroughs/HaipDrivingLicence/` scripts are updated as part of wave 14b. After running `dotnet run --project walkthroughs/HaipVerifiedCitizen` against a clean stack, the walkthrough:

1. Creates citizen and assessor accounts.
2. Citizen submits action 0 (application).
3. Assessor submits action 1 (approval) and observes the HAIP mint completing.
4. Walkthrough switches identity to citizen, queries `/my-actions`, asserts action 2 is pending with the credential offer seed.
5. Walkthrough exercises the claim path, asserts the credential lands in the citizen's local store.
6. Walkthrough asserts the register has three sealed transactions for the instance.

Re-run against n1.sorcha.dev after wave 14b ships to verify remote deployment (see `.claude/skills/network-bootstrap/SKILL.md`).

---

## 4. Rollback plan

Wave 14a and 14b are additive and independently revertable:

- **Wave 14a revert:** Revert the PR. `Route.OutputMapping` and `Instance.PendingActionPayloads` fields disappear. Existing instances' `PendingActionPayloads` data is orphaned but harmless. Any blueprint that tried to use `OutputMapping` will publish but evaluate to no-op. No data loss.
- **Wave 14b revert:** Revert the PR. The Verified Citizen v2 v3 blueprint rolls back to v2 (two actions). The engine primitive from 14a remains available for other blueprints. Citizens who had claim actions pending can still use wave 13's QR dialog path if needed.
- **Joint revert:** Revert both PRs. Back to wave 13 behaviour exactly.

---

## 5. Observability checks after deployment

After wave 14 lands:

- Metric: Rate of `/api/blueprint/instances/*/actions/*/claim-expired` calls — should be low, spike only when offers are configured with short expiry.
- Metric: Rate of action 2 Rejected outcomes on Verified Citizen v2 — should be low (citizens rarely decline their own credential).
- Trace: Action 1 execution for Verified Citizen v2 should show the HAIP mint span followed by the `OutputMapping` evaluation span.
- Log: No warnings from `BlueprintValidator` about `VAL_BP_011` on published blueprints (signals misconfigured output mappings).
- Dashboard: My Credentials page for assessor accounts should never show Verified Citizen credentials created via blueprint flows post-wave-14.
