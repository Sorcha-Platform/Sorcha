# Contract — Participant Identity Surface

## Components

| Component | Source path | Target path |
|-----------|-------------|-------------|
| `ParticipantList.razor` | `Sorcha.UI.Core/Components/Participants/` | `Sorcha.UI.Components.User/Components/Participants/` |
| `ParticipantSearch.razor` | same source | same target |
| `ParticipantDetail.razor` | same source | same target |
| `ParticipantForm.razor` | same source | same target |
| `PublishParticipantDialog.razor` | same source | same target |
| `WalletLinkForm.razor` | same source | same target |

## Parameters (preserved verbatim)

- `ParticipantList.Participants` — `IReadOnlyList<ParticipantIdentity>` to render
- `ParticipantList.OnSelected` — selection callback
- `ParticipantSearch.OnSearchResult` — fires with matches as the user types
- `ParticipantDetail.Participant` — single participant model
- `ParticipantForm.Participant` — bound model for create/edit
- `ParticipantForm.OnSave` / `OnCancel` — outcome callbacks
- `PublishParticipantDialog.Participant` — participant to publish to a register
- `WalletLinkForm.Participant` — participant whose wallet link is being established

## Injected services

The participant components currently lean on `Sorcha.ServiceClients.Http`'s `IParticipantServiceClient`. The migration preserves this exactly — `IParticipantServiceClient` is already in `Sorcha.ServiceClients.Http`, which both shells reference. No new service is introduced.

## Host responsibilities

1. Both shells already register `IParticipantServiceClient` for general service-client use; no new registration needed.
2. The web app uses these components for org-admin and the user's own participant profile management.
3. The PWA uses them for citizen-side participant identity display and (in future PWA expansion) for the user editing their own participant record.
4. Multi-org context (which org's participants we're looking at) is implicit in the JWT carried by `HttpClient` and `IParticipantServiceClient`. Components do not branch on org context.

## PWA-specific note

The PWA today is citizen-scoped — its `IParticipantServiceClient` calls return participants the citizen has visibility into, which is typically just their own. The components render correctly regardless of list size, so no special handling for the single-participant case is needed.

## Out of contract

- Org administration of participants. The components are display + edit; org-admin workflows (suspend, transfer, audit) are page-level concerns in the web app's admin surface and remain there.
- Wallet challenge / verify flow. `WalletLinkForm` initiates the challenge via `IParticipantServiceClient.InitiateWalletLinkAsync`; the actual signature verification round-trip is server-side (Feature 050 + the participant API per `sorcha-architecture` skill).
- On-register participant publishing crypto. The dialog initiates the publish call; the cryptography lives in the participant publishing service.

## Verification

1. **Given** five participants, **When** `ParticipantList` renders them, **Then** five list entries appear with display names — verified by bUnit test.
2. **Given** the migrated codebase, **When** the web app's participant management page exercises the full create-edit-publish flow, **Then** the flow completes identically to its pre-migration behaviour — verified by existing E2E test in `tests/Sorcha.UI.E2E.Tests` running unchanged.
3. **Given** the PWA registers `AddSorchaUserComponents()` and an `IParticipantServiceClient` (already registered for general service-client use), **When** the PWA renders `ParticipantDetail` for the signed-in citizen, **Then** the component instantiates without dependency-injection exceptions — verified during commit 3.
