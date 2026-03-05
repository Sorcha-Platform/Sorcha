# Contract: Participant Admin UI Services

## IParticipantApiService (existing — no new methods needed)

Already has: `SuspendParticipantAsync`, `ReactivateParticipantAsync`. Wire to UI buttons.

## IParticipantPublishingService (existing — no new methods needed)

Already has: `PublishAsync`, `UpdatePublishedAsync`, `RevokeAsync`. Wire to publish dialog.

## UI Component Contracts

### ParticipantDetail — Suspend/Reactivate Buttons

- Active participant: Show "Suspend" button (Color.Warning)
- Suspended participant: Show "Reactivate" button (Color.Success)
- Inactive participant: No action buttons
- Both require confirmation dialog before executing

### ParticipantDetail — Publish Button

- Visible only for Active participants when user has Administrator role
- Opens ParticipantPublishDialog with pre-filled fields
- After publish: show published register indicator

### ParticipantList — Status Chips

- MudChip Color.Success for Active
- MudChip Color.Warning for Suspended
- MudChip Color.Default for Inactive
