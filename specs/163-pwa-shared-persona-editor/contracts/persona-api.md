# Contract: Persona API (`/api/me/persona`)

**Status for this feature: UNCHANGED / REFERENCE ONLY.** Feature 163 introduces no new endpoints and
no payload changes. This document records the existing contract the shared `PersonaEditor` consumes
(via `IPersonaClient` / `PersonaHttpClient`) so the client wiring on both hosts targets the same shape.

Served by the Tenant Service (Feature 092), reached through the API Gateway with a **consumer-tier**
JWT. The PWA must register its persona `HttpClient` with the same authenticated chain
(`BearerTokenHandler` + `ServerClockHandler`) used by its other consumer clients.

---

## GET /api/me/persona

Read the signed-in citizen's persona.

- **Query**: `actingAs` (optional, default `self`; only `self` accepted in v1).
- **200 OK** → `PersonaReadModelV1` (JSON). A citizen with **no** saved persona returns a 200 with
  empty/null fields — **never** a 404. Client maps this to an empty editable form.
- **401** → unauthenticated/expired session (host redirects to auth).
- Client method: `IPersonaClient.GetPersonaAsync(actingAs = "self")` — returns `null` on non-success
  so the caller distinguishes transient failure from an empty persona.

## PUT /api/me/persona

Replace (full-replace, not patch) the citizen's persona.

- **Body**: `PersonaAttributesV1` (JSON).
- **200 OK** → server-canonical `PersonaReadModelV1`.
- **400 Bad Request** → field-level validation errors. Client throws
  `PersonaValidationException(IReadOnlyDictionary<string,string[]> Errors)`.
  Example codes: `invalid_email`, `multiple_defaults`, list-cap and ISO-code violations.
- **409 Conflict** → wallet not provisioned. Client throws `PersonaWalletNotProvisionedException`.
- **401** → unauthenticated/expired.
- Client method: `IPersonaClient.PutPersonaAsync(PersonaAttributesV1)`.

## DELETE /api/me/persona

Delete the citizen's persona. Idempotent.

- **204 No Content** → whether or not a row existed.
- Client method: `IPersonaClient.DeletePersonaAsync()`.

---

## Client-layer contract (consumed by `PersonaEditor`)

`IPersonaService` (cached wrapper over `IPersonaClient`):

| Member | Behaviour |
|--------|-----------|
| `GetAsync(PersonaReadOptions?, ct)` | Returns `PersonaReadModelV1` (empty, not null, when none). Session cache. |
| `UpdateAsync(PersonaAttributesV1, ct)` | PUT; returns canonical read model; invalidates cache; throws the typed exceptions above. |
| `DeleteAsync(ct)` | DELETE; idempotent; invalidates cache. |
| `GetAutofillEnabledAsync()` / `SetAutofillEnabledAsync(bool)` | Autofill preference (Blazored local storage — requires `ILocalStorageService` registered on the host). |
| `InvalidateCache()` | Clears session cache (logout / org-switch). |

**Host DI requirement (the gap this feature closes for the PWA):**
- `IPersonaClient` → authenticated typed `HttpClient` (Bearer + ServerClock) at the gateway base address.
- `IPersonaService` → scoped.
- `ILocalStorageService` → `AddBlazoredLocalStorage()` (already on web; **missing on PWA**).
