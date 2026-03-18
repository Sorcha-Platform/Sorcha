# Data Model: AI Blueprint Builder Enhancement

**Branch**: `063-ai-builder-schemas-vc` | **Date**: 2026-03-18

## Entity Overview

This feature introduces no new domain models. It adds a file-based schema library, new AI tools that operate on existing models, and a system prompt rewrite. The data model documents the schema file format and how it maps to existing entities.

## StandardisedSchema (File Format)

A JSON file in `blueprints/schemas/{category}/` that defines a reusable data schema.

| Field | Type | Required | Description |
|-------|------|----------|-------------|
| `identifier` | string | Yes | Unique ID (lowercase, hyphens). Maps to `SchemaEntry.Identifier`. |
| `title` | string | Yes | Human-readable name (3-100 chars). Maps to `SchemaEntry.Title`. |
| `description` | string | Yes | Purpose description (5-500 chars). Maps to `SchemaEntry.Description`. |
| `version` | string | Yes | Semantic version ("1.0.0"). Maps to `SchemaEntry.Version`. |
| `category` | string | Yes | Schema category slug (e.g., "people-identity", "financial", "credentials"). Used for `SchemaEntry.SectorTags[0]` and directory organisation. |
| `tags` | string[] | No | Searchable classification tags. Maps to `SchemaEntry.SectorTags`. |
| `keywords` | string[] | No | Search keywords. Maps to `SchemaEntry.Keywords`. |
| `schema` | object | Yes | JSON Schema (draft 2020-12) defining the data fields. Maps to `SchemaEntry.Content`. |
| `formLayout` | object | No | Default form layout using the `Control` model structure (`VerticalLayout`, `HorizontalLayout`, `Group`, etc.). Stored in `SchemaEntry.Content` under `x-sorcha-formLayout` extension. |
| `disclosure` | object | No | Disclosure recommendations. |
| `disclosure.sensitive` | string[] | No | Field names that are sensitive (NI number, bank details, etc.). |
| `disclosure.recommendation` | string | No | Human-readable disclosure guidance for the AI. |

### Mapping to SchemaEntry

```
File Field          → SchemaEntry Property
─────────────────────────────────────────────
identifier          → Identifier
title               → Title
description         → Description
version             → Version
category            → SectorTags[0] (also: SchemaCategory.System)
tags                → SectorTags (merged with category)
keywords            → Keywords
schema              → Content (with formLayout merged as x-sorcha-formLayout)
(n/a)               → Source = SchemaSource.Internal()
(n/a)               → Status = SchemaStatus.Active
(n/a)               → IsGloballyPublished = true
```

## Existing Models Used (No Modifications)

### CredentialRequirement (Sorcha.Blueprint.Models.Credentials)

Used by the `require_credential` tool. Already exists with:
- `Type` (string) — Credential type name
- `AcceptedIssuers` (IEnumerable<string>) — DID/address list
- `RequiredClaims` (IEnumerable<ClaimConstraint>) — Claim name + optional expected value
- `RevocationCheckPolicy` (enum) — FailClosed | FailOpen
- `Description` (string) — UI label

### CredentialIssuanceConfig (Sorcha.Blueprint.Models.Credentials)

Used by the `issue_credential` tool. Already exists with:
- `CredentialType` (string) — Type being minted
- `ClaimMappings` (IEnumerable<ClaimMapping>) — Source field → claim name
- `RecipientParticipantId` (string) — Who receives the VC
- `ExpiryDuration` (string) — ISO 8601 duration
- `UsagePolicy` (enum) — Reusable | SingleUse | LimitedUse
- `Disclosable` (IEnumerable<string>) — Claims for selective disclosure

### Action (Sorcha.Blueprint.Models)

The target entity for tool operations. Already has:
- `CredentialRequirements` (IEnumerable<CredentialRequirement>)
- `CredentialIssuanceConfig` (CredentialIssuanceConfig)
- `DataSchemas` (IEnumerable<JsonDocument>)
- `Form` (Control) — UI form specification

### SchemaEntry (Sorcha.Blueprint.Schemas.Models)

The storage entity for seeded schemas. Already exists in MongoDB via `ISchemaStore`.

## Schema Categories

| Category Slug | Display Name | Schema Count | Notes |
|---------------|-------------|--------------|-------|
| `people-identity` | People & Identity | 5 | UK/Intl Address, Contact, Personal ID, Company ID |
| `financial` | Financial | 3 | Payment, Invoice Line, Bank Account |
| `documents-evidence` | Documents & Evidence | 3 | Doc Upload, Signature, Audit Entry |
| `compliance-governance` | Compliance & Governance | 3 | Risk, Approval, Due Diligence |
| `supply-chain` | Physical / Supply Chain | 3 | Product, Shipment, Inspection |
| `healthcare` | Healthcare | 2 | Patient Ref, Clinical Observation |
| `credentials` | Credentials | 7 | Training Cert, License, Right-to-Work, ID Verification, Product Passport, Inspection Cert, Approval Attestation |
| **Total** | | **26** | |

## Relationships

```
StandardisedSchema (file)
    ↓ seeded by SchemaSeedService
SchemaEntry (MongoDB)
    ↓ queried by search_schemas tool
    ↓ applied by use_standard_schema tool
Action.DataSchemas (blueprint)
    ↓ rendered by
Action.Form (Control)

CredentialRequirement (on Action)
    ← set by require_credential tool
    ← references credential schema from "credentials" category

CredentialIssuanceConfig (on Action)
    ← set by issue_credential tool
    ← maps action data fields to credential claims
```
