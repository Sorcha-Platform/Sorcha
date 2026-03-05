# Contract: Credential Lifecycle UI Service

## ICredentialApiService Extensions

New methods added to existing `ICredentialApiService` interface.

### SuspendCredentialAsync

- **Input**: credentialId (string), issuerWallet (string), reason (string?)
- **HTTP**: POST `/api/v1/credentials/{credentialId}/suspend`
- **Body**: `{ "issuerWallet": "...", "reason": "..." }`
- **Success**: Returns CredentialLifecycleResult with NewStatus = "Suspended"
- **Errors**: 400 (wrong state), 403 (not issuer), 404 (not found)

### ReinstateCredentialAsync

- **Input**: credentialId (string), issuerWallet (string), reason (string?)
- **HTTP**: POST `/api/v1/credentials/{credentialId}/reinstate`
- **Body**: `{ "issuerWallet": "...", "reason": "..." }`
- **Success**: Returns CredentialLifecycleResult with NewStatus = "Active"
- **Errors**: 400 (wrong state), 403 (not issuer), 404 (not found)

### RefreshCredentialAsync

- **Input**: credentialId (string), issuerWallet (string), newExpiryDuration (string?)
- **HTTP**: POST `/api/v1/credentials/{credentialId}/refresh`
- **Body**: `{ "issuerWallet": "...", "newExpiryDuration": "P365D" }`
- **Success**: Returns CredentialLifecycleResult with NewCredentialId populated
- **Errors**: 400 (wrong state), 403 (not issuer), 404 (not found)

## Test Requirements

Per method: success, wrong-state error, forbidden error, network error = 12 tests minimum.
