# Quickstart: Identity & Credentials Admin

## Integration Scenarios

### Scenario 1: Credential Lifecycle via UI

1. User navigates to credential detail view
2. Credential is Active — UI shows "Suspend" and "Revoke" buttons
3. User clicks "Suspend" — dialog opens with wallet picker and reason field
4. User selects issuer wallet, enters reason, confirms
5. CredentialApiService.SuspendCredentialAsync calls POST /api/v1/credentials/{id}/suspend
6. UI updates credential status to "Suspended", action buttons change to "Reinstate" and "Revoke"

### Scenario 2: Participant Publish via UI

1. Admin navigates to participant detail page
2. Participant is Active with linked wallets — "Publish to Register" button visible
3. Admin clicks button — publish dialog opens pre-filled with name and org name
4. Admin selects register, wallet addresses, signer wallet, submits
5. ParticipantPublishingService.PublishAsync calls POST /api/organizations/{orgId}/participants/publish
6. UI shows published register indicator on detail page

### Scenario 3: Presentation Flow (Holder + Verifier)

1. Verifier navigates to /admin/presentations, fills form, creates request
2. PresentationAdminService.CreatePresentationRequestAsync calls POST /api/v1/presentations/request
3. QR code displayed with openid4vp:// URL
4. Holder views credentials page, sees pending request in list
5. Holder clicks request, reviews claims, selects credential, approves
6. CredentialApiService.SubmitPresentationAsync calls POST /api/v1/presentations/{id}/submit
7. Verifier polls result, sees verification outcome

### Scenario 4: CLI Credential Suspend

```bash
sorcha credential suspend --id cred-001 --wallet addr1... --reason "Under review"
# Output: Credential 'cred-001' suspended successfully.
#   Status: Suspended
#   Reason: Under review

sorcha credential suspend --id cred-001 --wallet addr1... --output json
# Output: { "credentialId": "cred-001", "status": "Suspended", ... }
```

### Scenario 5: CLI Participant Publish

```bash
sorcha participant publish --org-id <guid> --register-id reg-001 --name "Alice" --org-name "Acme" --wallet addr1... --signer addr2...
# Output: Participant published successfully!
#   Register: reg-001
#   Status:   Accepted (202)
```

## YARP Route Verification

Before implementation, verify these API Gateway routes exist:

- `/api/v1/credentials/{**catch-all}` → Blueprint Service (credential lifecycle)
- `/api/v1/credentials/status-lists/{**catch-all}` → Blueprint Service (status lists)
- `/api/v1/presentations/{**catch-all}` → Wallet Service (OID4VP)
- `/api/organizations/{**catch-all}` → Tenant Service (participant operations)
