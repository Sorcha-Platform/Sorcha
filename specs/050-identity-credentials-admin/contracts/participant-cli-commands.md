# Contract: Participant CLI Commands

## participant suspend

- **Options**: `--org-id` (required), `--id` (required)
- **Refit**: `POST /api/organizations/{orgId}/participants/{id}/suspend`
- **Output**: Success message with participant ID and new status

## participant reactivate

- **Options**: `--org-id` (required), `--id` (required)
- **Refit**: `POST /api/organizations/{orgId}/participants/{id}/reactivate`
- **Output**: Success message with participant ID and new status

## participant publish

- **Options**: `--org-id` (required), `--register-id` (required), `--name` (required), `--org-name` (required), `--wallet` (required), `--signer` (required)
- **Refit**: `POST /api/organizations/{orgId}/participants/publish` with `PublishParticipantRequest` body
- **Output**: Table with participant ID, register ID, status, transaction ID

## participant unpublish

- **Options**: `--org-id` (required), `--id` (required), `--register-id` (required), `--signer` (required)
- **Refit**: `DELETE /api/organizations/{orgId}/participants/publish/{id}?registerId=...&signerWalletAddress=...`
- **Output**: Success message with participant ID and register ID

## IParticipantServiceClient Refit Extensions

```
[Post("/api/organizations/{orgId}/participants/{id}/suspend")]
Task SuspendParticipantAsync(string orgId, string id, [Header("Authorization")] string token);

[Post("/api/organizations/{orgId}/participants/{id}/reactivate")]
Task ReactivateParticipantAsync(string orgId, string id, [Header("Authorization")] string token);

[Post("/api/organizations/{orgId}/participants/publish")]
Task<PublishParticipantResult> PublishParticipantAsync(string orgId, [Body] PublishParticipantRequest request, [Header("Authorization")] string token);

[Delete("/api/organizations/{orgId}/participants/publish/{id}")]
Task<PublishParticipantResult> UnpublishParticipantAsync(string orgId, string id, [Query] string registerId, [Query] string signerWalletAddress, [Header("Authorization")] string token);
```
