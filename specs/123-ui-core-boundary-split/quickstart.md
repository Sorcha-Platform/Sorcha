# Quickstart — UI.Core User/Admin Boundary Convention

This is the guide for working in `Sorcha.UI.Core` after Feature 123. It tells future contributors how to pick the right folder, name the right interface, and avoid re-introducing bi-modality.

A condensed version is also written to `src/Apps/Sorcha.UI/Sorcha.UI.Core/README.md` for in-repo discovery.

## The Convention

`Sorcha.UI.Core` partitions every service and model file by **audience** — who the code serves:

- **`User/`** — code consumed by end-user pages: workflow participation, credential viewing, form filling, persona management, register subscription, transaction display.
- **`Admin/`** — code consumed by org-admin or designer pages: tenant configuration, blueprint authoring, validator management, register governance, register policy, system register administration.
- **`Shared/`** — code consumed by both audiences: alerts, identity context, navigation, HTTP infrastructure, shared DTOs.

The partition lives at the **folder level**. A file's audience is identifiable from its path alone.

## Adding a new service interface

### Step 1 — Decide the audience

Ask: "Does an end user on a user-facing page need this operation?"

- **Yes, only end users** → `Services/User/`
- **Yes, only admin/designer** → `Services/Admin/`
- **Both** → see Step 2

### Step 2 — Resolve cross-audience cases

If your operation is genuinely needed by both audiences (the textbook example: "get an organisation's display name and branding for an org-card render"):

- Create a narrow read interface in `Services/Shared/<Subject>/I<Subject>ReadService.cs`. Name it descriptively.
- The Shared interface contains only the read methods both audiences need.
- The Admin interface (if it exists) does NOT inherit from the Shared interface. Admin pages that need both inject both — explicit dual-injection.

### Step 3 — Place the file

- One interface per file.
- One concrete class per file (or one class implementing multiple interfaces in one file if they're tightly coupled).
- Folder structure mirrors the audience: `Services/User/Forms/IFormSchemaService.cs`, not `Services/Forms/User/IFormSchemaService.cs`.

### Step 4 — DI registration

Register in `Extensions/ServiceCollectionExtensions.cs` under the audience-appropriate `AddUser*` / `AddAdmin*` / `AddShared*` extension method group. (The current registration grouping is part of Feature 123's deliverable; see the extension class for the established pattern.)

## Adding a new model type

### Step 1 — Decide the audience (same as for services)

### Step 2 — Place the file

- One type per file.
- Folder structure mirrors the audience: `Models/User/Forms/MyNewModel.cs`, not `Models/Forms/User/MyNewModel.cs`.
- If your subject (e.g., "Registers") has both user-facing and admin types, **the user-facing types go under `Models/User/<Subject>/`** and the admin/governance types go under `Models/Admin/<Subject>/`. The subject name is the same; the audience folder picks which side.

### Step 3 — Namespace policy

**Folders do not change namespaces.** A new model in `Models/User/Forms/` declares `namespace Sorcha.UI.Core.Models.Forms;` — the audience folder is not part of the namespace path. This is intentional: consumer `using` directives stay short and focused on the subject, and audience information is encoded at the file-system level only.

If you find yourself wanting to write `namespace Sorcha.UI.Core.Models.User.Forms;`, stop and use the subject-level namespace instead.

## Adding a new bi-modal-looking interface — DON'T

The whole point of Feature 123 is that interfaces in `Sorcha.UI.Core/Services/` are single-audience. If your new interface has both "list things for the signed-in user" methods and "manage admin policy" methods, **it must be split into two interfaces from day one** — one user, one admin, registered against the same concrete class.

This is the discipline that prevents the bug Feature 122 Phase 2 hit. A bi-modal interface is a debt that compounds — every new consumer reinforces it.

## When extracting a DTO from a service file

If you find yourself adding a record type next to a service interface and you suspect it'll be referenced by both audiences, apply the **shared DTO extraction pattern** documented at `contracts/shared-dto-extraction-pattern.md`:

- DTO goes to `Services/Shared/<Subject>/<DtoName>.cs`.
- Namespace stays as `Sorcha.UI.Core.Services` (the DTO's logical home).
- The original service file gets a one-line comment noting where the DTO moved.

## When you need to inject something cross-audience

A user-facing page that genuinely needs an org's name + branding injects `IOrganizationReadService` (the Shared interface).

An admin page that needs both org administration *and* the read operation injects `IOrganizationAdminService` AND `IOrganizationReadService` — two `@inject` directives, two constructor parameters.

Do NOT inherit one interface from the other. Do NOT add the read method as a duplicate to the admin interface. The explicit dual-injection is the convention.

## Bi-modal smell detector

If you find yourself writing or reviewing code with any of these symptoms, you're heading toward a Feature-122-Phase-2-style problem. Stop and refactor before the bi-modality solidifies:

- An interface in `Services/` (top-level, not under `User/`/`Admin/`/`Shared/`).
- An interface whose name is just `IFooService` (no audience suffix when the operations are mixed).
- A DTO type defined inside a service interface file (it should be in its own file).
- A user-facing component injecting an interface named `*AdminService`.
- An admin page injecting a Shared read interface to get one method but transitively needing admin-only types from it.

## Verifying your work

After adding any new service or model file:

1. `dotnet build src/Apps/Sorcha.UI/Sorcha.UI.Core/` — must succeed.
2. The new file lives under one of `User/`, `Admin/`, `Shared/` — not directly under `Services/` or `Models/`.
3. Consumers of your new type inject the audience-correct interface — no user-facing page injects an `*AdminService`, no admin page injects a Shared read interface to get an admin operation.

If your new file doesn't fit cleanly into the convention, that's a signal — either the audience classification is wrong (rethink which folder), or the interface is bi-modal (split it), or the DTO needs its own file (extract).
