# Feature Specification: Add Missing XML Doc Summaries to One Public C# Class

**Feature Branch**: `156-add-xml-doc-summaries`

**Created**: 2026-06-23

**Status**: Draft

**Input**: User description: "Find one public C# class in the Sorcha codebase whose public members (methods, properties, or the type itself) are missing /// <summary> XML documentation comments, and add concise, accurate XML doc summaries to that class public members. Limit the change to that single file. Per the project convention, all public API members should have /// <summary> to avoid build warnings. Keep it minimal and focused on the one file."

## User Scenarios & Testing *(mandatory)*

### User Story 1 - Document the undocumented public surface of one class (Priority: P1)

A maintainer locates a single public C# class whose public surface (the type itself and/or its public methods and properties) lacks `/// <summary>` XML documentation, and adds concise, accurate summaries so the class's public API is fully self-describing and no longer triggers missing-documentation build warnings for that file.

**Why this priority**: This is the entire deliverable. The project convention (and the documented build policy) requires every public API member to carry a `/// <summary>`. A single fully-documented file removes warnings for that file and improves IntelliSense/OpenAPI/generated-doc quality with a small, low-risk, reviewable change.

**Independent Test**: Pick the chosen file, confirm every public member previously lacked a `/// <summary>`, apply summaries, and verify each public member now has one. The class compiles unchanged in behavior and the file no longer produces missing-XML-doc warnings.

**Acceptance Scenarios**:

1. **Given** a public C# class with one or more public members missing `/// <summary>`, **When** the maintainer adds summaries, **Then** every public member of that class (and the type itself) has an accurate `/// <summary>`.
2. **Given** the documented class, **When** the solution is built, **Then** that file produces no missing-XML-documentation warnings.
3. **Given** the change, **When** the diff is reviewed, **Then** only the single chosen file is modified and no executable code (logic, signatures, accessibility) is changed.

### Edge Cases

- What happens when a member already has a `/// <summary>`? It is left untouched — only missing summaries are added.
- What happens when the class has non-`<summary>` doc tags (e.g. `<param>`, `<returns>`) but no `<summary>`? The missing `<summary>` is added; existing tags are preserved.
- What happens if a member is public but documenting it would be misleading without deeper context? The summary describes observable intent at the API level without asserting implementation details that cannot be verified from the signature and surrounding code.
- What happens to inherited or interface-implementing members? A concise summary is still added (or `/// <inheritdoc/>` used where the member directly implements a documented interface member and that is the accurate intent).

## Requirements *(mandatory)*

### Functional Requirements

- **FR-001**: The change MUST identify exactly one public C# class in the codebase that currently has one or more public members (the type, public methods, or public properties) missing a `/// <summary>` XML documentation comment.
- **FR-002**: The change MUST add a `/// <summary>` to every public member of that class that lacks one, including the type declaration itself if undocumented.
- **FR-003**: Each added summary MUST be concise and accurate — describing the member's purpose without restating its name verbatim or asserting unverifiable behavior.
- **FR-004**: The change MUST be limited to a single file; no other files may be modified.
- **FR-005**: The change MUST NOT alter any executable code, method signatures, accessibility modifiers, or runtime behavior — it adds documentation comments only.
- **FR-006**: Existing documentation comments on already-documented members MUST be preserved unchanged.
- **FR-007**: Added summaries MUST conform to the project's documentation policy and C# XML-doc conventions (well-formed `/// <summary> ... </summary>` blocks).

### Key Entities

- **Target class file**: The single `.cs` file selected for documentation. Attributes: file path, the public type it declares, and the set of public members requiring summaries.
- **Public member**: A type, method, or property with public accessibility that forms part of the documented API surface and is subject to the `/// <summary>` convention.

## Success Criteria *(mandatory)*

### Measurable Outcomes

- **SC-001**: 100% of the chosen class's public members (and the type itself) have a `/// <summary>` after the change.
- **SC-002**: The chosen file produces zero missing-XML-documentation build warnings after the change.
- **SC-003**: Exactly one file is changed in the resulting diff.
- **SC-004**: Zero lines of executable code (signatures, logic, modifiers) are changed — the diff contains only documentation-comment additions.
- **SC-005**: A reviewer can confirm each added summary accurately describes its member by reading the member's signature and surrounding code, with no factual inaccuracies.

## Assumptions

- The codebase enables XML documentation generation (or treats missing public-member docs as warnings), making the convention enforceable per the project's documentation policy.
- "Public members" covers the public type declaration and its public methods and properties; protected/internal/private members are out of scope for this change.
- A suitable single-file candidate exists with multiple undocumented public members so the change delivers meaningful value while staying minimal.
- Where a public member directly implements a documented interface or base member, `/// <inheritdoc/>` is an acceptable form of summary when it accurately reflects intent.
- No accompanying README or reference-doc update is required because this change only adds inline XML summaries and does not add or alter any endpoint, configuration, or architectural pattern.
