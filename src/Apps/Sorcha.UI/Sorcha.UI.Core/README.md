# Sorcha.UI.Core — Audience-Tag Convention

Shared service and model library for the Sorcha.UI family (Admin, App, Designer, Explorer, Web, Web.Client). Every type in this project is partitioned by **audience** at the folder level. The audience of a file is identifiable from its path alone.

> Full rationale and worked examples: `specs/123-ui-core-boundary-split/quickstart.md`.
> Motivating discovery (why this exists): `specs/122-shared-user-components/phase-2-discovery.md`.

## Folder layout

```
Services/
  User/        end-user pages: workflow participation, credentials, forms, personas, registers (read), transactions
  Admin/       org-admin + designer: tenant config, blueprints, validators, register governance, system register
  Shared/      both audiences: alerts, identity, navigation, HTTP, shared DTOs (e.g. OrganizationDto, BrandingDto)

Models/
  User/        models referenced by user-facing services + pages
  Admin/       models referenced by admin/designer services + pages (incl. GovernanceRosterViewModel, schema/canvas types)
  Shared/      models referenced by both (e.g. ActivityEventDto, Common/)
```

Subject folders nest *under* the audience: `Services/User/Forms/`, **not** `Services/Forms/User/`.

## Namespace policy — load-bearing

**Folders do not change namespaces.** A file in `Models/User/Forms/` declares `namespace Sorcha.UI.Core.Models.Forms;` — the audience folder is filesystem-only metadata.

This is what keeps consumer `using` directives stable across moves. If you find yourself typing `namespace Sorcha.UI.Core.Models.User.Forms;`, stop and use the subject-level namespace.

## Adding a new service

1. **Pick the audience.** Ask: *does an end user on a user-facing page need this?*
   - Only end users → `Services/User/`
   - Only admin/designer → `Services/Admin/`
   - Both → `Services/Shared/<Subject>/` with a narrow read interface (`I<Subject>ReadService`)
2. **One interface per file. One concrete per file.** Existing pattern: concrete in same audience folder as its interface (or under `Admin/` when only admin pages instantiate it).
3. **Register DI** in `Extensions/ServiceCollectionExtensions.cs` under the audience-appropriate group.
4. **Cross-audience access uses explicit dual-injection** — admin pages that need both governance and read inject `IOrganizationAdminService` AND `IOrganizationReadService`. The Shared interface does NOT inherit from the Admin interface.

## Adding a new model

1. Pick the audience (same question as for services).
2. Place under `Models/<Audience>/<Subject>/`. If a subject has both flavours (e.g. `Registers`), the user types go under `Models/User/Registers/` and the admin/governance types under `Models/Admin/Registers/` — same subject name, audience picks the side.
3. Namespace stays at the subject level (`Sorcha.UI.Core.Models.Registers`), not the audience level.

## Bi-modal smell detector

If you see any of these, stop and refactor before the bi-modality solidifies:

- An interface or model file directly under `Services/` or `Models/` (not in `User/` / `Admin/` / `Shared/`).
- An interface whose name is plain `IFooService` and whose methods mix "list things for the signed-in user" with "manage admin policy". Split it — one user, one admin, same concrete class implementing both.
- A DTO record defined inside a service-interface file. Extract it to `Services/Shared/<Subject>/<Dto>.cs` (keep the original namespace).
- A user-facing component injecting an interface named `*AdminService`.
- An admin page injecting a Shared read interface and transitively pulling admin-only types from its return values — that means the "Shared" interface isn't actually shared.

## DTO extraction pattern

When a shared DTO lives next to an admin interface (the textbook case: `OrganizationDto` next to `IOrganizationAdminService`):

1. Extract the DTO to its own file at `Services/Shared/<Subject>/<DtoName>.cs`.
2. **Keep the original namespace** (`Sorcha.UI.Core.Services`) — the DTO's logical home is unchanged, only the file moves.
3. Leave a one-line comment in the original service file noting where the DTO moved.

See `specs/123-ui-core-boundary-split/contracts/shared-dto-extraction-pattern.md`.

## Verifying your work

1. `dotnet build src/Apps/Sorcha.UI/Sorcha.UI.Core/` succeeds.
2. The new file lives under `User/`, `Admin/`, or `Shared/` — not directly under `Services/` or `Models/`.
3. Consumers inject the audience-correct interface. No user-facing page injects a `*AdminService`. No admin page injects a Shared read interface to reach an admin-only operation.

If your file doesn't fit the convention, the convention isn't wrong — one of three things is: the audience classification (rethink the folder), the interface is bi-modal (split it), or a DTO needs to be extracted.
