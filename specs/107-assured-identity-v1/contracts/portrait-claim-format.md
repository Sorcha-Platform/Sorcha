# Contract: Portrait claim format

**Feature**: 107-assured-identity-v1
**Standards alignment**: ISO/IEC 19794-5 (token image), ISO 18013-5 (mDL portrait), ICAO Doc 9303 (composition guidance)

## Token image (embedded in credential)

| Property | Value |
|---|---|
| Dimensions | 240 × 320 pixels |
| Aspect ratio | 3:4 (portrait orientation) |
| Format | JPEG, sRGB colour |
| Target raw size | ≤ 20KB |
| Base64-encoded size | ≤ ~27KB (raw × 1.37 base64 overhead) |
| Quality (JPEG q-factor) | Variable; resizer reduces until size target met |
| Embedding | Base64 string as the `portrait` claim value in the SD-JWT VC |

Rationale: matches ISO/IEC 19794-5 token image dimensions and the mDL precedent of embedding portraits directly in the credential rather than referencing them by URL. Offline verification works without a network round-trip.

## Full-resolution original (kept on register, not embedded)

| Property | Value |
|---|---|
| Minimum dimensions | 480 × 640 (ISO 19794-5 full frontal image floor) |
| Maximum size | 5MB (per existing `x-file.maxSizePerFile`) |
| Format | JPEG or PNG (per existing `x-file.accept`) |
| Storage | Existing Feature 085 chunked-file pipeline (XChaCha20-Poly1305 encrypted at rest) |
| Disclosure | Visible to the assessor on the review screen; not embedded in the credential |
| Future use | Available for a real backend identity-validator service to consume for biometric matching |

## Composition guidance (advisory, not enforced in v1)

Rendered as a sibling panel next to the capture control when `x-file.embedAs: "image-token-jpeg-240x320"` is set:

- Plain light background
- Face centred, fills 70-80% of frame
- Neutral expression
- Eyes open, looking at camera
- No sunglasses
- No head covering (except religious)
- Even lighting, no shadows

Automated composition checking (face detection, background uniformity, quality scoring) is **deferred**. The assessor (human or agent) rejects bad photos in v1.

## Resize algorithm (client-side)

`PhotoTokenResizer` runs in the browser via Blazor JS interop using `<canvas>`:

1. Read source file as `HTMLImageElement`
2. Compute scale factor to fit 240×320 (cover-style: scale to fill, then centre-crop)
3. Draw on a 240×320 `<canvas>`
4. Export as JPEG at quality 0.85
5. If output > 20KB raw, re-export at quality 0.75; if still > 20KB, quality 0.65; etc.
6. Return base64 string

Quality floor: 0.5. If 240×320 at quality 0.5 still exceeds 20KB (very unusual for a photo), the resizer surfaces a "photo too detailed for token" error; the citizen is prompted to retake; the credential can still be issued without the portrait if they skip.

## Server-side validation (issuance)

Per `ActionExecutionService`'s claim builder:

- Read `/portrait/tokenImageBase64` from action payload
- If absent → omit `portrait` claim from credential (valid)
- If present and ≤ 27KB base64 → include claim
- If present and > 27KB base64 → omit claim, log warning `WARN_CRED_PORTRAIT_OVERSIZE_001`, surface to citizen, credential still issued

Server does not re-resize. Client is authoritative; server is the size gate.

## Why a token, not the original

| Concern | Token (240×320, ~20KB) | Original (480×640+, MBs) |
|---|---|---|
| Credential size | Stays under ~50KB total — fits comfortably in QR codes for HAIP presentation | 100KB+; QR codes become unwieldy |
| Offline verification | Works | Works |
| Biometric matching (future) | Sufficient for face matching at desk-check resolution | Better, but not embedded |
| Holder selective disclosure | Single SD claim, easy to withhold | Same |
| Privacy | Token image deliberately small — recognisable but not high-resolution; reduces re-identification risk if leaked | More privacy risk |

The original on the register is what a future automated identity-validator service consumes — that service then writes its decision to the credential, not the photo.

## Acceptance

- A captured photo lands in the action payload as both `fullOriginalChunkIds` (chunked-file pipeline) and `tokenImageBase64` (≤27KB)
- The issued credential's `portrait` claim contains the base64 token, no larger than the bound
- Citizens who skip the photo receive a credential without the `portrait` claim
- Photos that fail client-side resize (resizer floor reached) prompt the citizen to retake or skip
