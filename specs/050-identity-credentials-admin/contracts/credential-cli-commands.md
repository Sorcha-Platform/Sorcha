# Contract: Credential CLI Commands

## credential suspend

- **Options**: `--id` (required), `--wallet` (required), `--reason` (optional)
- **Refit**: `POST /api/v1/credentials/{id}/suspend` with `LifecycleCredentialRequest` body
- **Output**: Table with credential ID, new status, performed by, reason

## credential reinstate

- **Options**: `--id` (required), `--wallet` (required), `--reason` (optional)
- **Refit**: `POST /api/v1/credentials/{id}/reinstate` with `LifecycleCredentialRequest` body
- **Output**: Table with credential ID, new status, performed by, reason

## credential refresh

- **Options**: `--id` (required), `--wallet` (required), `--expires-in-days` (optional)
- **Refit**: `POST /api/v1/credentials/{id}/refresh` with `RefreshCredentialRequest` body
- **Output**: Table with old credential ID, new credential ID, new expiry

## credential status-list get

- **Options**: `--id` (required)
- **Refit**: `GET /api/v1/credentials/status-lists/{id}`
- **Output**: Table with list ID, purpose, issuer, valid from; JSON mode shows full W3C document

## ICredentialServiceClient Refit Extensions

```
[Post("/api/v1/credentials/{credentialId}/suspend")]
Task<CredentialLifecycleResponse> SuspendCredentialAsync(string credentialId, [Body] LifecycleRequest request, [Header("Authorization")] string token);

[Post("/api/v1/credentials/{credentialId}/reinstate")]
Task<CredentialLifecycleResponse> ReinstateCredentialAsync(string credentialId, [Body] LifecycleRequest request, [Header("Authorization")] string token);

[Post("/api/v1/credentials/{credentialId}/refresh")]
Task<RefreshCredentialResponse> RefreshCredentialAsync(string credentialId, [Body] RefreshRequest request, [Header("Authorization")] string token);

[Get("/api/v1/credentials/status-lists/{listId}")]
Task<StatusListResponse> GetStatusListAsync(string listId);
```
