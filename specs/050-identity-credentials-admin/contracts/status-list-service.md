# Contract: Status List Viewer Service

## IStatusListService (NEW)

### GetStatusListAsync

- **Input**: listId (string)
- **HTTP**: GET `/api/v1/credentials/status-lists/{listId}` (public, no auth)
- **Success**: Returns StatusListViewModel
- **Errors**: 404 (not found)

## UI Page Contract

### /admin/status-lists

- Text input for List ID + "Lookup" button
- Results displayed as card with metadata fields
- Raw JSON viewer (expandable) showing full W3C document
- Recently-viewed list stored in browser localStorage (max 10)
- Empty state message when no lists viewed yet

## Test Requirements

- GetStatusListAsync: success, not found, network error = 3 tests minimum
