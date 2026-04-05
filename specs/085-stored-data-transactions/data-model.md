# Data Model: Stored Data Transactions

**Feature**: 085-stored-data-transactions
**Date**: 2026-04-05

## Entities

### FileReference

The runtime value stored in an action payload when a file field is populated.

| Field | Type | Description |
|-------|------|-------------|
| fileName | string | Original filename (e.g. "site-inspection.pdf") |
| contentType | string | MIME type (e.g. "application/pdf", "image/jpeg") |
| size | long | Total file size in bytes (original, pre-encryption) |
| hash | string | SHA-256 hash of the complete original file, prefixed "sha256:" |
| salt | string | Base64-encoded random salt used for HKDF key derivation |
| chunkTransactionIds | string[] | Ordered array of chunk transaction IDs |
| masterKeyId | string | Transaction ID of the parent action (where wrapped master key lives) |

**Validation rules**:
- fileName: non-empty, max 255 characters, no path separators
- contentType: valid MIME type format
- size: > 0, ≤ 41,943,040 (40MB)
- hash: must start with "sha256:" followed by 64 hex characters
- chunkTransactionIds: 1-10 items, each non-empty
- masterKeyId: non-empty

**Relationships**: Embedded in action transaction payload. References chunk transactions by ID.

### FileChunkMetadata

Metadata stored in each chunk transaction's `Metadata` field (JSON).

| Field | Type | Description |
|-------|------|-------------|
| type | string | Always "file-chunk" |
| parentActionId | string? | Transaction ID of the parent action (null at submission, populated by validator) |
| fileHash | string | SHA-256 hash of the complete file (must match parent FileReference.hash) |
| chunkIndex | int | Zero-based position in the chunk sequence |
| totalChunks | int | Total number of chunks in this file |
| contentType | string | MIME type of the original file |
| chunkSize | int | Size of this chunk in bytes (encrypted payload size) |

**Validation rules**:
- type: must be "file-chunk"
- chunkIndex: 0 to totalChunks-1
- totalChunks: 1-10
- chunkSize: > 0, ≤ 4,194,304 (4MB)
- fileHash: must match "sha256:" format

**Relationships**: Belongs to a parent action transaction. Sealed in the same docket.

### FileSchemaExtension (x-file)

Blueprint schema metadata for file fields. Parsed from the `x-file` property in JSON Schema.

| Field | Type | Description |
|-------|------|-------------|
| accept | string[] | Allowed MIME types (e.g. ["image/jpeg", "image/png"]) |
| maxSizePerFile | string | Maximum file size as human-readable string (e.g. "16MB") |
| maxChunks | int | Maximum chunks per file (default: 10, platform max: 10) |

**Validation rules**:
- accept: at least one MIME type, each valid format
- maxSizePerFile: parseable size string, ≤ "40MB"
- maxChunks: 1-10

**Relationships**: Declared in blueprint action dataSchema. Used by UI for client-side validation and by validator for server-side enforcement.

### MasterFileKey (transient)

Not persisted as a separate entity — wrapped in the action transaction's payload challenges.

| Field | Type | Description |
|-------|------|-------------|
| key | byte[32] | Random 256-bit symmetric key |

**Lifecycle**: Generated per file upload. Wrapped per recipient via existing `Challenges` mechanism in the action payload. Derived into per-chunk keys via HKDF. Never stored in chunk transactions. Zeroized after use.

## State Transitions

### File Upload Lifecycle

```
[File Selected] → [Validating] → [Chunking] → [Uploading Chunks] → [Chunks Submitted] → [Action Submitted] → [Sealed in Docket]
                       |                              |
                       v                              v
                  [Rejected]                   [Upload Failed]
                  (type/size)                  (retry chunk)
```

### Chunk Transaction Lifecycle

```
[Submitted] → [Pending] → [Referenced by Action] → [Sealed in Docket]
                  |
                  v (30 min timeout, no action reference)
              [Orphaned] → [Discarded]
```

## Existing Entities Modified

### Transaction (Sorcha.TransactionHandler)

No schema changes. Chunk transactions use existing fields:
- `Metadata`: JSON string containing FileChunkMetadata
- `PayloadManager`: Contains encrypted chunk data with PayloadType.Document
- `PreviousTxHash`: Links to parent action transaction (set by validator)

### ActionSubmissionRequest (Sorcha.Blueprint.Service)

Already has `List<FileAttachment>? Files` field. The existing `FileAttachment` model (FileName, ContentType, ContentBase64) is replaced with the chunked approach — files are uploaded as separate chunk transactions before action submission. The `Files` field on `ActionSubmissionRequest` may be removed or repurposed.

### PayloadModel (Sorcha.Register.Models)

No changes. ContentType and ContentEncoding fields already support file payloads.
