# Contract: Resolve published org DID document by DID (public)

**New endpoint — Tenant Service.** Lets a verifier fetch an org's published `did.json` from the issuer DID alone (`did:sorcha:org:{A}`), since the existing route is keyed only by org GUID. Backed by the existing `PrimaryDid` index.

```
GET /orgs/by-did/{did}/did.json
Accept: application/did+json
# anonymous (mirrors the existing GET /orgs/{orgId:guid}/did.json)
```

`{did}` is the URL-encoded `did:sorcha:org:{walletAddress}`.

### Responses

| Status | Body | Meaning |
|---|---|---|
| 200 | DID document (`application/did+json`) | Published doc whose `PrimaryDid == {did}`. Contains the `#vc-issuance-{n}` verification methods (issuance key C's `publicKeyJwk`) referenced from `assertionMethod`. |
| 404 | (problem) | No published document for that DID. Verifier resolution returns null → fail closed. |

### Server

- `OrgDidDocumentService.GetByPrimaryDidAsync(string did)` → `OrgDidDocuments.FirstOrDefault(d => d.PrimaryDid == did)` (indexed). The returned document `id` is already `{did}` (opaque address anchoring done in `IssuanceKeyService`).

### Consumer

- `SorchaDidResolver.ResolveOrgDidAsync` issues `GET /orgs/by-did/{urlencoded-did}/did.json` against the **Tenant** base address (3-arg ctor HttpClient). On 200 → parse to `DidDocument`; on 404/unreachable → return null (no local rebuild, no synthesized `#vc-issuance-1`). `did:sorcha:w:` (wallet) resolution is unchanged.

### Notes

- Exposes only already-public DID-document data.
- `.WithSummary()/.WithDescription()`; `application/did+json` content type to match the existing route.
