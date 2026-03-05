# CLI Command Contracts (Feature 049)

## Register Policy Commands

### `register policy get`
```
sorcha register policy get --register-id <id> [--output table|json]
```
**Refit**: `GET /api/registers/{registerId}/policy` → `RegisterPolicyResponse`
**Table output**: Policy fields as key-value pairs
**JSON output**: Full `RegisterPolicyResponse` serialized

### `register policy history`
```
sorcha register policy history --register-id <id> [--page 1] [--page-size 20] [--output table|json]
```
**Refit**: `GET /api/registers/{registerId}/policy/history?page={p}&pageSize={ps}` → `PolicyHistoryResponse`
**Table output**: Version | UpdatedBy | UpdatedAt columns
**JSON output**: Full response with pagination metadata

### `register policy update`
```
sorcha register policy update --register-id <id> [--min-validators N] [--max-validators N] [--signature-threshold N] [--registration-mode open|consent] [--yes]
```
**Refit**: `POST /api/registers/{registerId}/policy/update` → `PolicyUpdateResponse`
**Confirmation**: Required unless `--yes` flag
**Output**: Proposed version number and governance vote status

---

## Register System Commands

### `register system status`
```
sorcha register system status [--output table|json]
```
**Refit**: `GET /api/system-register` → anonymous object
**Table output**: Key-value pairs (ID, Name, Initialized, BlueprintCount, CreatedAt)

### `register system blueprints`
```
sorcha register system blueprints [--page 1] [--page-size 20] [--output table|json]
```
**Refit**: `GET /api/system-register/blueprints?page={p}&pageSize={ps}` → `PaginatedBlueprintResponse`
**Table output**: BlueprintId | Version | PublishedAt | PublishedBy | Active columns

---

## Validator Consent Commands

### `validator consent pending`
```
sorcha validator consent pending --register-id <id> [--output table|json]
```
**Refit**: `GET /api/validators/{registerId}/pending` → list of pending validators
**Table output**: ValidatorId | RequestedAt columns

### `validator consent approve`
```
sorcha validator consent approve --register-id <id> --validator-id <vid> [--yes]
```
**Refit**: `POST /api/validators/{registerId}/{validatorId}/approve` → success response
**Confirmation**: Required unless `--yes`

### `validator consent reject`
```
sorcha validator consent reject --register-id <id> --validator-id <vid> [--reason "..."] [--yes]
```
**Refit**: `POST /api/validators/{registerId}/{validatorId}/reject` → success response
**Confirmation**: Required unless `--yes`

### `validator consent refresh`
```
sorcha validator consent refresh --register-id <id> [--output table|json]
```
**Refit**: `POST /api/validators/{registerId}/refresh` → approved validators list

---

## Validator Metrics Commands

### `validator metrics` (aggregated)
```
sorcha validator metrics [--output table|json]
```
**Refit**: `GET /api/metrics` → `AggregatedMetrics`
**Table output**: KPI summary (success rate, dockets proposed, queue depth, cache ratio)

### `validator metrics validation`
```
sorcha validator metrics validation [--output table|json]
```
**Refit**: `GET /api/metrics/validation` → `ValidationMetricsResponse`

### `validator metrics consensus`
```
sorcha validator metrics consensus [--output table|json]
```
**Refit**: `GET /api/metrics/consensus` → `ConsensusMetricsResponse`

### `validator metrics pools`
```
sorcha validator metrics pools [--output table|json]
```
**Refit**: `GET /api/metrics/pools` → `PoolMetricsResponse`

### `validator metrics caches`
```
sorcha validator metrics caches [--output table|json]
```
**Refit**: `GET /api/metrics/caches` → `CacheMetricsResponse`

### `validator metrics config`
```
sorcha validator metrics config [--output table|json]
```
**Refit**: `GET /api/metrics/config` → `ConfigurationResponse`

---

## Validator Threshold Commands

### `validator threshold status`
```
sorcha validator threshold status --register-id <id> [--output table|json]
```
**Refit**: `GET /api/validators/threshold/{registerId}/status` → threshold status

### `validator threshold setup`
```
sorcha validator threshold setup --register-id <id> --threshold <t> --total-validators <n> --validator-ids <id1,id2,...> [--yes]
```
**Refit**: `POST /api/validators/threshold/setup` → `ThresholdSetupResponse`
**Confirmation**: Required unless `--yes`

---

## CLI Refit Interface Extensions

### IRegisterServiceClient (add)
```csharp
[Get("/api/registers/{registerId}/policy")]
Task<RegisterPolicyResponse> GetPolicyAsync(string registerId, [Header("Authorization")] string auth);

[Get("/api/registers/{registerId}/policy/history")]
Task<PolicyHistoryResponse> GetPolicyHistoryAsync(string registerId, [Query] int? page, [Query] int? pageSize, [Header("Authorization")] string auth);

[Post("/api/registers/{registerId}/policy/update")]
Task<PolicyUpdateResponse> ProposePolicyUpdateAsync(string registerId, [Body] PolicyUpdateRequest request, [Header("Authorization")] string auth);

[Get("/api/system-register")]
Task<HttpResponseMessage> GetSystemRegisterStatusAsync([Header("Authorization")] string auth);

[Get("/api/system-register/blueprints")]
Task<PaginatedBlueprintResponse> GetSystemRegisterBlueprintsAsync([Query] int? page, [Query] int? pageSize, [Header("Authorization")] string auth);
```

### IValidatorServiceClient (add)
```csharp
// Metrics
[Get("/api/metrics")]
Task<HttpResponseMessage> GetAggregatedMetricsAsync([Header("Authorization")] string auth);

[Get("/api/metrics/validation")]
Task<HttpResponseMessage> GetValidationMetricsAsync([Header("Authorization")] string auth);

[Get("/api/metrics/consensus")]
Task<HttpResponseMessage> GetConsensusMetricsAsync([Header("Authorization")] string auth);

[Get("/api/metrics/pools")]
Task<HttpResponseMessage> GetPoolMetricsAsync([Header("Authorization")] string auth);

[Get("/api/metrics/caches")]
Task<HttpResponseMessage> GetCacheMetricsAsync([Header("Authorization")] string auth);

[Get("/api/metrics/config")]
Task<HttpResponseMessage> GetConfigMetricsAsync([Header("Authorization")] string auth);

// Consent
[Get("/api/validators/{registerId}/pending")]
Task<HttpResponseMessage> GetPendingValidatorsAsync(string registerId, [Header("Authorization")] string auth);

[Post("/api/validators/{registerId}/{validatorId}/approve")]
Task<HttpResponseMessage> ApproveValidatorAsync(string registerId, string validatorId, [Header("Authorization")] string auth);

[Post("/api/validators/{registerId}/{validatorId}/reject")]
Task<HttpResponseMessage> RejectValidatorAsync(string registerId, string validatorId, [Header("Authorization")] string auth);

[Post("/api/validators/{registerId}/refresh")]
Task<HttpResponseMessage> RefreshValidatorsAsync(string registerId, [Header("Authorization")] string auth);

// Threshold
[Get("/api/validators/threshold/{registerId}/status")]
Task<HttpResponseMessage> GetThresholdStatusAsync(string registerId, [Header("Authorization")] string auth);

[Post("/api/validators/threshold/setup")]
Task<HttpResponseMessage> SetupThresholdAsync([Body] ThresholdSetupRequest request, [Header("Authorization")] string auth);
```
