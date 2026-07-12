# Contract: `x-claim-source` schema extension

**Owner**: `Sorcha.UI.Components.User` (form renderer). Consumed at form init; tolerated (stripped) by all schema validators via the existing `x-*` strip (F137 precedent).

## Shape

```jsonc
{
  "emailVerified": {
    "type": "boolean",
    "title": "Email verified",
    "readOnly": true,
    "default": true,
    "x-claim-source": "email_verified"   // ← the claim to seed from
  }
}
```

`x-claim-source` is a non-empty string naming a JWT claim present on the authenticated `ClaimsPrincipal`.

## Resolver contract (`IClaimSourceSeeder`)

```csharp
public interface IClaimSourceSeeder
{
    /// Walk top-level schema properties carrying `x-claim-source`, read the named
    /// claim from `user`, coerce to the property's declared `type`, and return
    /// pointer→value (leading-slash JSON Pointer, e.g. "/emailVerified" → true).
    IReadOnlyDictionary<string, object?> Resolve(JsonDocument? mergedSchema, ClaimsPrincipal? user);
}
```

### Behaviour table

| Property `type` | Claim present? | Claim value | Result pointer value |
|-----------------|----------------|-------------|----------------------|
| `boolean` | yes | `"true"` (any case) | `true` |
| `boolean` | yes | anything else | `false` |
| `boolean` | no | — | `false` (fail closed) |
| non-boolean | yes | `s` | `s` (string) |
| non-boolean | no | — | not seeded |
| (no `x-claim-source`) | — | — | not seeded |
| `mergedSchema` or `user` null | — | — | empty map |

## Renderer integration contract

- Seeding runs once per action at form init (`SorchaFormRenderer`, on `actionChanged`), reading the state from `AuthenticationStateProvider`.
- Each resolved pointer is written to `FormContext.FormData` **only if the user has not already set it** (never clobbers user input).
- Resolved via `IServiceProvider.GetService` (graceful skip when unregistered, e.g. bUnit) — mirrors persona autofill.
- Values flow unchanged to the wire: `FormData` → `FormSubmission.Data` → `FormPayloadBuilder.BuildNested` → `PayloadData` → wallet-signed.

## Acceptance

- Given the AIAS action-1 schema and a principal with `email_verified=true`, `Resolve` yields `{ "/emailVerified": true }`.
- Given the same with `email_verified=false` or absent, yields `{ "/emailVerified": false }`.
- Given a schema property without `x-claim-source`, that property is absent from the result.
