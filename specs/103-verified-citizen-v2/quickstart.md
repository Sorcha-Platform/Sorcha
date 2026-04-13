# Quickstart: Verified Citizen v2

**Audience**: developers picking up Verified Citizen v2 work fresh — either implementing one of the four phases or onboarding to the resulting platform capabilities afterwards.

**Prerequisites**: a working local Sorcha dev environment (`docker-compose up -d`), .NET 10 SDK, and familiarity with the existing `walkthroughs/HaipVerifiedCitizen` shape.

## Concepts in 60 seconds

- **Open starting action** — an action with `isStartingAction: true` whose sender participant has `walletAddress = null` in the published blueprint. The runtime binds the first qualifying submitter to the participant role on the instance, immutably for the rest of the workflow.
- **Identity primitive** — a Sorcha-managed JSON Schema fragment with an HTTPS `$id` (e.g. `https://schemas.sorcha.dev/core/PostalAddress/v1`), referenced from blueprints via standard JSON Schema `$ref`. Carries its own validation, layout, persona-autofill bindings, and (for postcode) address-lookup intent.
- **Address lookup provider** — a pluggable `IAddressLookupProvider` registered in Tenant Service that resolves a postcode to either validation metadata (postcodes.io, default-on) or full address candidates (OS Places, opt-in).
- **Late binding cache** — a Redis read-through cache for the per-instance participant→wallet map, with a three-tier fallback (cache → instance store → ledger replay).

## How to add a new identity primitive

1. **Create the JSON file** at `blueprints/schemas/sorcha-core/{Name}.v1.json`. Required: `$id` (HTTPS URI under `https://schemas.sorcha.dev/core/`), `type: "object"`, `title`, `properties`, `required` (optional). See `contracts/identity-primitive-format.md` for the full format.

2. **Declare layout** — add `x-pages` and/or `x-sections` at the schema root if the primitive should ship with its own form layout. Keep them simple; the consuming blueprint can override.

3. **Declare persona bindings** — add `x-persona: "<persona path>"` on each property the persona system can autofill. Use the dotted path form (`address.line1`, `defaultEmail`, `dateOfBirth`, etc.). Run the persona attribute path through the seed service validation by booting the Blueprint Service locally — it will reject unknown paths at startup.

4. **Add date constraints** if applicable — for `format: date` properties, use `formatMinimum` / `formatMaximum` with the Sorcha token vocabulary (`today`, `today+/-N{D|M|Y}`).

5. **Run the seed service** — boot Blueprint Service. The `CoreSchemaSeedService` IHostedService scans `blueprints/schemas/sorcha-core/*.json` at startup and upserts into the schema index. Watch for "Loaded core primitive ..." log lines.

6. **Reference from a blueprint**:
   ```jsonc
   "myField": { "$ref": "https://schemas.sorcha.dev/core/{Name}/v1" }
   ```

7. **Test in the form renderer** — use the Sorcha UI's blueprint preview (or the Verified Citizen v2 walkthrough) to render the form. Verify autofill, validation, and layout work as expected.

8. **Add unit tests** in `tests/Sorcha.Validator.Service.Tests/SchemaRefResolverTests.cs` for any new behaviour your primitive depends on.

## How to override layout while keeping a primitive's properties

Declare layout extensions as siblings to the `$ref` in the consuming blueprint:

```jsonc
"address": {
  "$ref": "https://schemas.sorcha.dev/core/PostalAddress/v1",
  "x-sections": [
    { "title": "Compact Address", "layout": "horizontal", "fields": ["line1", "town", "postcode", "country"] }
  ]
}
```

The override `x-sections` wins for layout. The component's `properties`, `required`, persona bindings, and `x-address-lookup` carry through unchanged.

**Forbidden**: overriding `properties`, `required`, or `type`. The resolver silently drops these (or surfaces a publish-time warning, depending on the planning decision). If you need different fields, version the primitive (`v2`) instead.

## How to add a new address lookup provider for a new country

1. **Implement `IAddressLookupProvider`** in `src/Common/Sorcha.AddressLookup/Providers/`. Required:
   - `ProviderName` — short identifier (e.g. `"finland-paf"`)
   - `Capability` — `ValidateOnly` or `FullAddress`
   - `SupportedCountries` — ISO 3166-1 alpha-2 codes (e.g. `["FI"]`)
   - `IsAvailableAsync()` — health check, called periodically
   - `LookupAsync(postcode, countryHint, ct)` — the actual lookup

2. **Register the provider** in `src/Services/Sorcha.Tenant.Service/Program.cs`:
   ```csharp
   builder.Services.AddSorchaAddressLookup()
       .AddProvider<FinlandPafProvider>();
   ```

3. **Add config** if the provider needs credentials. Convention: `Tenant:AddressLookup:{ProviderName}:ApiKey`. Use the standard `IOptions<T>` pattern.

4. **Add unit tests** in a new `tests/Sorcha.AddressLookup.Tests/Providers/` folder. Mock the HttpClient using the existing test helpers.

5. **Verify selection** — the `AddressLookupService` picks the most capable available provider for the country at request time. Add an integration test that boots Tenant Service with both the new provider and the existing UK providers, and asserts the right one is selected for a Finnish postcode.

## How to consume a primitive from a credential-bootstrapped flow

A "credential-bootstrapped" flow gates the open starting action on the submitter holding a particular Verifiable Credential (e.g. Driving Licence requires Verified Citizen). The pattern:

```jsonc
{
  "participants": [
    { "id": "applicant", "name": "Applicant" }
  ],
  "actions": [
    {
      "id": 1,
      "isStartingAction": true,
      "sender": "applicant",
      "credentialRequirements": [
        {
          "type": "VerifiedCitizenCredential",
          "presentationSource": "HaipExternalWallet",
          "requiredClaims": [
            { "claimName": "givenName" },
            { "claimName": "dateOfBirth" }
          ]
        }
      ],
      "dataSchemas": [
        {
          "type": "object",
          "properties": {
            "licenceClass": { "type": "string", "enum": ["A", "B", "C"] }
          }
        }
      ]
    }
  ]
}
```

Note that the data schema for the licence application does NOT ask for the applicant's name or date of birth — those come from the Verified Citizen credential they present. The HAIP presentation pipeline at `ActionExecutionService.cs:218-269` runs *before* the late-bind block at line 309, so only credential holders become bound applicants.

## How to run the Verified Citizen v2 walkthrough

### Against local Docker

```bash
docker-compose up -d                                                # all services up
pwsh ./walkthroughs/HaipVerifiedCitizen/setup.ps1 -Profile gateway   # creates orgs, blueprint, instance
pwsh ./walkthroughs/HaipVerifiedCitizen/run.ps1                      # runs the workflow end-to-end
```

Expected: the walkthrough completes with a `VerifiedCitizenCredential` SD-JWT VC delivered to the citizen's external HAIP wallet. The credential carries `givenName`, `middleName`, `familyName`, `dateOfBirth`, `email`, and a structured `address`.

### Against n1.sorcha.dev

```bash
pwsh ./walkthroughs/HaipVerifiedCitizen/setup.ps1 -Profile n1
pwsh ./walkthroughs/HaipVerifiedCitizen/run.ps1
```

If the run fails with `"Wallet X is not authorized to execute action 1"`, the walkthrough is using the v1 (broken) shape. Check that `setup.ps1` does NOT include `citizen` in `$walletMap` — the participant must be late-bound, not pre-baked. See [walkthrough-builder skill](../../.claude/skills/walkthrough-builder/SKILL.md) for the foot-gun callout.

## How to debug late binding

### Where the binding lives

| Layer | Storage | Lifetime |
|---|---|---|
| In-memory | `Instance.ParticipantWallets` dictionary | Per-process |
| Cache | Redis key `instance:{instanceId}:bindings` | 1h sliding TTL |
| Authoritative | The signed Action 1 transaction in the register | Permanent |

### Inspecting the cache

```bash
docker exec -it sorcha-redis redis-cli
> KEYS instance:*:bindings
> GET instance:abc-123-...:bindings
```

Expected value: a JSON object mapping participant id → wallet address, e.g. `{"citizen":"ws1qz...","assessor":"ws1qz..."}`.

### Forcing a ledger replay

Delete the Redis key and the instance store entry to force the three-tier fallback to fall all the way through to the ledger:

```bash
docker exec -it sorcha-redis redis-cli DEL instance:abc-123-...:bindings
# ... and clear the Mongo instance document if it exists
```

The next call to `GetBindingsAsync` will replay the action chain for that instance and reconstruct the binding from the canonical sender. Watch for `binding.cache_result=miss-ledger-replay` in the OTel span.

### Common late-binding errors

| Error | Likely cause | Fix |
|---|---|---|
| `Wallet X is not authorized to execute action 1` | Walkthrough pre-bound the participant in `$walletMap` | Remove the participant entry from `$walletMap` |
| `Participant 'X' is already bound to wallet Y` | Second submission attempt on a bound instance | Start a new instance instead — bindings are immutable |
| `Participant 'X' is the sender of starting action N and must have a null walletAddress` | Publish-time guardrail (VAL_BP_010) | Remove `walletAddress` from the participant in the blueprint |

## How to debug schema component resolution

### Where the resolver runs

`SchemaRefResolver` runs inside the validator pipeline (Validator Service) just before `JsonSchema.FromText()`. The flatten step happens once per validation; results are cached in the Mongo schema index.

### Forcing a resolver refresh

Delete the cached resolved form from Mongo:

```bash
docker exec -it sorcha-mongo mongosh
> use sorcha
> db.schemaIndex.find({"$id": "https://schemas.sorcha.dev/core/PostalAddress/v1"})
> db.schemaIndex.deleteOne({"$id": "https://schemas.sorcha.dev/core/PostalAddress/v1"})
```

Restart Blueprint Service. The `CoreSchemaSeedService` repopulates from disk. The next blueprint validation re-resolves from the fresh source.

### Common resolver errors

| Error | Likely cause | Fix |
|---|---|---|
| `Cannot resolve $ref 'https://schemas.sorcha.dev/core/Foo/v1'` | Primitive file missing or `$id` doesn't match file name | Check `blueprints/schemas/sorcha-core/Foo.v1.json` exists with the matching `$id` |
| `Cycle detected in $ref chain` | Primitive A refs B refs A | Refactor to break the cycle; primitives should be acyclic |
| `Layout override on properties is not supported` | Consuming blueprint declared `properties` as a sibling to `$ref` | Remove the override; properties are component-owned |
| `Invalid date token: tomorrow` | Used a non-vocabulary token in `formatMinimum`/`formatMaximum` | Use `today`, `today+/-N{D|M|Y}`, or a literal ISO date |

## How to verify the address lookup degraded path

```bash
# Disable the OS Places provider (if configured) and clear postcodes.io as a test
docker-compose stop tenant-service
# Edit appsettings to remove all address-lookup providers
docker-compose start tenant-service
```

Open the Verified Citizen application form and fill in the postcode. The field should render as a plain text input with no lookup affordance. Submit the rest of the form normally — the workflow must complete without the lookup.

## How to add a new acceptance test for the v2 flow

E2E tests for Verified Citizen v2 live in `tests/Sorcha.UI.E2E.Tests/Docker/VerifiedCitizenV2Tests.cs`. Pattern (see `sorcha-ui` skill):

1. Create a page object in `PageObjects/VerifiedCitizenV2Page.cs` with locators for the form fields, the postcode lookup picker, the submit button, and the credential confirmation modal.
2. Inherit from `AuthenticatedDockerTestBase` so the test runs against the Docker compose stack with a logged-in user.
3. Use `[Test] [Retry(2)]` decorators per the existing convention.
4. The base class auto-checks console errors, network 5xx, and CSS health on every test, so you only need to assert your specific behaviour.

```bash
dotnet test tests/Sorcha.UI.E2E.Tests --filter "Category=VerifiedCitizenV2"
```
