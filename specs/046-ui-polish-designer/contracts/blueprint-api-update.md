# Contract: Blueprint API Update Method

**Existing endpoint**: `PUT /api/blueprints/{id}` (Blueprint Service — already implemented)

**New client wrapper needed in `IBlueprintApiService`**:

```
UpdateBlueprintAsync(id: string, blueprint: object, ct: CancellationToken) → BlueprintListItemViewModel?
```

Maps to: `PUT /api/blueprints/{id}` with JSON body of `Sorcha.Blueprint.Models.Blueprint`

Returns: Updated blueprint summary or null on failure.

---

# Contract: EventsHub Client Connection

**Existing hub**: `/hubs/events` (Blueprint Service — already implemented)

**New client class**: `EventsHubConnection` (follows `ActionsHubConnection` pattern)

| Direction | Method | Payload |
|-----------|--------|---------|
| Client → Server | `Subscribe()` | (none) |
| Client → Server | `Unsubscribe()` | (none) |
| Server → Client | `EventReceived` | `ActivityEventDto` |
| Server → Client | `UnreadCountUpdated` | `int` |
