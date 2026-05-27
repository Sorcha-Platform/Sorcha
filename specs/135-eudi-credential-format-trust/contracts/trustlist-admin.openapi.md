# Contract: Trust-list snapshot management (Tenant Service)

Operator-facing surface to upload/version external trust-anchor snapshots consulted by the `trustlist` / `x509-lotl` trust source (FR-017). Shipped provider = operator snapshot; live LOTL fetch is a future provider behind the same `ITrustListProvider` seam.

## Endpoints (Scalar-documented, JWT admin-scoped, RateLimitPolicies.Strict)

### `PUT /api/v1/trust/trustlists/{trustListId}`

Upload/replace a trust-list snapshot.

Request (`application/json`):
```jsonc
{
  "source": "EU LOTL 2026-Q2 manual export",
  "roots": ["<base64 DER root cert>", "..."],
  "freshness": "2026-04-30T00:00:00Z"   // operator-asserted as-of time
}
```
Response `200`:
```jsonc
{ "trustListId": "eu-lotl-2026q2", "rootCount": 12, "createdAt": "…", "freshness": "…" }
```

### `GET /api/v1/trust/trustlists/{trustListId}`

Returns snapshot metadata (id, root count, source, createdAt, freshness) — **not** used per-verification (the provider caches), but lets operators audit what is loaded.

### `GET /api/v1/trust/trustlists`

Lists available snapshot ids + freshness.

## Rules

- `trustListId` referenced by a `TrustPolicy` `trustlist` source MUST exist or evaluation fails closed (`SourceUnavailable`).
- Snapshot `id` + `freshness` are copied into `TrustEvidence` on every decision that used the list (FR-014/015).
- Endpoints carry `.WithSummary(...)` + `.WithDescription(...)` (FR-023).

## Acceptance mapping

- FR-017, US2 scenario 1 (trust list naming), SC-005 (freshness in evidence).
