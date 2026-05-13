# Contract — File and Photo Upload Surface

## Components

The file-upload surface lives inside `Components/Forms/Controls/` — the schema-driven control that renders for fields with `format: "file-reference"` (Feature 085) and the `x-file` capture-and-embed extensions (Feature 107). The control's exact file name is determined during execution by inspecting `Sorcha.UI.Core/Components/Forms/Controls/`; the contract here describes the behaviour it must preserve.

| Component | Source path | Target path |
|-----------|-------------|-------------|
| File-reference control (from `Forms/Controls/`) | `Sorcha.UI.Core/Components/Forms/Controls/` | `Sorcha.UI.Components.User/Components/Forms/Controls/` |

## Schema-driven behaviour (preserved verbatim)

The control honours the schema extensions documented in Feature 085 and Feature 107:

- `format: "file-reference"` — switches the control into file-attachment mode
- `x-file.accept` — MIME-type allowlist
- `x-file.maxSizePerFile` — per-file size cap
- `x-file.maxChunks` — chunk count cap (Feature 085 chunked encryption)
- `x-file.capture: "user"` — request front-facing camera on mobile (Feature 107)
- `x-file.embedAs: "image-token-jpeg-240x320"` — client-side resizer producing a base64 JPEG token at `{fieldPointer}/tokenImageBase64` alongside the full-resolution chunked original

The control's outputs to the form value graph stay unchanged:
- `FileReference` object (fileName, contentType, size, hash, salt, chunkTransactionIds, masterKeyId)
- Optional `tokenImageBase64` companion when `embedAs` is set

## Injected services

The chunk-upload path consumes a service that POSTs to Blueprint Service's `/api/file-chunks` (Feature 085). The encryption (HKDF-SHA256 chunk keys + XChaCha20-Poly1305) happens server-side via Wallet Service; the client uploads pre-staging encrypted chunks. The exact service name is preserved during migration — no new abstraction.

Camera capture uses `IJSRuntime` to invoke a browser API (the PWA also bridges to a JS module under `wwwroot/`; the web app uses the same browser API but with desktop-keyboard-fallback messaging).

## Host responsibilities

1. Both shells already register `HttpClient` for chunk POSTs and `IJSRuntime` for camera invocation.
2. The PWA's host additionally registers any PWA-specific JS modules under its `wwwroot/` for native-feel capture — these JS modules are not part of the shared library; the library only invokes the standard browser API. (Future enhancement: a `wwwroot/` companion in the shared library that ships JS for capture — out of scope for this feature.)
3. Host pages bind the schema and observe `ValueChanged` from the parent `SorchaFormRenderer`; the file control's outputs surface through that.

## PWA differentiator (preserved capability)

The PWA-shell-only capabilities documented in the 2026-05-10 design note (NFC, Share Target, motion sensors, native camera, offline outbox) are NOT part of this shared file-upload contract. Those are host-shell capabilities that, when relevant to a future component, would be plugged in by the PWA via JS interop. The shared component just calls the browser's standard API; the PWA's native-feel wrapping is the host's concern.

## Out of contract

- Chunk-key derivation. Owned by Wallet Service (Feature 085 server-side).
- Server-side reassembly + decryption on download. Owned by Wallet Service `GET /api/v1/wallets/{address}/files/download`.
- Orphan-chunk cleanup. Server-side, time-window enforced.
- NFC / Share Target / sensor APIs. Host-shell capabilities, not component-level.

## Verification

1. **Given** a schema field with `format: "file-reference"`, **When** rendered inside `SorchaFormRenderer`, **Then** the file-input control appears with an upload affordance — verified by bUnit test.
2. **Given** a schema field with `x-file.capture: "user"`, **When** rendered on a mobile-emulated viewport in bUnit, **Then** the control includes the camera-capture affordance — verified by bUnit test.
3. **Given** a schema field with `x-file.embedAs: "image-token-jpeg-240x320"`, **When** a 2 MB image is captured, **Then** the produced form value carries both a `FileReference` and a `tokenImageBase64` ≤ 27 KB — verified by an existing test in `Sorcha.UI.Core.Tests` that moves to `Sorcha.UI.Components.User.Tests`.
