# Contract: `credentialIssuanceConfig.targetAudience = SorchaLocalWallet`

**Feature**: 106-register-native-credentials
**Surface**: Blueprint JSON schema — a blueprint action's `credentialIssuanceConfig` block
**Layer**: Publish-time validation + run-time engine dispatch
**Binds**: FR-001, FR-002, FR-003, FR-004, FR-020, FR-021, FR-022

## Wire shape

A blueprint author selects register-native delivery by setting `targetAudience: "SorchaLocalWallet"` on the issuing action. Example action fragment from a Verified Citizen-style blueprint:

```jsonc
{
  "id": 2,
  "title": "Review and issue credential",
  "sender": "verification-analyst",
  "requiredPriorActions": [1],
  "dataSchemas": [
    {
      "type": "object",
      "properties": {
        "verificationDecision": { "type": "string", "enum": ["approved", "rejected"] },
        "reviewerNotes": { "type": "string", "maxLength": 1000 }
      },
      "required": ["verificationDecision"]
    }
  ],
  "credentialIssuanceConfig": {
    "credentialType": "VerifiedCitizenCredential",
    "recipientParticipantId": "citizen",
    "targetAudience": "SorchaLocalWallet",
    "claimMappings": [
      { "from": "/previousData/1/payload/name/givenName",  "to": "givenName"  },
      { "from": "/previousData/1/payload/name/familyName", "to": "familyName" },
      { "from": "/previousData/1/payload/dob/dateOfBirth", "to": "dateOfBirth" },
      { "from": "/previousData/1/payload/email/email",     "to": "email" }
    ],
    "disclosable": ["givenName", "familyName", "dateOfBirth", "email"]
  },
  "disclosures": [
    { "participantAddress": "verification-analyst", "dataPointers": ["/*"] },
    { "participantAddress": "citizen", "dataPointers": ["/credential"] }
  ],
  "routes": [
    {
      "id": "approved",
      "nextActionIds": [3],
      "condition": { "==": [{ "var": "verificationDecision" }, "approved"] }
    },
    { "id": "rejected", "nextActionIds": [], "isDefault": true }
  ]
}
```

Key differences from the wave 14b pattern:

- `targetAudience` is `SorchaLocalWallet` rather than `HaipExternalWallet`.
- No `outputMapping` on the approval route — the credential travels via the recipient-addressed disclosure, not via action 3's prepopulated payload.
- Action 3 still exists as the symbolic terminal accept/reject action, but its `dataSchema` can be an empty object — it no longer carries a `credentialOffer` shape.

## Publish-time validation (new rules)

When the Blueprint Service's publish-time validator processes an action with `credentialIssuanceConfig.targetAudience = SorchaLocalWallet`, it MUST enforce:

### VAL_BP_CRED_001 — recipient participant must resolve

**Rule**: `recipientParticipantId` MUST match a `participant.id` declared at the blueprint root.

**Error shape** (publish-time):
```json
{
  "errorCode": "VAL_BP_CRED_001",
  "actionId": 2,
  "field": "credentialIssuanceConfig.recipientParticipantId",
  "message": "Recipient participant 'citizen-typo' is not declared in the blueprint. When targetAudience is SorchaLocalWallet, the recipient must be a known participant."
}
```

**Rationale**: The engine needs to look up the recipient's wallet at execute time via `instance.ParticipantWallets[recipientParticipantId]`. A typo in the recipient id would otherwise surface as a runtime failure after the assessor has already clicked approve — better to fail at publish.

### VAL_BP_CRED_002 — recipient should have a disclosure

**Rule**: The action SHOULD declare a `disclosures` entry with `participantAddress == recipientParticipantId`. If omitted, the engine will synthesise a disclosure at runtime to carry the credential payload, but the author's intent is clearer when it's explicit.

**Warning shape** (publish-time, non-blocking):
```json
{
  "warningCode": "WARN_BP_CRED_002",
  "actionId": 2,
  "message": "Action 2 uses SorchaLocalWallet target audience but does not declare a disclosure for recipient participant 'citizen'. The engine will auto-create one at runtime, but explicit disclosures are recommended for clarity and audit."
}
```

**Rationale**: Warning, not error, because auto-synthesis is supported and deterministic. But explicit > implicit.

### VAL_BP_CRED_003 — required fields remain required

**Rule**: `credentialType`, `claimMappings`, `recipientParticipantId` are required regardless of `targetAudience` value. `SorchaLocalWallet` does not relax any existing requirements.

**Error shape**: Existing `VAL_BP_CRED_*` errors for missing fields continue to apply unchanged.

## Runtime engine dispatch

When `ActionExecutionService.ExecuteAsync` processes an action whose `credentialIssuanceConfig.targetAudience == SorchaLocalWallet`, it:

1. Resolves the recipient wallet address via `instance.ParticipantWallets[recipientParticipantId]`. If unbound, throws an execution error — recipient wallet MUST exist before the credential can be minted. (In the open-participant case, the recipient is late-bound earlier in the instance's life; by the time Action 2 runs, the binding is canonical.)

2. Fetches the recipient wallet's public key via `IWalletServiceClient.GetWalletAsync(address)`. If the wallet cannot be resolved (e.g. the Wallet Service is on a different node and hasn't synced the recipient's wallet yet), the engine MUST NOT proceed — it surfaces a `VAL_RUNTIME_CRED_001` error and leaves the instance in its pre-execution state. The assessor can retry later.

3. Constructs the credential claims by applying `claimMappings` against the instance's accumulated data (existing code path — unchanged).

4. Calls `IHaipCredentialMinter.MintCredentialAsync` with:
   - `issuerDid = <resolved issuer DID>`
   - `holderJwk = { kty: OKP, crv: Ed25519, x: <recipient wallet pubkey> }` (or the equivalent for NIST-P256 wallets)
   - `credentialType`, `claims`, `disclosablePaths`, `signingKey`, `algorithm`, `expiresAt`

   Same minter, same inputs, same SD-JWT VC output as the `HaipExternalWallet` path.

5. Builds a `DisclosureGroup` targeted at the recipient:
   ```csharp
   new DisclosureGroup
   {
     Recipients = [recipientWalletAddress],
     Payload = new {
       Type = "credential-offer-v1",
       CredentialId = mintedCredential.Id,
       CredentialType = mintedCredential.Type,
       IssuerDid = mintedCredential.IssuerDid,
       RawToken = mintedCredential.RawToken,
       ExpiresAt = mintedCredential.ExpiresAt
     }
   }
   ```

6. Calls `IEncryptionPipelineService.EncryptDisclosedPayloadsAsync` with the disclosure group. This returns an `EncryptedPayloadGroup` wrapping the credential payload with a per-recipient X25519 wrap + XChaCha20-Poly1305 AEAD.

7. Merges the encrypted group into the action's `Disclosures` under the `/credential` pointer. The rest of the action's disclosures (e.g. the verification-analyst's full view of `/verificationDecision` and `/reviewerNotes`) remain unchanged.

8. Proceeds with the normal execute path: transaction construction, validator submission, docket confirmation, route evaluation, next-action resolution.

### Runtime error codes (new)

| Code | Meaning | When |
|---|---|---|
| `VAL_RUNTIME_CRED_001` | Recipient wallet not resolvable via Wallet Service | Engine couldn't fetch the recipient's pubkey (e.g. wallet unknown on this node) |
| `VAL_RUNTIME_CRED_002` | Credential mint failed | `HaipCredentialMinter.MintCredentialAsync` returned an error — unchanged from existing behaviour |
| `VAL_RUNTIME_CRED_003` | Encryption pipeline failed | `EncryptDisclosedPayloadsAsync` threw — e.g. payload too large for AEAD; surfaces the underlying error |

All runtime errors leave the instance in its pre-execution state so the caller can retry.

## Backwards compatibility

- Blueprints with `targetAudience = HaipExternalWallet` continue through the unchanged HAIP path. Zero behaviour change.
- Blueprints without any `targetAudience` (or explicitly `SorchaInternal`) also continue through their current code path. Feature 106 does NOT redirect `SorchaInternal` to the new branch — new branch is opt-in only.
- Wave 14b blueprints already published and in-flight continue through their original delivery mode. No migration of in-flight instances.

## Testing contract

- **Unit tests** (`Sorcha.Blueprint.Models.Tests` or equivalent): serialise/deserialise a blueprint JSON containing `targetAudience: "SorchaLocalWallet"` round-trip; assert the enum value maps correctly.
- **Publish-time validation tests**: assert `VAL_BP_CRED_001` fires on typo'd recipient, `WARN_BP_CRED_002` fires on missing explicit disclosure, existing required-field validation still fires.
- **Runtime dispatch tests**: mock the minter + encryption pipeline, assert `ActionExecutionService.ExecuteAsync` with a `SorchaLocalWallet` action calls them in the right order with the right inputs, and the resulting transaction carries the expected disclosure structure.
- **Integration test**: end-to-end against a real encryption pipeline — seal an action, read the transaction back, assert the `/credential` disclosure can be decrypted with the recipient's private key and decodes to a valid SD-JWT VC.
