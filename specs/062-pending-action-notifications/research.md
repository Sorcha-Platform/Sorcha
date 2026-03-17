# Research: Pending Action Notifications

**Date**: 2026-03-17 | **Feature**: 062-pending-action-notifications

## Decision 1: Notification Preference Storage

**Decision**: Use existing Tenant Service `UserPreferences` table and endpoints
**Rationale**: Infrastructure already exists — `NotificationsEnabled`, `NotificationMethod` (InApp/InAppPlusEmail/InAppPlusPush), and `NotificationFrequency` (RealTime/HourlyDigest/DailyDigest) fields are defined and served via `GET/PUT /api/preferences`
**Alternatives considered**: New dedicated notification service, Redis-only preferences — rejected because Tenant Service already owns user settings

## Decision 2: Notification Persistence

**Decision**: Use existing Tenant Service `ActivityEvent` system for notification history
**Rationale**: ActivityEvent already has: Id, UserId, OrganizationId, EventType, Severity, Title, Message, IsRead, CreatedAt, ExpiresAt. Events are persisted in PostgreSQL with `GET /api/events` (paginated, filtered), `POST /api/events/mark-read`, and `GET /api/events/unread-count`. UI already renders these via ActivityLogPanel.
**Alternatives considered**: Separate notification table in Wallet Service, MongoDB collection — rejected because the activity event system exists and the UI already renders it

## Decision 3: SignalR Delivery Path

**Decision**: Add `InboundActionReceived` handler to existing `EventsHubConnection`, plus create a new `PendingActionInbox` component
**Rationale**: EventsHubNotificationBridge already enriches notifications and sends `InboundActionReceived` to the `user:{userId}` group. The handler just needs registering on the client side.
**Alternatives considered**: New SignalR hub, polling-based approach — rejected because the infrastructure is already in place

## Decision 4: Blueprint Notification Templates

**Decision**: Add optional `NotificationConfig` property to the `Action` model
**Rationale**: Action model already has 24 properties including form, instructions, routes. Adding notification config follows the same pattern. Existing `AdditionalProperties` dictionary could work but a typed property gives better validation.
**Alternatives considered**: Store templates separately, use AdditionalProperties — rejected for type safety and discoverability

## Decision 5: Pending Actions Query

**Decision**: Add a dedicated endpoint to Blueprint Service for querying pending actions by user wallet
**Rationale**: `IInstanceStore.GetByParticipantWalletAsync()` exists but returns full instances. Need a lightweight query that returns pending action summaries with blueprint context.
**Alternatives considered**: Client-side filtering of all instances — rejected for performance with many instances

## Decision 6: Replace DefaultNotificationPreferenceProvider

**Decision**: Create `TenantNotificationPreferenceProvider` that calls Tenant Service `/api/preferences` via service client
**Rationale**: DefaultNotificationPreferenceProvider is hardcoded to RealTime+InApp. Tenant Service already stores the real preferences. Just need to wire the call.
**Alternatives considered**: Keep hardcoded defaults — rejected because user preferences already exist but aren't used

## Key Infrastructure Already in Place

| Component | Location | Status |
|-----------|----------|--------|
| User notification preferences | Tenant Service `UserPreferences` table | Exists, stores method + frequency |
| Activity events (persistence) | Tenant Service `ActivityEvent` table | Exists, full CRUD + read/unread |
| EventsHub SignalR | Blueprint Service `EventsHub.cs` | Exists, user group routing |
| Notification enrichment | Blueprint Service `EventsHubNotificationBridge` | Exists, resolves blueprint/action/sender |
| ActivityLogPanel UI | Sorcha.UI `ActivityLogPanel.razor` | Exists, renders events with grouping |
| Notification delivery routing | Wallet Service `NotificationDeliveryService` | Exists, real-time/digest/rate-limit |
| InboundActionEvent model | ServiceClients `InboundActionEvent.cs` | Exists, all fields present |
| InboundActionNotification model | EventsHubNotificationBridge (inline) | Exists, enriched model |
| NextAction model | Blueprint Service `NextAction.cs` | Exists, has ActionId/Title/Deadline |
