# Contract — Shared DTO Extraction Pattern

This contract formalises the pattern for extracting DTOs that are co-located with a service interface but actually used by both user and admin consumers. The pattern was first applied informally during the Feature 122 Phase 2 attempt (`SchemaOverlayFieldInfo`, `OrganizationDto`) and is locked here as the canonical approach for future contributors.

## When to apply

Apply this pattern when **all** of the following are true:

1. A DTO type lives inside a service-interface file (e.g., `IFooAdminService.cs` contains `public record FooDto { ... }`).
2. The DTO is referenced by code outside that service — specifically by code with a different audience classification than the service itself.
3. Moving the service to its audience folder would force the cross-audience consumer to either inherit the service surface (wrong) or duplicate the DTO (worse).

Do NOT apply when:

- The DTO is only used as a request/response type for the single service it lives next to. Keep it co-located.
- The "DTO" is actually a result wrapper specific to one operation (e.g., `OrganizationListResult` wraps `OrganizationDto[]`). Keep the wrapper with the operation; only the inner type extracts.

## Pattern

### Step 1 — Identify the extraction target

The DTO has a name like `<Thing>Dto`, `<Thing>ViewModel`, or `<Thing>Info`. It carries data, not behaviour (records or POCOs). It has no constructor logic that depends on the surrounding service.

### Step 2 — Create a new file in the SHARED audience folder

Path: `Services/Shared/<Subject>/<DtoName>.cs`, where `<Subject>` is a noun describing the DTO's domain (e.g., `Organization`, `Blueprints`).

```csharp
// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

namespace Sorcha.UI.Core.Services;

/// <summary>
/// [Original XML doc preserved verbatim from the source file]
/// </summary>
/// <remarks>
/// Extracted from <see cref="I<OriginalServiceName>"/> as part of Feature 123
/// so user-facing components can reference this type without inheriting the
/// admin service surface.
/// </remarks>
public record <DtoName>
{
    // members preserved verbatim from the source definition
}
```

### Step 3 — Delete the original definition

In the source file (e.g., `IFooAdminService.cs`), delete the `public record FooDto { ... }` block. Replace with a one-line comment:

```csharp
// FooDto moved to Services/Shared/<Subject>/FooDto.cs (Feature 123) so
// user-facing components can reference it without inheriting this admin
// service's surface.
```

### Step 4 — Verify

The codebase still builds. The DTO type still resolves from its preserved namespace. Consumer `using` directives are unchanged.

## Namespace preservation rule

**The DTO's namespace stays the same after extraction.** Even though the DTO moved from `Services/IFooAdminService.cs` to `Services/Shared/Organization/FooDto.cs`, the namespace remains `Sorcha.UI.Core.Services`. The folder-based audience-tag convention (R2) lives in the folder structure, not in the namespace.

This is what keeps consumer `using` directives unchanged across the refactor.

## When several DTOs share an extraction target

If three DTOs in `IFooAdminService.cs` are all shared (e.g., `FooDto`, `BrandingDto`, `ContactDto`), each goes into its own file under `Services/Shared/<Subject>/`. Per-file convention matches the existing codebase pattern.

If a DTO is admin-only but still wants to leave the interface file (for tidiness), the same extraction pattern applies but the target folder is `Services/Admin/<Subject>Dtos.cs` (or similar), and the audience-tag is ADMIN not SHARED. This is the case for `AddUserDto`, `UpdateOrganizationDto`, etc. — they stay admin but get tidied into a sibling file.

## Verification (per extraction)

1. **Given** the DTO's pre-extraction namespace `Sorcha.UI.Core.Services`, **When** a consumer's `using Sorcha.UI.Core.Services;` is processed after the refactor, **Then** the DTO is found exactly as before.
2. **Given** the DTO has been extracted into a SHARED folder, **When** a future contributor wants to add a new shared organisation DTO, **Then** they discover the existing `Services/Shared/Organization/` folder and place the new DTO there on the first attempt.
3. **Given** the original service file is opened, **When** the reader scans it, **Then** the file contains only the service interface declaration plus comments noting where DTOs moved (no business-relevant code is lost).
