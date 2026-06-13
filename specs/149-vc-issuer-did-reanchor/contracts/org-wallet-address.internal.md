# Contract: Resolve org canonical wallet address (internal)

**New endpoint — Tenant Service.** Lets the Wallet Service anchor the issuer DID on the org's canonical operational wallet (A).

```
GET /api/internal/orgs/{orgId:guid}/wallet-address
Authorization: Bearer <service principal>     # policy: RequireService (token_type==service AND aud==:service)
```

### Responses

| Status | Body | Meaning |
|---|---|---|
| 200 | `{ "walletAddress": "ws1q..." }` | Org found and has a canonical wallet (A). |
| 404 | (problem) | Org not found **OR** `WalletAddress` is null (not yet provisioned). Indistinguishable by design; the caller treats 404 as "no resolvable issuer identity → fail closed". |

### Client

`Sorcha.ServiceClients.Http/OrgInfo/IOrgInfoClient.cs`:

```csharp
public interface IOrgInfoClient
{
    /// <summary>Resolve the org's canonical operational wallet address (Organization.WalletAddress),
    /// or null if the org is unknown or not yet provisioned.</summary>
    Task<string?> ResolveCanonicalWalletAddressAsync(Guid organizationId, CancellationToken ct = default);
}
```

- 200 → `walletAddress`; 404/transport error → `null`.
- Registered against the Tenant base address (`ServiceClients:TenantService:Address`), service-principal auth, mirroring `OrgDidDocumentClient`.
- Consumed by `IssuanceKeyService` (Wallet). A `null` result → `GetActiveSigningMaterialAsync` yields no material → mint fails closed (D4).

### Notes

- XML-documented; `.WithSummary()/.WithDescription()` on the endpoint.
- Read-only; returns public material (a wallet address). No secrets.
