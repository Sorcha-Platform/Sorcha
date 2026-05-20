# Contract: format + trustAnchor on CredentialIssuanceConfig

JSON shape on a blueprint action's `credentialIssuance`.

## Schema (informal)

```jsonc
{
  "credentialType": "PersonIdentificationData",   // → mdoc docType when format=mso_mdoc
  "recipientParticipantId": "citizen",
  "claimMappings": [ /* unchanged; for mdoc, claimName maps to (namespace, element) */ ],
  "disclosable": ["birth_date"],
  "expiryDuration": "P1Y",
  "targetAudience": "HaipExternalWallet",          // unchanged discriminator
  "format": "mso_mdoc",                            // NEW: "sd-jwt-vc" (default) | "mso_mdoc"
  "trustAnchor": "x509-tenant"                     // NEW: "register" (default) | "x509-tenant" | "x509-lotl"
}
```

## Rules

- `format=mso_mdoc` ⇒ `credentialType` MUST resolve to an mdoc `docType` and claim mappings to `(namespace, element)` pairs (FR-004). Known doctypes: PID `eu.europa.ec.eudi.pid.1`, mDL `org.iso.18013.5.1`.
- `trustAnchor=register` ⇒ no certificate chain attached; credential is DID-verifiable.
- `trustAnchor∈{x509-tenant,x509-lotl}` ⇒ the org cert chain MUST be resolved and attached (SD-JWT `x5c` header / mdoc COSE `x5chain`). If the chain cannot be resolved, **minting fails closed** (FR-020/022) — no chainless credential is issued under an X.509 anchor.
- An unsupported `format`/`trustAnchor` combination for the issuer's provisioned keys ⇒ configuration error, never a silent substitution (FR-022).

## Acceptance mapping

- US3 scenarios 1–4, FR-018/019/020/021/022.
