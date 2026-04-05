# Feature Specification: Stored Data Transactions

**Feature Branch**: `085-stored-data-transactions`
**Created**: 2026-04-05
**Status**: Draft
**Input**: User description: "Stored Data Transactions - file attachments as chunked transactions with HKDF encryption"

## User Scenarios & Testing *(mandatory)*

### User Story 1 - Upload File Attachment During Workflow Action (Priority: P1)

A workflow participant executing a blueprint action that includes a file field (e.g. "site photo", "inspection report") selects or captures a file from their device. The system chunks the file if needed, encrypts each chunk, submits chunk transactions, then submits the action referencing those chunks. All chunks and the action are sealed together in the same docket.

**Why this priority**: This is the core capability. Without file upload during action execution, no other file-related feature has value. Enables evidence capture, document attachment, and photo upload in real-world workflows like construction permits and trade finance.

**Independent Test**: Can be tested by creating a blueprint with a file field, executing the action with a file attachment, and verifying the file reference appears in the sealed action payload with valid chunk transaction IDs.

**Acceptance Scenarios**:

1. **Given** a blueprint action with a single file field (`format: "file-reference"`, accept: `image/jpeg`), **When** the participant selects a 2MB JPEG photo and submits the action, **Then** the file is uploaded as a single chunk transaction, the action payload contains a file reference with one chunk transaction ID, filename, content type, size, and SHA-256 hash, and both transactions are sealed in the same docket.

2. **Given** a blueprint action with a file field (`maxSizePerFile: "16MB"`), **When** the participant selects an 8MB PDF, **Then** the file is split into two 4MB chunks, each chunk is submitted as a separate transaction, and the action payload references both chunk transaction IDs in order.

3. **Given** a blueprint action with a file field (`accept: ["image/jpeg", "image/png"]`), **When** the participant selects a `.docx` file, **Then** the system rejects the file immediately with a message indicating the accepted file types.

4. **Given** a blueprint action with a file field (`maxSizePerFile: "16MB"`), **When** the participant selects a 50MB video file, **Then** the system rejects the file immediately with a message indicating the maximum allowed size.

---

### User Story 2 - Download File Attachment from Completed Action (Priority: P1)

A workflow participant viewing a completed action that contains file attachments clicks a download link. The Wallet Service fetches the chunk transactions from the register, unwraps the master file key, derives per-chunk decryption keys, decrypts and reassembles the file, verifies the integrity hash, and streams the decrypted file to the participant's browser.

**Why this priority**: Upload without download is useless. Participants must be able to retrieve evidence and documents attached to actions they have access to. This completes the file lifecycle.

**Independent Test**: Can be tested by viewing a previously submitted action with file attachments and downloading a file, verifying the downloaded file matches the original in content and integrity.

**Acceptance Scenarios**:

1. **Given** a sealed action with a single-chunk file attachment, **When** an authorised participant clicks the download link, **Then** the Wallet Service fetches the chunk, decrypts it, verifies the SHA-256 hash, and streams the original file to the browser with the correct filename and MIME type.

2. **Given** a sealed action with a multi-chunk file attachment (3 chunks), **When** an authorised participant clicks the download link, **Then** the Wallet Service fetches all 3 chunks, derives per-chunk keys via HKDF, decrypts each, reassembles in order, verifies the hash, and streams the complete file.

3. **Given** a sealed action with a file attachment, **When** a participant who is not an authorised recipient attempts to download, **Then** the system denies the request because the participant cannot unwrap the master file key.

4. **Given** a sealed action with a file attachment where one chunk has been corrupted, **When** the Wallet Service reassembles and verifies the hash, **Then** the download fails with an integrity error rather than serving a corrupted file.

---

### User Story 3 - Upload Multiple Files in Array Field (Priority: P2)

A workflow participant executing a blueprint action with an array file field (e.g. "site photos", min 1, max 5) uploads multiple files. Each file is independently chunked and encrypted. The UI shows progress per file and disables action submission until all uploads complete.

**Why this priority**: Many real-world workflows require multiple attachments (multiple photos of a site, multiple supporting documents). This extends the single-file capability to collections.

**Independent Test**: Can be tested by creating a blueprint with an array file field, uploading 3 files of varying sizes, and verifying each has its own file reference with independent chunk transaction IDs.

**Acceptance Scenarios**:

1. **Given** a blueprint action with an array file field (`minItems: 1, maxItems: 5`), **When** the participant uploads 3 JPEG photos, **Then** each photo gets its own file reference, each with independent chunk transactions, and all chunks plus the action are sealed in the same docket.

2. **Given** a blueprint action with an array file field (`minItems: 2`), **When** the participant uploads only 1 file and attempts to submit, **Then** the system prevents submission and indicates the minimum number of files required.

3. **Given** a blueprint action with an array file field (`maxItems: 3`), **When** the participant has uploaded 3 files, **Then** the "Add file" button is disabled and the participant cannot add more files.

4. **Given** an array file field with 3 uploaded files, **When** the participant removes one file before submitting, **Then** the removed file's chunk transactions are not referenced in the action and become orphans subject to the cleanup timeout.

---

### User Story 4 - Camera Capture on Mobile Device (Priority: P2)

A field worker on a mobile device executing a blueprint action with an image file field taps a "Take Photo" button, which opens the device camera. After capturing the photo, the image is automatically attached to the file field and begins uploading.

**Why this priority**: Camera capture is the primary use case for on-site evidence collection (construction inspections, goods arrival in trade finance, site surveys). Without this, mobile users must take a photo separately and then find it in their file picker.

**Independent Test**: Can be tested on a mobile device by tapping the camera capture button, taking a photo, and verifying the photo appears as an attachment with upload progress.

**Acceptance Scenarios**:

1. **Given** a blueprint action with an image file field on a mobile device, **When** the participant taps "Take Photo", **Then** the device rear camera opens for capture.

2. **Given** the camera is open, **When** the participant takes a photo, **Then** the photo is attached to the file field and upload begins with a progress indicator.

3. **Given** the camera is open, **When** the participant cancels without taking a photo, **Then** no file is attached and the field remains in its previous state.

---

### User Story 5 - Validator Enforces File Chunk Integrity (Priority: P1)

The validator service receives an action transaction that references file chunk transactions. Before signing and sealing the docket, the validator verifies all referenced chunks exist, have matching metadata, are contiguous, comply with size and type constraints, and have not been sealed in another docket.

**Why this priority**: Without validator enforcement, the system cannot guarantee file integrity at the register level. This is a security-critical requirement.

**Independent Test**: Can be tested by submitting actions with various invalid chunk references (missing chunks, wrong types, exceeded limits) and verifying the validator rejects each.

**Acceptance Scenarios**:

1. **Given** an action referencing 3 chunk transaction IDs, **When** all 3 chunks exist with matching file hashes and contiguous indices, **Then** the validator signs and seals the docket containing the action and all chunks.

2. **Given** an action referencing 3 chunk transaction IDs, **When** chunk 1 does not exist, **Then** the validator rejects the action and does not seal the docket.

3. **Given** an action referencing chunks that exceed the schema's `maxSizePerFile`, **Then** the validator rejects the action.

4. **Given** an action referencing chunks whose content type does not match the schema's `accept` list, **Then** the validator rejects the action.

5. **Given** chunk transactions that have not been referenced by any action, **When** 30 minutes elapse, **Then** the orphaned chunks are discarded.

6. **Given** an action referencing chunk transaction IDs that are already sealed in a different docket, **Then** the validator rejects the action.

---

### User Story 6 - View File Metadata Without Downloading (Priority: P3)

A workflow participant viewing a completed action sees file attachment metadata (filename, file type icon, size) displayed inline without triggering a download. This allows participants to identify files and decide which to download.

**Why this priority**: Convenience feature. Participants need to know what files are attached before committing to a potentially large download, especially on mobile or slow connections.

**Independent Test**: Can be tested by viewing an action with file attachments and verifying filename, type, and size are displayed without any network request for file content.

**Acceptance Scenarios**:

1. **Given** a sealed action with 3 file attachments, **When** the participant views the action, **Then** each file shows its filename, a type-appropriate icon, and human-readable size (e.g. "2.4 MB").

2. **Given** a sealed action with file attachments, **When** the participant views the action on a slow connection, **Then** file metadata appears immediately (it is in the action payload) without waiting for file content to load.

---

### Edge Cases

- What happens when a chunk upload fails partway through a multi-chunk file? The successfully uploaded chunks remain pending; the client retries only the failed chunk. If the user abandons the action, all chunks become orphans and are cleaned up after 30 minutes.
- What happens when the same file is uploaded to two different actions? Each upload produces independent chunk transactions with independent encryption keys. There is no deduplication — each action owns its own copy.
- What happens when a file field is optional and no file is provided? The file reference field is null/absent in the action payload. The validator does not require chunk transactions for optional empty file fields.
- What happens when a device runs out of storage during camera capture? This is handled by the device OS, not the application. The capture fails and no file is attached.
- What happens when the network disconnects during chunk upload? The current chunk upload fails. The UI shows an error with a retry button for that specific chunk. Previously uploaded chunks remain valid until orphan timeout.

## Requirements *(mandatory)*

### Functional Requirements

- **FR-001**: System MUST support a `file-reference` format in blueprint action data schemas, allowing blueprint authors to declare file attachment fields with accepted MIME types, maximum file size, and maximum chunk count.
- **FR-002**: System MUST split files exceeding 4MB into chunks of at most 4MB each, with a maximum of 10 chunks per file (40MB total limit).
- **FR-003**: System MUST encrypt each file chunk using a key derived via HKDF-SHA256 from a randomly generated master file key, using a random per-file salt and the chunk index as info parameter. The salt is stored in the file reference for recipients to derive chunk keys.
- **FR-004**: System MUST wrap the master file key once per authorised recipient in the parent action's payload, using the existing per-recipient key wrapping mechanism.
- **FR-005**: System MUST submit chunk transactions before the action transaction (staged submission), with each chunk as a separate transaction with metadata type `file-chunk`.
- **FR-006**: System MUST store the file reference (filename, content type, size, SHA-256 hash, random salt, ordered chunk transaction IDs, master key reference) in the action payload field value.
- **FR-007**: Validator MUST verify all referenced chunk transactions exist, have matching file hashes, contiguous indices, compliant sizes and MIME types, and are not sealed in another docket before signing the action.
- **FR-008**: Validator MUST seal the action and all its referenced chunk transactions in the same docket.
- **FR-009**: System MUST discard orphaned chunk transactions (not referenced by any action) after 30 minutes.
- **FR-010**: System MUST support array file fields with configurable minimum and maximum item counts, where each item is an independent file reference.
- **FR-011**: Wallet Service MUST provide a file download capability that fetches chunk transactions from the register, unwraps the master file key, derives per-chunk keys, decrypts, reassembles, verifies the SHA-256 hash, and streams the plaintext file to the requesting client.
- **FR-012**: System MUST validate file MIME type and size on the client before upload begins, rejecting non-compliant files immediately with a clear error message.
- **FR-013**: UI MUST provide a file picker input and a camera capture button (on mobile devices) for file fields.
- **FR-014**: UI MUST show upload progress per file, with sub-progress for multi-chunk uploads, and disable action submission until all uploads complete.
- **FR-015**: UI MUST display file metadata (filename, type icon, human-readable size, download link) for file attachments on completed actions, without automatically downloading file content.
- **FR-016**: System MUST reject files that exceed the platform maximum (40MB) regardless of blueprint schema settings.
- **FR-017**: Authorization for file download MUST require the requesting user to own the wallet address and be an authorised recipient of the action's encrypted payload.

### Key Entities

- **File Reference**: The value stored in an action payload for a file field — contains filename, MIME type, total size, integrity hash, ordered chunk transaction IDs, and master key reference. Lives within the action transaction payload.
- **File Chunk Transaction**: A standard transaction with metadata type `file-chunk`. Contains encrypted file data for one chunk (up to 4MB). Linked to the parent action via the file reference and sealed in the same docket.
- **Master File Key**: A random 256-bit symmetric key generated per file upload. Wrapped per-recipient in the parent action payload. Used with HKDF to derive per-chunk encryption keys. Never stored in chunk transactions.
- **File Schema Extension (`x-file`)**: Blueprint schema metadata declaring accepted MIME types, maximum file size, and maximum chunk count for a file field. Used by the UI for client-side validation and by the validator for server-side enforcement.

## Success Criteria *(mandatory)*

### Measurable Outcomes

- **SC-001**: Users can attach a file up to 40MB to a workflow action and have it accepted, encrypted, and sealed within 60 seconds on a standard broadband connection.
- **SC-002**: Users can download a previously attached file and receive an identical copy (verified by hash) of the original file.
- **SC-003**: Files that violate size or type constraints are rejected before any upload begins, with feedback appearing within 1 second of file selection.
- **SC-004**: Multi-chunk files (>4MB) upload with visible per-chunk progress, giving users continuous feedback during the upload process.
- **SC-005**: Orphaned chunks (from abandoned uploads) are cleaned up within 30 minutes, preventing storage waste.
- **SC-006**: Mobile users can capture a photo directly from the device camera and have it attached to a file field without leaving the workflow form.
- **SC-007**: The validator rejects 100% of actions with invalid file references (missing chunks, wrong types, exceeded limits, chunks sealed in other dockets).
- **SC-008**: File download only succeeds for authorised recipients — unauthorised users cannot retrieve file content.
- **SC-009**: Actions with file attachments display file metadata (name, type, size) instantly when viewing, without requiring file content to be downloaded first.
