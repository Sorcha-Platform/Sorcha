# Contract: mso_mdoc presentation over OpenID4VP (HAIP Service)

mdoc verification reuses the **existing** OpenID4VP `direct_post` surface (specs 097/098) — no new public endpoint shape; the `vp_token` now also carries `mso_mdoc`. Documented here for the OpenAPI surface (Scalar, FR-023).

## Request authorization (DCQL, format `mso_mdoc`)

The authorization/presentation request advertises an mso_mdoc credential query:

```jsonc
{
  "dcql_query": {
    "credentials": [{
      "id": "pid",
      "format": "mso_mdoc",
      "meta": { "doctype_value": "eu.europa.ec.eudi.pid.1" },
      "claims": [
        { "path": ["eu.europa.ec.eudi.pid.1", "family_name"] },
        { "path": ["eu.europa.ec.eudi.pid.1", "birth_date"], "intent_to_retain": false }
      ]
    }]
  },
  "nonce": "…", "client_id": "x509_san_dns:verifier.example", "response_uri": "https://…/cb"
}
```

## Response (`POST {response_uri}`, `application/x-www-form-urlencoded`)

```
vp_token = { "pid": ["<base64url(DeviceResponse CBOR)>"] }
```

## Verification pipeline (MdocPresentationVerifier → ITrustEvaluator)

1. base64url-decode → CBOR-decode `DeviceResponse`; for each `Document`:
2. Verify `issuerAuth` (COSE_Sign1 over tag-24 MSO); resolve issuer key from `x5chain` (label 33) or DID.
3. Recompute `valueDigests` over each disclosed `IssuerSignedItemBytes`; reject on mismatch (`IntegrityFailure`).
4. Reconstruct `SessionTranscript = [null, null, OpenID4VPHandover(clientId, nonce, jwkThumbprint?, responseUri)]`; verify `DeviceAuth` (signature/MAC) over `DeviceAuthentication`; reject on mismatch (`HolderBindingInvalid`).
5. Check MSO `status.status_list` via `IStatusListChecker` (fail-closed).
6. Hand issuer + assurance context to `ITrustEvaluator.EvaluateAsync(policy)` → `TrustDecision`.
7. Surface disclosed elements to the workflow as claims (same shape as SD-JWT VC).

## Endpoint doc requirements

- `.WithSummary("Accept an OpenID4VP presentation (SD-JWT VC or mso_mdoc)")` + `.WithDescription(...)`.
- 200 accepted (+ TrustEvidence on the receipt), 400 malformed, 422 trust/verification failure with `TrustFailureReason`.

## Acceptance mapping

- US2 scenarios 1–5, FR-002/003/004/016, SC-003.
