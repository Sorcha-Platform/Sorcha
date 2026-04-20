# Contract: `x-file` capture + token-resize extension

**Feature**: 107-assured-identity-v1
**Status**: Extension to existing Feature 085 schema extension

## Purpose

Extend the existing Feature 085 `x-file` schema extension with two new fields that let a blueprint declare a portrait-capture intent: `capture` (advise device camera) and `embedAs` (advise client-side resize for credential embedding).

## Schema delta

Existing `x-file` schema (Feature 085):

```jsonc
{
  "x-file": {
    "accept": ["image/jpeg", "image/png"],
    "maxSizePerFile": "5MB",
    "maxChunks": 1
  }
}
```

New fields added in v1:

```jsonc
{
  "x-file": {
    "accept": ["image/jpeg"],
    "maxSizePerFile": "5MB",
    "maxChunks": 1,
    "capture": "user",
    "embedAs": "image-token-jpeg-240x320"
  }
}
```

| Field | Type | Required | Default | Notes |
|---|---|---|---|---|
| `capture` | enum: `user` \| `environment` \| null | no | null | `user` = front-facing (selfie), `environment` = rear. Renders `capture` HTML attribute on `<InputFile>` for mobile camera access. Null = legacy behaviour (no capture hint). |
| `embedAs` | enum: `image-token-jpeg-240x320` \| null | no | null | When set, the renderer produces a resized token JPEG client-side alongside the full original. The token lands in the action payload as a base64 string at `<field>/tokenImageBase64`. The full original lands via the existing chunked-file pipeline. |

Unknown values for either field produce a publish-time warning; the renderer falls back to legacy behaviour (no capture hint, no resize).

## Renderer changes (FileRenderer.razor)

When `capture` is non-null:
- The rendered `<InputFile>` element gets a `capture` HTML attribute matching the value (`capture="user"` or `capture="environment"`)
- On mobile devices, the device camera opens with the requested facing
- On desktop, the file picker opens normally (capture attribute is mobile-only per HTML spec)

When `embedAs` is set:
- After the citizen picks/captures a file, the renderer invokes `PhotoTokenResizer.ResizeAsync(file, targetDimensions, qualityHint)` via Blazor JS interop
- The resizer runs on the browser's `<canvas>` element: draws the source image, resizes proportionally to fit 240×320 (cover-style), exports as JPEG with progressive quality reduction until the result is ≤ 20KB
- The resized token is added to the form payload at `<field>/tokenImageBase64` as a base64 string
- The full original is uploaded via the existing chunked-file pipeline; the resulting `chunkTransactionIds` land at `<field>/fullOriginalChunkIds`
- ICAO composition advice is rendered in a sibling panel (only when `embedAs` is set, since composition guidance is portrait-specific)

## Action payload shape (after submission)

Action payload field for the portrait:

```jsonc
{
  "portrait": {
    "fullOriginalChunkIds": ["tx-abc...", "tx-def..."],
    "tokenImageBase64": "/9j/4AAQSkZJ...",  // ≤ 20KB raw, ≤ 27KB base64
    "contentType": "image/jpeg",
    "hash": "a1b2c3..."  // SHA-256 hex over the full original
  }
}
```

When the citizen does not provide a photo, the entire `portrait` field is absent from the payload (not present-but-empty).

## Issuance-time behaviour (ActionExecutionService)

The credential's `claimMappings` references the token by JSON pointer:

```jsonc
{
  "claimName": "portrait",
  "sourceField": "/portrait/tokenImageBase64"
}
```

Validation at issuance time:
- If `/portrait/tokenImageBase64` is absent: `portrait` claim is omitted from the credential (credential issued without portrait — valid v1 behaviour)
- If `/portrait/tokenImageBase64` is present and ≤ 27KB base64: claim included
- If `/portrait/tokenImageBase64` is present but > 27KB: claim omitted, warning surfaced (`WARN_CRED_PORTRAIT_OVERSIZE_001`); credential issued without portrait

Server-side does not re-resize the image — the client is authoritative for the token. The server is the size gate.

## Acceptance

- A blueprint declaring `x-file: { capture: "user", embedAs: "image-token-jpeg-240x320", ... }` on a portrait field causes the renderer to default to the front-facing camera on mobile and to produce a token-image alongside the full original on submission.
- The credential issuance path embeds `portrait` from `/portrait/tokenImageBase64` when present and within size bounds.
- Citizens with no camera (desktop without webcam) get the file picker fallback and can still upload a photo.
- Citizens who skip the photo entirely receive a credential without the `portrait` claim — credential is still valid.

## Test surface

- `FileRendererCaptureTests` — `capture` attribute propagated to `<InputFile>` correctly
- `PhotoTokenResizerTests` — produces ≤20KB JPEG at 240×320; quality scaling logic
- Issuance unit tests in `BuildClaimsFromMappingsTests` — token included when present + valid; omitted when absent; omitted with warning when oversize
