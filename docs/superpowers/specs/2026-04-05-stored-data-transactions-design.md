# Stored Data Transactions Design

**Date:** 2026-04-05
**Status:** Draft
**Feature:** File attachments as chunked transactions with HKDF encryption

---

## Problem

Sorcha workflows need to support binary file attachments — photos taken on-site, PDFs, evidence files, machine output — as first-class fields in blueprint action schemas. The current 4MB encrypted transaction limit and inline payload model don't scale to multi-megabyte files. Files must be encrypted, replicated across peers, and validated like any other transaction data.

## Approach: Transaction-Native Chunking

Files are chunked into ≤4MB transactions that flow through the existing validator → register pipeline. No new storage infrastructure. The system decides storage strategy transparently based on size — consumers see a uniform file reference regardless of whether the file is one chunk or ten.

Phase 1 implements chunking within existing storage providers (MongoDB/Postgres) with a 40MB ceiling. Phase 2 (future) adds blob storage backends to raise the limit without changing the external API contract.

---

## 1. File Reference Model & Schema

### Blueprint Schema Declaration

A file field uses JSON Schema `format: "file-reference"` with an `x-file` extension:

**Single file:**
```json
{
  "sitePhoto": {
    "type": "string",
    "format": "file-reference",
    "x-file": {
      "accept": ["image/jpeg", "image/png"],
      "maxSizePerFile": "16MB",
      "maxChunks": 10
    }
  }
}
```

**Multiple files (array):**
```json
{
  "sitePhotos": {
    "type": "array",
    "items": { "type": "string", "format": "file-reference" },
    "minItems": 1,
    "maxItems": 5,
    "x-file": {
      "accept": ["image/jpeg", "image/png"],
      "maxSizePerFile": "16MB",
      "maxChunks": 10
    }
  }
}
```

The UI renders array fields with an expandable "Add file" button, respecting `minItems`/`maxItems`.

### Runtime File Reference Value

The field value stored in the action payload at runtime:

```json
{
  "fileName": "site-inspection.pdf",
  "contentType": "application/pdf",
  "size": 8650752,
  "hash": "sha256:a1b2c3...",
  "salt": "<base64-random-salt>",
  "chunkTransactionIds": ["tx-abc1", "tx-abc2", "tx-abc3"],
  "masterKeyId": "parent-action-tx-id"
}
```

| Field | Purpose |
|-------|---------|
| `fileName` | Original filename |
| `contentType` | MIME type |
| `size` | Total file size in bytes |
| `hash` | SHA-256 of complete original file (pre-encryption), for reassembly integrity |
| `salt` | Random per-file salt for HKDF key derivation (base64-encoded) |
| `chunkTransactionIds` | Ordered array of chunk transaction IDs (1 for files ≤4MB, up to 10) |
| `masterKeyId` | References the parent action transaction where the wrapped master file key lives |

---

## 2. Chunking & Encryption

### Chunk Lifecycle

1. Client reads file, generates random 256-bit `MasterFileKey`
2. File split into ≤4MB chunks (last chunk may be smaller)
3. Each chunk encrypted with `HKDF-SHA256(MasterFileKey, salt=randomPerFileSalt, info="chunk-{n}")`
4. Each chunk submitted as a transaction with `PayloadType.Document`
5. Chunk transaction metadata:
   ```json
   {
     "type": "file-chunk",
     "parentActionId": null,
     "fileHash": "sha256:a1b2c3...",
     "chunkIndex": 0,
     "totalChunks": 3,
     "contentType": "application/pdf"
   }
   ```
6. `parentActionId` is null at submission time — the validator populates it when the action transaction is submitted and links chunks to the action before sealing the docket

### Key Wrapping

- `MasterFileKey` wrapped once per recipient in the parent action's payload (existing `Challenges` mechanism)
- Chunk transactions carry **no key wrapping** — just ciphertext + nonce
- Recipients derive chunk keys from the master key + chunk index
- HKDF is a well-established pattern (used in TLS 1.3) — no related-key weakness

### Reassembly (Download)

Handled by Wallet Service on behalf of the client (see Section 5).

### Size Enforcement

| Constraint | Value | Enforced by |
|-----------|-------|------------|
| Chunk size | 4MB max | Client + Validator |
| Chunks per file | 10 max | Validator |
| Total file size | 40MB max | Validator (sum of chunks) |
| Files per field | `maxItems` in schema | Validator |

---

## 3. Submission Flow & Validator Rules

### Staged Submission

```
Client                    Validator               Register
  │                          │                       │
  ├─ Submit chunk 0 ────────►├─ Validate tx ────────►├─ Store (pending docket)
  ├─ Submit chunk 1 ────────►├─ Validate tx ────────►├─ Store (pending docket)
  ├─ Submit chunk 2 ────────►├─ Validate tx ────────►├─ Store (pending docket)
  │                          │                       │
  ├─ Submit action ─────────►├─ Validate action ────►├─ Store (pending docket)
  │  (refs chunk tx IDs)     │  + chunk existence    │
  │                          │  + size/type checks   │
  │                          │                       │
  │                          ├─ Seal docket ─────────►├─ Docket contains:
  │                          │  (action + all chunks) │   action + chunks
  │                          │                       │
  │◄─── Receipt ─────────────┤◄── Signed proof ──────┤
```

Chunks are uploaded first. The client receives chunk transaction IDs, then submits the action referencing those IDs. This is consistent with the existing async transmission model.

### Validator Rules for File-Bearing Actions

1. **Chunk existence** — all `chunkTransactionIds` referenced in the file field must exist and be pending (not yet sealed in another docket)
2. **Chunk integrity** — each chunk transaction must have `type: "file-chunk"` metadata with matching `fileHash`
3. **Ordering** — chunk indices must be contiguous (0 to N-1), no gaps or duplicates
4. **Size compliance** — each chunk ≤4MB, total ≤ `maxSizePerFile` from schema, chunk count ≤ `maxChunks`
5. **MIME type** — `contentType` in chunk metadata must match one of the `accept` types in the schema
6. **Same-docket rule** — validator must not seal the action until all referenced chunks are available, then seals them together in the same docket
7. **Orphan timeout** — chunks not referenced by an action within 30 minutes are discarded

### What Validators Do NOT Do

- Decrypt file content (they don't have the key)
- Verify the file hash (recipient's responsibility on download)
- Generate thumbnails or previews

---

## 4. Storage & Retrieval API

### Storage

Chunks stored individually by their transaction ID using existing storage providers. No new storage abstraction for Phase 1 — MongoDB and Postgres handle 4MB documents fine. File metadata (name, type, size, chunk list) lives in the action payload itself.

### Retrieval

No new dedicated endpoints. The existing transaction payload retrieval endpoints serve chunk content. The file reference in the action tells the client which transaction IDs to fetch.

Since the Blazor WASM client doesn't hold private keys, file retrieval is mediated by the Wallet Service.

---

## 5. Wallet-Mediated File Retrieval

### Download Flow

```
UI Client              Wallet Service            Register Service
  │                        │                          │
  ├─ "Download file" ─────►│                          │
  │  (actionTxId,          │                          │
  │   fieldName,           ├─ Fetch action payload ──►│
  │   walletAddress)       │◄── File reference ───────┤
  │                        │                          │
  │                        ├─ Unwrap MasterFileKey    │
  │                        │  (from action payload)   │
  │                        │                          │
  │                        ├─ Fetch chunk 0 ─────────►│
  │                        ├─ Fetch chunk 1 ─────────►│
  │                        ├─ Fetch chunk 2 ─────────►│
  │                        │◄── Chunk payloads ───────┤
  │                        │                          │
  │                        ├─ Derive chunk keys (HKDF)│
  │                        ├─ Decrypt each chunk      │
  │                        ├─ Reassemble + verify hash│
  │                        │                          │
  │◄── Stream decrypted ───┤                          │
  │    file to browser     │                          │
```

### Wallet Service Endpoint

| Method | Path | Purpose |
|--------|------|---------|
| GET | `/api/wallets/{address}/files/download` | Fetch, decrypt, reassemble, stream file |

Query params: `actionTxId`, `fieldName`, `fileIndex` (for array fields, defaults to 0)

### Key Points

- Wallet Service already has private keys and Register Service client access
- File streamed back as plaintext bytes — never touches the UI client encrypted
- Wallet Service validates SHA-256 hash after reassembly before streaming
- Progress communicated via chunked HTTP response (`Content-Length` known from metadata)
- Authorization: JWT must prove the requesting user owns the wallet address
- Private keys never leave the Wallet Service

---

## 6. UI Component & UX

### File Upload Component (`FileReferenceField.razor`)

- Renders for `ControlTypes.File` / `format: "file-reference"` fields
- Phase 1: file picker + camera capture (`accept="image/*" capture="environment"` on mobile)
- Array fields: "Add file" button, file list, respects `minItems`/`maxItems`

### Upload UX Flow

1. User selects file(s) via picker or camera
2. Client validates MIME type against `accept`, size against `maxSizePerFile`
3. Progress bar per file
4. Client chunks if >4MB, uploads chunks sequentially
5. Sub-progress within each file's progress bar for multi-chunk uploads
6. File reference populated in form data on completion
7. Action submit button disabled until all uploads complete

### Display UX (Viewing Actions with Files)

- File fields show: icon (by MIME type) + filename + size + download link
- Array fields show as a list
- Click download → Wallet Service fetches/decrypts/streams → browser download triggered
- Download progress bar for multi-chunk files
- No inline preview or thumbnails (deferred)

### Error States

- File too large → immediate client-side rejection with message
- Wrong MIME type → immediate client-side rejection
- Upload failure mid-chunk → retry that chunk, not the whole file
- Chunk timeout → clear error with "retry" button

---

## 7. Phase 1 Scope

### In Scope

- File reference model (`format: "file-reference"`, `x-file` extension)
- 4MB chunking with HKDF-derived encryption keys
- Staged submission (chunks first, then action)
- Validator rules (chunk existence, same-docket sealing, size/type enforcement, orphan timeout)
- `FileReferenceField.razor` component (file picker + camera capture)
- Array file fields with min/max
- Wallet-mediated download (fetch, decrypt, reassemble, stream)
- Download-link-only display (icon + filename + size)
- Sequential chunk upload with per-file progress
- Client-side MIME type and size validation

### Explicitly Deferred

- Thumbnails / inline preview (security model TBD)
- Drag-and-drop upload
- Parallel chunk upload
- Blob storage backend (raises ceiling beyond 40MB)
- Configurable per-register size limits
- Resumable uploads
- ZKP attachment integration
- Compression integration (GAP-007)

### Limits

| Parameter | Phase 1 Value |
|-----------|:---:|
| Max chunk size | 4MB |
| Max chunks per file | 10 |
| Max total file size | 40MB |
| Max files per array field | Blueprint-defined (`maxItems`) |
| Orphan chunk timeout | 30 minutes |
| Accepted MIME types | Blueprint-defined (`accept`) |

---

## 8. Components to Modify

| Component | Change |
|-----------|--------|
| `Sorcha.TransactionHandler` | HKDF key derivation, `file-chunk` metadata type |
| `Sorcha.Blueprint.Models` | `x-file` schema extension parsing |
| `Sorcha.Blueprint.Engine` | File reference validation in schema evaluator |
| `Sorcha.Validator.Core` | Chunk existence, same-docket, size/type rules |
| `Sorcha.Wallet.Service` | File download endpoint (fetch + decrypt + stream) |
| `Sorcha.Wallet.Core` | HKDF chunk key derivation |
| `Sorcha.Register.Service` | No new endpoints (existing payload retrieval) |
| `Sorcha.Blueprint.Service` | Chunk submission handling, orphan cleanup |
| `Sorcha.UI.Web.Client` | `FileReferenceField.razor`, download UX |
| `Sorcha.ServiceClients` | Wallet file download client method |
