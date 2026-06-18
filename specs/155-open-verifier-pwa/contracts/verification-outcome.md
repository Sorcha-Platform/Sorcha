# Contract — Enriched verification outcome (engine ↔ verifier UI)

The verifier engine produces a `VerificationOutcome` consumed by the verifier app's `/status` endpoint
and the `Outcome.razor` verdict screen. Feature 155 adds the per-layer breakdown.

## VerificationOutcome (engine)

```
VerificationOutcome {
  Accepted: bool                                  // unchanged — overall presentation accept
  DisclosedClaims: IReadOnlyDictionary<string,object?>  // unchanged
  Errors: IReadOnlyList<string>                   // unchanged — kept for back-compat
  CompletedAt: DateTimeOffset                     // unchanged
  IssuerSignature: IssuerSignatureStatus          // unchanged (NotVerified | Verified)
  Layers: IReadOnlyList<ValidationLayerResult>    // NEW (default [])
}

ValidationLayerResult {
  Layer:   LivePresentation | IssuerSignature | Revocation | RegisterAnchor
  Status:  Pass | Fail | Unverified
  Headline: string
  Detail:   IReadOnlyDictionary<string,string>
}
```

### Population responsibility

| Layer | Populated by | From |
|---|---|---|
| LivePresentation | engine validator | KB-JWT nonce/aud/freshness + delegation chain |
| IssuerSignature | engine validator | `IIssuerKeyResolver` + JWS verify; `Unverified` when key unresolved and `requireIssuerSignature:false` |
| Revocation | engine validator | `IStatusListCache.CheckAsync` verdict (Active→Pass, Revoked→Fail, Unverifiable→Unverified) |
| RegisterAnchor | **verifier app** (not engine) | `IRegisterAnchorClient.CheckAsync` → appended to a copy of the outcome before rendering |

Selective-disclosure (disclosed vs withheld) is **not** a Layer — it is derived in the UI from
`DisclosedClaims` plus the session's requested claim set, and rendered as its own expandable block.

## /status response (verifier app)

`GET /verify/r/{sessionId}/status` extends `SessionStatusResponse` with the layer list so the polling UI
can render the trail without a second round-trip:

```
SessionStatusResponse {
  Status: "pending" | "accepted" | "rejected"   // unchanged
  Purpose: string
  Accepted: bool?
  Errors: IReadOnlyList<string>?
  DisclosedClaims: IReadOnlyDictionary<string,string?>?
  Layers: ValidationLayerResult[]?              // NEW
  Issuer: { displayName: string?, did: string? }?  // NEW — surfaced on the verdict (FR-015)
}
```

The RegisterAnchor layer is filled in lazily: the verdict screen shows it as a "tap to verify" action
(FR-008) that calls the verifier app, which calls the public anchor endpoint and verifies the proof, then
updates the layer to Pass/Fail/Unverified.

## Overall verdict rule (FR-013)

`OverallPass = Accepted AND no Layer has Status==Fail`. A `RegisterAnchor` (or any) layer of
`Unverified` does **not** flip the overall verdict to fail — it is shown as "could not determine",
visually distinct from a red Fail. A `Revocation==Fail` (revoked) **does** make the overall verdict fail
even when `IssuerSignature==Pass` (SC-005).
