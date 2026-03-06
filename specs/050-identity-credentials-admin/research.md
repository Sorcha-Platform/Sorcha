# Research: Identity & Credentials Admin

## Decision 1: QR Code Generation for Presentation Requests

**Decision**: Use a JavaScript interop approach with a lightweight QR code library (qrcode.js or similar) for generating QR codes in Blazor WASM.

**Rationale**: Blazor WASM cannot natively generate QR codes. A JS interop call to a small library is the simplest approach. The QR data is just a URL string (`openid4vp://authorize?request_uri=...&nonce=...`), so no server-side generation is needed.

**Alternatives considered**:
- Server-side QR generation (rejected: adds unnecessary backend work, the URL is already available client-side)
- .NET QR library like QRCoder (rejected: adds a NuGet dependency and may have WASM compatibility issues)
- Inline SVG generation (rejected: more complex than JS interop for the same result)

## Decision 2: Credential Lifecycle Dialog Pattern

**Decision**: Use a single reusable `CredentialLifecycleDialog` component parameterized by action type (Suspend, Reinstate, Revoke, Refresh), rather than separate dialogs per action.

**Rationale**: All 4 actions share the same UI structure: wallet picker dropdown, optional reason/duration field, and confirm/cancel buttons. The only differences are the action label, warning text (for Revoke), and the optional field (reason vs. expiry duration for Refresh). A parameterized component reduces duplication.

**Alternatives considered**:
- Separate dialog per action (rejected: 4 nearly-identical components with duplicated wallet picker logic)
- Inline confirmation (rejected: wallet picker requires more screen space than an inline expand)

## Decision 3: Participant Status Chip Colors

**Decision**: Use MudBlazor's `MudChip` component with: Color.Success (green) for Active, Color.Warning (amber) for Suspended, Color.Default (grey) for Inactive.

**Rationale**: Follows MudBlazor's built-in color semantics. Green=good, amber=attention, grey=disabled. Consistent with how status indicators work elsewhere in the Sorcha admin UI (e.g., validator status chips from Feature 049).

**Alternatives considered**:
- Custom CSS colors (rejected: breaks MudBlazor theme consistency)
- Icon-based indicators (rejected: chips with text labels are more accessible and self-explanatory)

## Decision 4: Status List Viewer Scope

**Decision**: Read-only viewer that fetches individual status lists by ID. No list-all endpoint exists, so the page provides an ID input field plus a table of recently-viewed lists (stored in browser local storage).

**Rationale**: The backend only exposes `GET /api/v1/credentials/status-lists/{listId}` — there is no "list all status lists" endpoint. Building a list-all endpoint would require backend changes, which is out of scope. A lookup-by-ID approach with local history is the simplest solution.

**Alternatives considered**:
- Add a backend list-all endpoint (rejected: out of scope, no backend changes)
- Hardcode known list IDs (rejected: not scalable, IDs are dynamic)

## Decision 5: Presentation Admin Service Separation

**Decision**: Create a new `IPresentationAdminService` / `PresentationAdminService` rather than extending `ICredentialApiService`.

**Rationale**: The verifier-side operations (create request, get result) are conceptually separate from credential management. They target different backend endpoints (Wallet Service presentations vs. Blueprint Service credentials). A separate service keeps concerns clean and allows independent testing.

**Alternatives considered**:
- Extend ICredentialApiService (rejected: conflates holder-side credential ops with verifier-side presentation ops)
- Use ICredentialApiService for holder + new service for verifier only (considered: but the existing service already has holder methods, so this would split presentation logic across two services)

## Decision 6: CLI Refit Interface Extensions

**Decision**: Add new methods to existing `ICredentialServiceClient` and `IParticipantServiceClient` Refit interfaces rather than creating new interfaces.

**Rationale**: The new CLI commands call the same backend services as existing commands. Adding methods to existing interfaces follows the established pattern and avoids creating new HTTP client registrations in DI.

**Alternatives considered**:
- New Refit interfaces per feature area (rejected: would require new HttpClient factory methods and DI registration, unnecessary complexity)
