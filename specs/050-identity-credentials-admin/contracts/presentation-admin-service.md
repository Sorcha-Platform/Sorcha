# Contract: Presentation Admin Service

## IPresentationAdminService (NEW)

### CreatePresentationRequestAsync

- **Input**: CreatePresentationRequestViewModel
- **HTTP**: POST `/api/v1/presentations/request`
- **Body**: `{ "credentialType": "...", "callbackUrl": "https://...", "requiredClaims": [...], ... }`
- **Success**: Returns PresentationRequestResultViewModel with QR URL
- **Errors**: 400 (validation), 401 (unauthorized)

### GetPresentationResultAsync

- **Input**: requestId (string)
- **HTTP**: GET `/api/v1/presentations/{requestId}/result`
- **Success (completed)**: 200 with verification result
- **Success (pending)**: 202 with status only
- **Errors**: 404, 410 (expired)

## UI Page Contracts

### /admin/presentations (Verifier Side)

- Create form: credential type, accepted issuers (tags), required claims (tags), target wallet, callback URL, TTL
- After creation: QR code display + shareable link + expiry countdown
- Request history table with status column

### Credentials Page — Holder Side (existing page, replace placeholder)

- Pending requests list using existing ICredentialApiService.GetPresentationRequestsAsync
- Request detail with approve/deny flow
- Approve: credential selection + claim checkboxes + confirm
- Deny: simple confirmation

## Test Requirements

- CreatePresentationRequestAsync: success, validation error, auth error = 3 tests
- GetPresentationResultAsync: completed, pending, expired, not found = 4 tests
