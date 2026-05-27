# Contract: `sorcha-holder-key` blueprint field extension (C3)

A blueprint-declared form field that the client auto-fills with the submitting citizen's public
delivery keys and writes into the starting-action payload. Mirrors the F103 `x-address-lookup` →
`PostcodeLookup` renderer-autofill idiom (NOT the form-block-driven `x-file` pattern).

## Schema declaration (in a starting action's JSON Schema)

```jsonc
{
  "holderKeys": {
    "type": "object",
    "format": "sorcha-holder-key",     // → ControlTypes.HolderKey via FormSchemaService.InferControlFromSchema
    "x-holder-key": { "required": true } // optional config; tolerated by the generic x- strip
  }
}
```

## Payload value written (read-only to the user)

```jsonc
"holderKeys": {
  "holderJwk": { "kty": "EC", "crv": "P-256", "x": "…", "y": "…" },
  "encryptionPublicKey": "<base64>",
  "algorithm": "ED25519"
}
```

## Behavioural contract

| Aspect | Requirement |
|--------|-------------|
| Recognition | `FormSchemaService.InferControlFromSchema` maps `format == "sorcha-holder-key"` → `ControlTypes.HolderKey`; `ControlDispatcher` renders `HolderKeyRenderer`. |
| Autofill source | `HolderKeyRenderer.OnInitializedAsync` calls `GET /api/v1/wallet/holder-keys` (contract: `holder-keys-endpoint.openapi.yaml`) and writes `/holderKeys/holderJwk`, `/holderKeys/encryptionPublicKey`, `/holderKeys/algorithm` via `FormContext.SetValue` (sibling fan-out, like `PostcodeLookupRenderer`). |
| User interaction | Field is display-only (shows "identity keys captured" affordance); the user cannot edit key material. |
| Private keys | Never present in the field, payload, or wire — public material only. |
| Submission | Values flow through `SorchaFormRenderer.HandleSubmit` → `FormSubmission.Data` → action payload → replicated register state. |
| Server read | `ActionExecutionService.IssueCredentialFromActionAsync` reads `/holderKeys/holderJwk` + `/holderKeys/encryptionPublicKey` via the existing `TryResolveJsonPointer` walker (no new extraction code). |
| Validation | `x-holder-key` auto-tolerated by `ValidationEngine` generic `x-` strip (`:1873`); unknown `format` validates as pass. **Build-time check**: confirm `SchemaValidator.cs` (no strip) is not on the action-data path; if it is, apply the same strip. Structural validation of the value shape is an optional opt-in validator (mirror `FileReferenceValidator`). |
| Missing/malformed | Submission may still seal (field is data); the **credential-issuance** step fails closed if neither a published participant record nor a usable carried key resolves (FR-012). |

## Out of scope (backlog)

- Proof-of-possession (a key-binding challenge proving the citizen controls the carried keys). Safe to omit in v1 because the open-participant submitter is the recipient (self-defeating to lie). Pairs with participant-record promotion.
