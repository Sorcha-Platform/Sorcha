# My Credentials — zero-tab redesign + selective-disclosure correctness

**Date:** 2026-07-14
**Status:** Approved (design)
**Surface:** `Sorcha.UI.Web.Client` `/app/credentials`, `Sorcha.UI.Components.User` credential components, `Sorcha.Wallet.Service` SD-JWT claim decoder

---

## 1. Why

The page was found overcrowded during AIAS live testing on n1: five tabs (Pending / Active / Expired / Revoked / Inbox), a dense list that prints claim values, and — visible in the field — the `address` claim rendering a raw SD-JWT digest array:

```
address   {"_sd": ["zSH_kfTeW2MlcQf4bGNVE_gNtemoRyOa_WQ1MgDP45E", …]}
```

Investigation found three separate defects behind that screenshot, two of which are correctness bugs rather than styling:

1. Nested selective disclosure is never resolved, so unresolved digest arrays reach the UI.
2. Every claim renders with an "always disclosed" padlock, including the selectively-disclosable ones — the card asserts the opposite of the truth.
3. The Inbox tab is **dead code**: it calls a route that does not exist and has never rendered a row.

## 2. The holder's job

The five tabs organise credentials by **lifecycle state** — the issuer's mental model. A holder arrives with one job:

> *See what I hold, and act on anything that needs me.*

The design follows from that. State becomes a **property of a card**, not a destination you navigate to.

---

## 3. Page structure — zero tabs

`MyCredentials.razor` becomes one scrolling page of three bands. **Each band is omitted entirely when empty.**

| Band | Contents | Notes |
|---|---|---|
| **Needs you** | Credentials in `PendingAcceptance` | Decision cards. Accept / Decline. The **only** place claim detail is expanded at rest. |
| **Your credentials** | `Active` | The main list. Gets the density work below. |
| **Archive** (collapsed, with count) | `Expired` + `Revoked`, merged | Reason survives as a chip on the card ("Expired 3 Jun" / "Revoked by issuer"). |

`MudTabs` is removed. So is the **Inbox** tab (§6).

### 3.1 The Active card at rest

Shows **identity only, plus a one-line summary of claim _names_**:

- Credential name, humanised — `AssuredIdentityCredential` → "Assured Identity"
- Issuer
- Status / expiry chip
- Summary line: *"Name, date of birth, address"* — **names, never values**

Claim **values** appear only when the card is opened.

Rationale beyond density: the list currently prints the holder's home address and email in plain text on a scrollable page. A wallet should look like a wallet of cards, not a dump of their contents.

### 3.2 The pending-offer card

Deliberately **not** the same component. An offer is a decision surface — "do you want to accept this credential?" is unanswerable without seeing what is in it — so claim detail stays expanded here, with **truthful** disclosure indicators (§5).

---

## 4. Bug A — nested selective disclosure is never resolved

**Root cause (server).** `InboundCredentialDetector.ExtractDisclosedClaimsJson`
(`src/Services/Sorcha.Wallet.Service/Services/Implementation/InboundCredentialDetector.cs:593–656`)
strips `_sd` only from the **top level** of the SD-JWT body:

```csharp
var skip = new HashSet<string>(StringComparer.Ordinal)
{
    "iss", "sub", "iat", "exp", "nbf", "jti", "aud", "vct",
    "_sd", "_sd_alg", "cnf", "credentialStatus", "type"
};
foreach (var prop in bodyDoc.RootElement.EnumerateObject())
{
    if (skip.Contains(prop.Name)) continue;
    claims[prop.Name] = JsonElementToValue(prop.Value);   // ← copies address:{_sd:[…]} verbatim
}
```

When the issuer uses **nested** disclosure (`address` is an object whose `town` / `line1` / `postcode` are individually disclosable), the body contains `"address": { "_sd": [digests…] }`. That object is copied verbatim into the stored `ClaimsJson`, while the nested disclosures are written back as **flat top-level keys**. Hence the screenshot: `town` and `line1` correct, `address` a digest array.

**Fix.** Use the recursive resolver that already exists and is currently unused:
`NestedDisclosure.Reconstruct` (`src/Common/Sorcha.Cryptography/SdJwt/NestedDisclosure.cs:184`) —
it resolves nested `_sd` arrays recursively and strips `_sd` / `_sd_alg` at **every** level.
Tested by `tests/Sorcha.Cryptography.Tests/SdJwt/SdJwtNestedDisclosureTests.cs`.

Because this repairs the **stored** `ClaimsJson`, it fixes every downstream consumer — web, PWA sync, and any future one — at the source.

**Defence in depth (UI).** `CredentialApiService.StringifyClaimValue`
(`Sorcha.UI.Components.User/Services/User/Credentials/CredentialApiService.cs:417–430`)
currently falls through to `el.GetRawText()` for objects and arrays. It must never dump raw JSON into a card: an object-valued claim renders as a nested pair list, or is omitted from the summary line. A rendering layer should not be *capable* of leaking a digest array even if the server regresses.

`BuildHighlightClaims` (`:341–363`) additionally filters protocol keys (`_sd`, `_sd_alg`, …) before taking its first N claims.

---

## 5. Bug B — every claim lies about being locked

`CredentialCardViewModel.DisclosableClaims` is **never populated** by
`CredentialApiService.MapToCardViewModel` (`:306–331`). The card's test —
`DisclosableClaims.Contains(claim.Key)` (`CredentialAcceptCard.razor:49`) — therefore always
returns false, and **every** claim renders with the 🔒 "always disclosed" icon, including the
selectively-disclosable ones.

On the accept card this tells the citizen the exact opposite of the truth about what they would be
compelled to reveal. Selective disclosure is the product promise; the UI currently denies it.

### 5.1 The information does not exist anywhere today

Nothing persists *which* claims are disclosable. `CredentialEntity` stores only `ClaimsJson`, and
the web client's `CredentialListItem` DTO (`CredentialApiService.cs:507–536`) does **not** carry the
raw token — so the client cannot derive it. The same missing fact bites a second time in
`PresentationRequestService.cs:164`, which papers over it with:

```csharp
var disclosable = claims.Keys.ToArray();   // ← declares EVERY claim disclosable
```

Both the padlocks and the presentation-matching path are therefore asserting things they do not know.

### 5.2 Fix — derive it from the raw token, no schema change

The server already has the answer. `CredentialEntity.RawToken` (`CredentialEntity.cs:94`) holds
*"the complete SD-JWT VC raw token (with all disclosures)"*. In an SD-JWT the disclosable claims are
**exactly** those arriving in the `~disclosure~` segments; always-disclosed claims sit directly in
the JWT body. So the set is computable from data already persisted.

- Extend the decoder to return the **disclosable claim-name set** alongside the reconstructed claims
  (it is already parsing the disclosure segments — the names are in hand).
- Expose `disclosableClaims: string[]` on the credential list DTO; map it into
  `CredentialCardViewModel.DisclosableClaims`.
- Point `PresentationRequestService` at the same source instead of `claims.Keys`.

**No new column and no EF migration** — and therefore no Wallet-DB reset on n1. Nested disclosable
paths are reported using the same shape the reconstruction produces, so a disclosable `town` inside
`address` is identifiable rather than being flattened into a top-level name.

---

## 6. The Inbox tab is dead code — delete it

`CredentialApiService.GetPresentationRequestsAsync` (`:119–140`) calls
`GET /api/v1/presentations?wallet={address}`. **That route does not exist.** The Wallet Service
registers only `POST /request`, `GET /{requestId}`, `POST /{requestId}/submit`,
`POST /{requestId}/deny`, `GET /{requestId}/result`
(`src/Services/Sorcha.Wallet.Service/Endpoints/PresentationEndpoints.cs:18–67`) — there is no list
route, and `/api/v1/presentations` cannot match `/{requestId}`. The call 404s and the service
swallows the failure (`return [];`). **The tab renders empty for every user, always.**

Deleting it costs nothing. Removed:
- the Inbox tab (`MyCredentials.razor:135–182`)
- `CredentialApiService.GetPresentationRequestsAsync` and its view-model mapping

### 6.1 Explicitly NOT in scope: verifier-initiated requests in the bell drawer

Surfacing incoming presentation requests in the F118 bell drawer was considered and **rejected for
this change**. It is a feature, not a fix, and it is blocked on three things:

1. **Presentation requests are not durable.** `PresentationRequestService` holds them in a
   `ConcurrentDictionary` inside a **singleton** (`PresentationRequestService.cs:80`,
   `AddSingleton` at `Wallet.Service/Program.cs:50`). They vanish on redeploy, are invisible to a
   second replica, and there is no query-by-wallet method at all. A durable inbox entry pointing at
   a volatile in-memory request would routinely deep-link to a 404.
2. **The inbox has no expiry concept.** `InboxEntry` has no `ExpiresAt`, no supersession, no
   withdraw path, and there is no sweeper. A presentation request lives ~5 minutes; the bell entry
   would sit unread and badge-counted **forever**.
3. **Most requests have no addressee.** The QR / OID4VP flow creates *untargeted* requests
   (`TargetWalletAddress == null`), which by construction cannot produce an inbox entry.

The prerequisites are a durable presentation-request store and `InboxEntry.ExpiresAt` (a schema
migration touching the F118 notifications core). Captured as a follow-up, not undertaken here.

The bell itself is already mounted on **both** citizen surfaces — `/app` web
(`Web.Client/Components/Layout/MainLayout.razor:43,276`) and the PWA
(`Wallet.Pwa/MainLayout.razor:90,142`), the same `InboxPanel` component, no role gate — so a single
server-side writer would light up both when we do take this on.

---

## 7. The Wallet PWA — in scope

The PWA has its **own** credential list (`Pages/Cards.razor`, `Services/SdJwtReader.cs`) sharing no
code with the web page. `SdJwtReader.ReadDisclosedClaims` (`:28–60`) reads **only** the disclosure
segments, never the JWT body — so it will not print `{"_sd":…}` for `address`. But it has the same
flat-only limitation, and `JsonValueToString` (`:87–95`) dumps an object-valued disclosure as raw
JSON. A credential read straight from the raw token **on-device** therefore still renders a nested
object badly.

This matters because **the PWA is the device in the two-device proximity run**. So it is in scope:

- `SdJwtReader` handles **nested** disclosures — reconstructing the parent object rather than
  emitting the children as top-level names.
- `JsonValueToString` never dumps raw JSON; an object value renders structurally.
- Covered by `tests/Sorcha.Wallet.Pwa.Tests/Services/SdJwtReaderTests.cs` with a nested case.

The PWA credential UI has **no padlock icons** (verified) — the lock-icon correctness fix (§5) is
web-only. It has no Inbox tab either; the page restructure (§3) is web-only.

**Still not in scope:** unifying the PWA and web list UIs into one shared component. That is a
worthwhile de-duplication and a separate piece of work.

---

## 8. Tests

There are currently **no bUnit tests for `MyCredentials.razor`, `CredentialAcceptCard`,
`CredentialCard`, or `CredentialCardList`**, and every existing decoder test
(`tests/Sorcha.Wallet.Service.Tests/Services/InboundCredentialDetectorClaimDecoderTests.cs`) uses a
**flat** SD-JWT. That is exactly why this shipped.

| Test | Guards |
|---|---|
| Decoder test with a **nested** SD-JWT (`address` → disclosable `town`/`line1`/`postcode`) | Reconstructed claims contain a real address object and **no `_sd` key at any depth**. The regression guard that would have caught this. |
| Decoder test — disclosable set | A credential with both always-disclosed and selectively-disclosable claims reports **only** the latter as disclosable. Guards the padlock. |
| `StringifyClaimValue` / `BuildHighlightClaims` unit tests | An object-valued claim never renders as raw JSON; protocol keys are filtered. |
| bUnit — page bands | Empty bands are omitted; Expired + Revoked both land in Archive; Archive is collapsed by default. |
| bUnit — Active card | Summary line contains claim **names** and **no claim values**. |
| bUnit — offer card | Claim detail expanded; padlock reflects `DisclosableClaims` (locks are truthful). |
| PWA `SdJwtReaderTests` — nested case | A nested disclosure reconstructs its parent object; an object value never renders as raw JSON on-device. |

---

## 9. Files touched

**Server**
- `src/Services/Sorcha.Wallet.Service/Services/Implementation/InboundCredentialDetector.cs` — call `NestedDisclosure.Reconstruct`; also return the disclosable claim-name set
- `src/Services/Sorcha.Wallet.Service/Endpoints/CredentialEndpoints.cs` — expose `disclosableClaims` on the list DTO
- `src/Services/Sorcha.Wallet.Service/Services/PresentationRequestService.cs` — stop declaring every claim disclosable (`:164`)

**Shared components (`Sorcha.UI.Components.User`)**
- `Services/User/Credentials/CredentialApiService.cs` — carry + map `DisclosableClaims`; filter protocol keys; stop raw-JSON stringification; delete `GetPresentationRequestsAsync`
- `Components/Credentials/CredentialCard.razor` — identity + claim-name summary
- `Components/Credentials/CredentialAcceptCard.razor` — truthful padlocks
- `Components/Credentials/CredentialCardList.razor` — band rendering

**Web page**
- `src/Apps/Sorcha.UI/Sorcha.UI.Web.Client/Pages/MyCredentials.razor` — remove `MudTabs`, three bands, collapsed Archive

**Wallet PWA**
- `src/Apps/Sorcha.Wallet.Pwa/Services/SdJwtReader.cs` — nested-disclosure reconstruction; no raw-JSON stringification

**Tests** — as §8.

---

## 10. Success criteria

- **SC-1** No `_sd` / `_sd_alg` key, and no raw JSON blob, is renderable in any credential card for any credential — nested or flat.
- **SC-2** A selectively-disclosable claim renders as disclosable; an always-disclosed claim renders as locked. The padlock is never a lie.
- **SC-3** The page has no tabs. Expired and Revoked appear together in a collapsed Archive, each carrying its reason.
- **SC-4** The Active list at rest exposes **no claim values**.
- **SC-5** Empty bands are absent, not empty-stated. A holder with one active credential and nothing pending sees exactly one band.
- **SC-6** SC-1 holds on the **PWA** too, for a credential read straight from its raw token on-device — the surface used in the two-device proximity run.
