# PayloadTests Actor Migration Design

**Date:** 2026-04-07
**Status:** Draft
**Scope:** Migrate PayloadTests walkthrough to sorcha-agent actor-based execution, adding file upload preAction support

---

## Problem

The PayloadTests walkthrough runs as a single-threaded PowerShell script (`run.ps1`) that logs in as both sender and receiver sequentially. This needs to be migrated to the actor-based execution model introduced in feature 087, where each participant runs as an independent `sorcha-agent` process.

The key challenge is that the sender must generate/read a file, chunk it, upload chunks via the file-chunks API, and include the resulting file reference in its action payload. The current `sorcha-agent` only submits JSON payloads — it has no file upload capability.

## Solution

Extend the actor rule format with a `preActions` array. PreActions run after rule matching but before payload submission. The first (and only) preAction type is `file-upload`, which handles file generation or reading, chunking, upload, and file reference injection.

### PreAction Hook in Actor Rules

```json
{
  "actionName": "Send File",
  "decision": "approve",
  "preActions": [
    {
      "type": "file-upload",
      "config": {
        "fieldName": "attachment",
        "sizeBytes": 1024,
        "seed": 85,
        "fileName": "test-file.bin",
        "contentType": "application/octet-stream"
      }
    }
  ],
  "payload": { "message": "Test file transfer" }
}
```

**Two modes:**
- **Generated file:** When `filePath` is absent, generates a deterministic test file from `sizeBytes` + `seed` (matching the PowerShell `New-TestFileContent` pattern)
- **Real file:** When `filePath` is present, reads the file from disk. `fileName` and `contentType` are auto-detected from the path if not explicitly set.

**Pipeline (identical for both modes):**
1. Obtain file bytes (generate or read)
2. Compute SHA-256 hash
3. Chunk to 4MB segments
4. POST each chunk to `/api/file-chunks/` with sender wallet and register address
5. Build file reference (fileName, contentType, size, hash, salt, chunkTransactionIds, uploadSessionId, masterKeyId)
6. Inject file reference into payload under `config.fieldName`

### Actor Definitions

**Sender** — generates test file, uploads chunks, executes "Send File" (action 0)
**Receiver** — acknowledges receipt (action 1), simple payload `{ "acknowledged": true }`

### What Changes in Sorcha.Agent

| File | Change |
|------|--------|
| `Configuration/ActorDefinition.cs` | Add `PreAction`, `FileUploadConfig` records; add `PreActions` to `ActorRule` |
| `Execution/FileUploadHandler.cs` | NEW — file generation/reading, chunking, upload, reference building |
| `Commands/RunCommand.cs` | After rule match, execute preActions and merge results into payload |

### What Does NOT Change

- `setup.ps1` — unchanged
- `run.ps1` — unchanged, remains for detailed verification testing
- `cross-node-setup.ps1`, `stress-test.ps1` — unchanged

### New Walkthrough Files

| File | Purpose |
|------|---------|
| `walkthroughs/PayloadTests/actors/sender.json` | Sender with file-upload preAction |
| `walkthroughs/PayloadTests/actors/receiver.json` | Receiver acknowledges |
| `walkthroughs/PayloadTests/actors/sender-remote.json` | Remote variant (n1.sorcha.dev) |
| `walkthroughs/PayloadTests/actors/receiver-remote.json` | Remote variant |
| `walkthroughs/PayloadTests/run-agents.ps1` | Launcher for both actors |
| `walkthroughs/PayloadTests/actors/README.md` | Usage docs |

### Tests

| File | Coverage |
|------|----------|
| `FileUploadHandlerTests.cs` | Deterministic file generation, chunking sizes, file reference building, real file reading |

### Design Decisions

- **PreActions are rule-level, not global** — only run when the specific rule matches
- **Only `file-upload` type for now** — extensible but YAGNI
- **No file verification in the agent** — receiver just acknowledges. Download + SHA-256 verification stays in `run.ps1`
- **Deterministic file generation** — seeded RNG matches the PowerShell pattern for reproducibility
- **4MB chunk size** — hardcoded, matching the platform's chunk size constraint

### Out of Scope

- File download/verification in the agent (stays in run.ps1)
- Multi-round support (agent handles one cycle; stress testing stays in stress-test.ps1)
- New preAction types beyond file-upload
