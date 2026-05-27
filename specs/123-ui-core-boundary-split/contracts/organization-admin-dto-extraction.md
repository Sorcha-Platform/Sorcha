# Contract — `IOrganizationAdminService` DTO Extraction + `IOrganizationReadService` Introduction

This contract covers two related actions: extracting shared DTOs out of the admin-service file, and introducing a new narrow read interface for user-facing org-card consumers (R7's cross-audience convention applied to organisations).

## Before

`Sorcha.UI.Core/Services/IOrganizationAdminService.cs` is a single file containing:
- The `IOrganizationAdminService` interface (~14 admin operations)
- `OrganizationDto`, `BrandingDto`, `UserDto` — shared DTOs (referenced by both admin pages and user-facing components)
- `AddUserDto`, `UpdateUserDto`, `CreateOrganizationDto`, `UpdateOrganizationDto`, `SubdomainValidationResult`, `OrganizationListResult`, `UserListResult`, `PlatformKpis` — admin-only DTOs

User-facing components that today want `OrganizationDto` (for org-card display, branding rendering) have to either inject `IOrganizationAdminService` (acquiring the whole admin surface unnecessarily) or duplicate the DTO shape locally.

## After

### Shared DTOs extracted

Three new files in `Sorcha.UI.Core/Services/Shared/Organization/`:

```csharp
// OrganizationDto.cs
namespace Sorcha.UI.Core.Services;

/// <summary>
/// Organization DTO for client-side use. Shared between user-facing org-card
/// renders and admin org-management screens.
/// </summary>
public record OrganizationDto
{
    public Guid Id { get; init; }
    public string Name { get; init; } = string.Empty;
    public string Subdomain { get; init; } = string.Empty;
    public string Status { get; init; } = "Active";
    public DateTimeOffset CreatedAt { get; init; }
    public BrandingDto? Branding { get; init; }
}
```

```csharp
// BrandingDto.cs
namespace Sorcha.UI.Core.Services;

/// <summary>
/// Branding configuration DTO. Shared between user-facing branding
/// renders and admin branding-editor screens.
/// </summary>
public record BrandingDto
{
    public string? LogoUrl { get; init; }
    public string? PrimaryColor { get; init; }
    public string? SecondaryColor { get; init; }
    public string? CompanyTagline { get; init; }
}
```

```csharp
// UserDto.cs
namespace Sorcha.UI.Core.Services;

/// <summary>
/// User DTO for client-side use. Shared between user-facing user-display
/// (e.g., session-user info) and admin user-management screens.
/// </summary>
public record UserDto
{
    // existing fields preserved verbatim from the original definition
}
```

Namespaces preserved (`Sorcha.UI.Core.Services`) so consumer `using` directives are unaffected.

### Admin-only DTOs grouped

A new file `Sorcha.UI.Core/Services/Admin/OrganizationAdminDtos.cs` contains every admin-only DTO that previously lived alongside the interface in `IOrganizationAdminService.cs`:

```csharp
// OrganizationAdminDtos.cs
namespace Sorcha.UI.Core.Services;

public record AddUserDto { ... }
public record UpdateUserDto { ... }
public record CreateOrganizationDto { ... }
public record UpdateOrganizationDto { ... }
public record SubdomainValidationResult { ... }
public record OrganizationListResult { ... }
public record UserListResult { ... }
public record PlatformKpis { ... }
```

### Admin interface stays as-is (renamed home)

`Sorcha.UI.Core/Services/Admin/IOrganizationAdminService.cs` retains the interface declaration only. All ~14 admin operations stay. Methods returning shared DTOs (e.g., `GetOrganizationAsync(Guid) → OrganizationDto?`) compile fine since the DTO types are still in the `Sorcha.UI.Core.Services` namespace.

### New user-facing read interface

```csharp
// Sorcha.UI.Core/Services/Shared/Organization/IOrganizationReadService.cs
namespace Sorcha.UI.Core.Services;

/// <summary>
/// User-facing organization read operations. Consumed by org-card renders,
/// session-context org info, and other read-only org-display surfaces.
/// </summary>
public interface IOrganizationReadService
{
    Task<OrganizationDto?> GetOrganizationAsync(Guid id, CancellationToken cancellationToken = default);
}
```

Concrete implementation: either a thin wrapper that delegates to the existing admin service (cleanest) or a separate read-only class that hits the same HTTP endpoint. Implementation choice is left to the Phase 2 task.

Per R7: admin consumers that need the read operation inject `IOrganizationReadService` *in addition to* `IOrganizationAdminService` — no inheritance between the two.

## Migration path for consumers

1. User-facing components that today either inject `IOrganizationAdminService` or duplicate the DTO shape locally → switch to injecting `IOrganizationReadService` and consuming `OrganizationDto` / `BrandingDto` from the shared location.
2. Admin pages that today inject `IOrganizationAdminService` and call `GetOrganizationAsync` → continue to inject `IOrganizationAdminService`; the interface still has its admin methods. If the admin page only does read-and-display, switch to `IOrganizationReadService`.
3. No DTO renames, no namespace changes — consumer `using` blocks are unchanged.

## Verification

1. **Given** a user-facing component that renders org branding, **When** it consumes `IOrganizationReadService` and reads `OrganizationDto.Branding`, **Then** the rendered branding matches the pre-refactor rendering when the same org is loaded.
2. **Given** an admin page that creates an org, **When** it injects `IOrganizationAdminService` and calls `CreateOrganizationAsync`, **Then** the call behaves identically to pre-refactor — same HTTP endpoint, same response shape, same UI handling.
3. **Given** the refactored codebase, **When** a developer greps for `OrganizationDto`, `BrandingDto`, `UserDto`, **Then** every reference resolves through the `Sorcha.UI.Core.Services` namespace (preserved) but the type definitions live in their new files under `Services/Shared/Organization/`.
