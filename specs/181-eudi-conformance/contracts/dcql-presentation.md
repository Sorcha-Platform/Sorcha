# Contract: DCQL Presentation Dialect (routes unchanged — D1)

**Owner**: `Sorcha.Verifier.Engine/Dcql` (single builder/parser, FR-008). Consumers: HAIP VerifierEndpoints,
Blueprint SorchaWalletPresentationConsumer, desk/open verifier, Wallet PWA, Sorcha.Agent.

## 1. Request Object (fetched via existing `GET /api/v1/verifier/requests/{id}/request-object`)

Media type `application/oauth-authz-req+jwt` (unchanged, RFC 9101). Signed JWT; **JWS header gains
`x5c`** (verifier certificate chain, research R12). Payload:

```jsonc
{
  "iss": "x509_san_dns:verify.sorcha.example",     // == client_id
  "aud": "https://self-issued.me/v2",
  "iat": 1720620000, "exp": 1720620600,
  "response_type": "vp_token",
  "response_mode": "direct_post",
  "response_uri": "https://…/direct-post",          // unchanged route
  "client_id": "x509_san_dns:verify.sorcha.example",// prefixed final-spec form (was bare URL)
  "nonce": "…", "state": "…",
  "dcql_query": {                                   // REPLACES presentation_definition
    "credentials": [
      { "id": "identity", "format": "dc+sd-jwt",
        "meta": { "vct_values": ["https://sorcha.dev/vc/assured-identity/v1"] },
        "claims": [ { "path": ["givenName"] }, { "path": ["familyName"] } ] }
    ],
    "credential_sets": [
      { "options": [["identity"]], "required": true, "purpose": "Prove your identity" }
    ]
  }
}
```

Deep link (all internal producers converge on the request_uri form; inline-definition form retired, FR-026):
`openid4vp://authorize?client_id=x509_san_dns%3A{host}&request_uri={url-encoded request-object URL}`

## 2. direct_post body (existing `POST …/direct-post`, form-encoded)

| Field | Change |
|---|---|
| `vp_token` | now ALWAYS a JSON object `{ "<queryId>": ["<presentation>", …] }` for **both** formats. SD-JWT presentation string = `credentialJwt~disc1~…~kbJwt`; mdoc = base64url(DeviceResponse) (already this shape). |
| `presentation_submission` | **removed**. If present, or if `vp_token` is a bare compact string → `400 { "error": "LEGACY_DIALECT", "expected": "OpenID4VP 1.0 dcql" }` (FR-007). |
| `state` | unchanged |

KB-JWT `aud` = the full prefixed `client_id` string (mint + verify sides move together or presentations
self-reject).

## 3. Verification result (existing `GET …/requests/{id}/result`)

`VerificationResult` gains `perQuery: [{ queryId, outcome, credentialType, verifiedClaims }]`;
existing top-level fields keep their meaning (overall success = every required query/set satisfied,
FR-005). Unknown query id in vp_token → overall failure, reason `DCQL_UNKNOWN_QUERY_ID`.

## 4. Blueprint credentialRequirements → DCQL mapping

`CredentialRequirement { type, format, requiredClaims, trustPolicy }` maps to one `DcqlCredentialQuery`
(id = slugified requirement key). Multiple requirements on one action → multiple queries in one request.
An explicit alternative construct (`credentialRequirements` gaining an optional `anyOf` grouping) rides
`credential_sets`. Trust remains F135 `TrustPolicy` — DCQL `trusted_authorities` is NOT used (research R2).

## 5. Wallet-side validation order (PWA, FR-026 / research R13)

1. Parse deep link → require `request_uri` (inline `presentation_definition` → refuse `LEGACY_DIALECT`).
2. Fetch request object; verify JWS via `x5c` leaf (`REQUEST_OBJECT_INVALID` on failure).
3. Leaf SAN dNSName == host of `x509_san_dns:` client_id (`REQUEST_HOST_MISMATCH`).
4. Chain→anchor against cached trusted-list anchors ⇒ `VerifierAuthState` (Trusted / AuthenticUntrusted;
   anchors unavailable never blocks — FR-027).
5. Parse `dcql_query` → match → consent → build object-keyed `vp_token` → direct_post.

## 6. CI gate

`scripts/check-presentation-dialect.ps1`: fail on `presentation_definition|input_descriptors|presentation_submission`
under `src/` outside `.presentation-dialect-allowlist`; fail on new `"vc+sd-jwt"` typ **writes**
(verify-side acceptance list allowed). Wired as a workflow step (FR-009 / SC-008).
