# Implementation Plan: UI Consolidation - Admin to Main UI Migration

**Feature Branch**: `ui-consolidation`
**Created**: 2026-01-21
**Estimated Effort**: 4-5 weeks
**Risk Level**: Medium

---

## Overview

This plan details the phased migration of advanced features from Sorcha.Admin to Sorcha.UI, culminating in the deprecation and removal of the Admin project.

---

## Architecture Decisions

### AD-001: Migration Strategy - Component-by-Component

**Decision**: Migrate individual components rather than wholesale page replacement.

**Rationale**:
- Lower risk of breaking existing functionality
- Easier to test incrementally
- Allows parallel development
- Enables rollback at component level

**Alternative Considered**: Fork Admin Designer.razor entirely → Rejected due to different project structure and potential namespace conflicts.

---

### AD-002: Shared Component Library Location

**Decision**: All migrated components go to `Sorcha.UI.Core` shared library.

**Rationale**:
- Sorcha.UI.Core already exists as shared component library
- Enables future reuse across potential mobile/desktop clients
- Consistent with existing architecture

---

### AD-003: Styling Approach

**Decision**: Adapt Admin components to use existing UI theme/styles.

**Rationale**:
- Sorcha.UI has established MudBlazor theme
- Prevents visual inconsistency
- Minor CSS adjustments preferable to dual styling

**Action Required**: Review Admin component styles and adjust to UI palette (#667eea primary).

---

### AD-004: Service Consolidation Strategy

**Decision**: Merge services by interface, keeping best implementation.

**Rationale**:
- Both projects have authentication services with different strengths
- Admin has better offline sync; UI has better SignalR integration
- Interfaces allow swapping implementations

---

## Phase 1: P0 Designer Components (Days 1-10)

### Goals
- Migrate core designer editing components
- Enable complete action configuration
- Migrate associated models and tests

### 1.1 Models Migration (Day 1-2)

**Files to Migrate**:
```
Admin/Models/CalculationModel.cs     → UI.Core/Models/Designer/CalculationModel.cs
Admin/Models/ConditionModel.cs       → UI.Core/Models/Designer/ConditionModel.cs
Admin/Models/ParticipantModel.cs     → UI.Core/Models/Designer/ParticipantModel.cs
```

**Steps**:
1. Copy model files to target locations
2. Update namespaces from `Sorcha.Admin.Models` to `Sorcha.UI.Core.Models.Designer`
3. Add license headers if missing
4. Verify all referenced types exist in UI.Core
5. Build and resolve any dependency issues

**Dependencies**: None

---

### 1.2 ParticipantEditor Component (Day 2-3)

**Files to Migrate**:
```
Admin/Components/Designer/ParticipantEditor.razor    → UI.Core/Components/Designer/
Admin/Components/Designer/ParticipantEditor.razor.cs → UI.Core/Components/Designer/
Admin/Components/Designer/ParticipantList.razor      → UI.Core/Components/Designer/
```

**Steps**:
1. Copy component files
2. Update namespaces and using statements
3. Adjust MudBlazor styling to UI theme
4. Wire up to Properties Panel
5. Add button in PropertiesPanel.razor to open editor
6. Test participant CRUD operations

**Dependencies**: 1.1 (ParticipantModel)

**Integration Points**:
- `PropertiesPanel.razor` - Add "Add Participant" button
- `Designer.razor` - Ensure action model updates

---

### 1.3 ConditionEditor Components (Day 4-6)

**Files to Migrate**:
```
Admin/Components/Designer/ConditionEditor.razor    → UI.Core/Components/Designer/
Admin/Components/Designer/ConditionEditor.razor.cs → UI.Core/Components/Designer/
Admin/Components/Designer/ConditionClause.razor    → UI.Core/Components/Designer/
```

**Steps**:
1. Copy component files
2. Update namespaces
3. Verify JSON Logic integration (JsonLogic NuGet package)
4. Wire up to Properties Panel
5. Test condition creation and editing
6. Verify bidirectional JSON Logic ↔ UI conversion

**Dependencies**: 1.1 (ConditionModel)

**Technical Notes**:
- Uses recursive rendering for nested conditions
- JSON Logic operators: `==`, `!=`, `<`, `>`, `<=`, `>=`, `and`, `or`, `not`
- Field references use JSON Pointer paths

---

### 1.4 CalculationEditor Component (Day 6-8)

**Files to Migrate**:
```
Admin/Components/Designer/CalculationEditor.razor    → UI.Core/Components/Designer/
Admin/Components/Designer/CalculationEditor.razor.cs → UI.Core/Components/Designer/
```

**Steps**:
1. Copy component files
2. Update namespaces
3. Verify expression parsing (infix to postfix)
4. Wire up to Properties Panel
5. Test calculation creation
6. Verify preview functionality

**Dependencies**: 1.1 (CalculationModel)

**Technical Notes**:
- Infix notation parsing with operator precedence
- Outputs JSON Logic format
- Supports field references and constants

---

### 1.5 Test Migration - Phase 1 (Day 8-10)

**Files to Migrate**:
```
Admin.Tests/Components/Designer/ConditionEditorTests.cs    → UI.Core.Tests/Components/Designer/
Admin.Tests/Components/Designer/ParticipantEditorTests.cs  → UI.Core.Tests/Components/Designer/
```

**Steps**:
1. Copy test files
2. Update namespaces and references
3. Adjust test context setup for UI.Core
4. Run tests and fix any failures
5. Ensure >85% coverage maintained

**Dependencies**: 1.2, 1.3, 1.4

---

### 1.6 Designer Page Enhancement (Day 9-10)

**File to Modify**: `Sorcha.UI.Web.Client/Pages/Designer.razor`

**Steps**:
1. Add imports for new components
2. Add "Add Participant" button to action toolbar
3. Add "Add Condition" button to action toolbar
4. Add "Add Calculation" button to action toolbar
5. Update ActionNodeWidget to show new counts
6. Integrate dialogs with state management
7. Test full workflow

**Dependencies**: 1.2, 1.3, 1.4

---

## Phase 2: P1 Export/Import & Services (Days 11-18)

### Goals
- Enable blueprint file exchange
- Add wallet selection capability
- Enhance blueprint storage

### 2.1 BlueprintSerializationService (Day 11-12)

**Files to Migrate**:
```
Admin/Services/BlueprintSerializationService.cs → UI.Core/Services/BlueprintSerializationService.cs
```

**Steps**:
1. Copy service file
2. Update namespaces
3. Register in DI (Program.cs)
4. Test JSON serialization
5. Test YAML serialization (YamlDotNet package)

**Dependencies**: None

**NuGet Check**: Ensure `YamlDotNet` is referenced in UI.Core

---

### 2.2 Export/Import Dialogs (Day 12-14)

**Files to Migrate**:
```
Admin/Components/Designer/ExportDialog.razor    → UI.Core/Components/Designer/
Admin/Components/Designer/ExportDialog.razor.cs → UI.Core/Components/Designer/
Admin/Components/Designer/ImportDialog.razor    → UI.Core/Components/Designer/
Admin/Components/Designer/ImportDialog.razor.cs → UI.Core/Components/Designer/
Admin/Models/ImportValidationResult.cs          → UI.Core/Models/Designer/
```

**Steps**:
1. Copy component and model files
2. Update namespaces
3. Add Export/Import buttons to Designer toolbar
4. Inject BlueprintSerializationService
5. Test export to JSON/YAML
6. Test import validation
7. Test file download

**Dependencies**: 2.1

---

### 2.3 WalletSelectorDialog (Day 14-15)

**Files to Migrate**:
```
Admin/Components/Designer/WalletSelectorDialog.razor → UI.Core/Components/Designer/
Admin/Components/Designer/SelectParticipantDialog.razor → UI.Core/Components/Designer/
```

**Steps**:
1. Copy component files
2. Update namespaces
3. Inject IWalletApiService (already exists in UI)
4. Wire up to ParticipantEditor
5. Test wallet listing
6. Test wallet selection and address assignment

**Dependencies**: 1.2

---

### 2.4 BlueprintStorageService Enhancement (Day 16-17)

**Approach**: Enhance existing UI service with Admin's dual-mode capabilities.

**Steps**:
1. Compare Admin.BlueprintStorageService with UI version
2. Add server-first storage mode
3. Add offline fallback logic
4. Add local cache persistence
5. Test dual-mode operation

**Dependencies**: None

---

### 2.5 Test Migration - Phase 2 (Day 17-18)

**Files to Migrate**:
```
Admin.Tests/Components/Designer/ExportImportTests.cs → UI.Core.Tests/Components/Designer/
Admin.Tests/Components/Designer/BlueprintStorageServiceTests.cs → UI.Core.Tests/Services/
```

**Steps**:
1. Copy test files
2. Update namespaces
3. Run and fix tests
4. Add new tests for dual-mode storage

**Dependencies**: 2.1, 2.2, 2.4

---

## Phase 3: P2 Offline & Additional Features (Days 19-26)

### Goals
- Enable offline-first operation
- Add useful secondary pages
- Enhance authentication

### 3.1 OfflineSyncService Migration (Day 19-21)

**Files to Migrate**:
```
Admin/Services/OfflineSyncService.cs     → UI.Core/Services/OfflineSyncService.cs
Admin/Services/IOfflineSyncService.cs    → UI.Core/Services/IOfflineSyncService.cs
Admin/Models/SyncQueueItem.cs            → UI.Core/Models/Designer/SyncQueueItem.cs
Admin/Components/Designer/OfflineSyncIndicator.razor → UI.Core/Components/Designer/
Admin/Components/Designer/SyncQueueDialog.razor      → UI.Core/Components/Designer/
Admin/Components/Designer/ConflictResolutionDialog.razor → UI.Core/Components/Designer/
```

**Steps**:
1. Copy all files
2. Update namespaces
3. Register service in DI
4. Add sync indicator to Designer toolbar
5. Integrate with BlueprintStorageService
6. Test offline queue operations
7. Test conflict resolution

**Dependencies**: 2.4

---

### 3.2 Settings Page (Day 21-22)

**Files to Create/Migrate**:
```
Admin/Pages/Settings.razor → UI.Web.Client/Pages/Settings.razor
```

**Steps**:
1. Copy page (or create new based on Admin)
2. Update namespaces and routing
3. Add to navigation menu
4. Include user preferences
5. Include profile/environment selection (if multi-profile added)

**Dependencies**: None

---

### 3.3 Help Page (Day 22-23)

**Files to Create/Migrate**:
```
Admin/Pages/Help.razor → UI.Web.Client/Pages/Help.razor
```

**Steps**:
1. Copy page structure
2. Update namespaces
3. Add to navigation menu
4. Include documentation links
5. Include keyboard shortcuts

**Dependencies**: None

---

### 3.4 Multi-Profile Authentication (Day 23-25)

**Approach**: Enhance UI auth with Admin's multi-profile capability.

**Files to Review**:
```
Admin/Services/Configuration/ConfigurationService.cs
Admin/Models/Configuration/Profile.cs
Admin/Models/Configuration/ProfileDefaults.cs
Admin/Authentication/ProfileSelector.razor
```

**Steps**:
1. Compare auth implementations
2. Add Profile model if not present
3. Enhance ConfigurationService with profile support
4. Add ProfileSelector component
5. Update token cache for per-profile storage
6. Test profile switching

**Dependencies**: None

---

### 3.5 Test Migration - Phase 3 (Day 25-26)

**Files to Migrate**:
```
Admin.Tests/Services/ConfigurationServiceTests.cs → UI.Core.Tests/Services/
Admin.Tests/Models/ProfileDefaultsTests.cs        → UI.Core.Tests/Models/
Admin.Tests/Services/BrowserTokenCacheTests.cs    → UI.Core.Tests/Services/
```

**Steps**:
1. Copy test files
2. Update namespaces
3. Run and fix tests

**Dependencies**: 3.1, 3.4

---

## Phase 4: Deprecation & Cleanup (Days 27-35)

### Goals
- Mark Admin deprecated
- Complete documentation
- User acceptance testing
- Remove Admin from solution

### 4.1 Documentation Updates (Day 27-28)

**Files to Update**:
```
CLAUDE.md                              - Remove Admin references
docs/reference/development-status.md   - Update completion status
.specify/MASTER-TASKS.md               - Mark migration complete
README.md                              - Update project list
```

**Steps**:
1. Update all documentation
2. Add migration guide for users
3. Update architecture diagrams
4. Update API documentation

---

### 4.2 Deprecation Notice (Day 28-29)

**Steps**:
1. Add `[Obsolete]` attributes to Admin project
2. Add deprecation banner to Admin UI
3. Log deprecation warning on Admin startup
4. Add redirect from Admin to UI (optional)

---

### 4.3 User Acceptance Testing (Day 29-32)

**Test Scenarios**:
1. Create new blueprint with all features
2. Add participants with wallet selection
3. Add conditions with nested logic
4. Add calculations with expressions
5. Export to JSON and YAML
6. Import from file
7. Test offline operation
8. Test profile switching (if implemented)
9. Verify all Admin test scenarios pass in UI

---

### 4.4 Final Cleanup (Day 33-35)

**Steps**:
1. Remove Sorcha.Admin from solution
2. Remove Sorcha.Admin.Client from solution
3. Remove Sorcha.Admin.Tests from solution
4. Delete Admin project directories
5. Update AppHost to remove Admin references
6. Update docker-compose to remove Admin service
7. Final solution build and test

---

## Risk Mitigation

### Rollback Plan

Each phase can be rolled back independently:

1. **Phase 1 Rollback**: Delete migrated components, revert Designer.razor changes
2. **Phase 2 Rollback**: Delete export/import dialogs, revert storage service
3. **Phase 3 Rollback**: Delete offline sync components
4. **Phase 4 Rollback**: Restore Admin project from git

### Feature Flags

Consider adding feature flags for gradual rollout:
```csharp
// appsettings.json
{
  "Features": {
    "UseEnhancedDesigner": true,
    "EnableOfflineSync": false,
    "EnableMultiProfile": false
  }
}
```

---

## Testing Strategy

### Unit Tests
- Migrate all Admin unit tests
- Maintain >85% coverage
- Run tests after each component migration

### Integration Tests
- Test component interactions
- Test service integrations
- Test data persistence

### E2E Tests
- Expand existing Playwright tests
- Cover new Designer functionality
- Cover export/import flows

---

## Dependencies

### External Packages to Verify

| Package | Admin | UI | Action |
|---------|-------|-----|--------|
| YamlDotNet | ✅ | ❓ | Add if missing |
| Blazor.Diagrams | ✅ | ✅ | Verify version |
| Blazored.LocalStorage | ✅ | ✅ | Verify version |
| MudBlazor | ✅ | ✅ | Verify version |
| JsonLogic | ✅ | ❓ | Add if missing |

---

## Success Metrics

| Metric | Target | Measurement |
|--------|--------|-------------|
| Components migrated | 100% P0, P1 | Code review |
| Tests passing | 100% | CI pipeline |
| No regressions | 0 new bugs | QA testing |
| User acceptance | Approved | UAT sign-off |
| Admin removed | Complete | Solution clean |
