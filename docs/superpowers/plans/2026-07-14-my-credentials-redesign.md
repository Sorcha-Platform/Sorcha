# My Credentials Redesign Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Make the My Credentials page a single zero-tab list of what the holder holds, and stop the credential cards lying about selective disclosure.

**Architecture:** One shared SD-JWT projection in the Wallet Service becomes the single authority for (a) the reconstructed claim tree and (b) which claims are selectively disclosable. Both are derived from `CredentialEntity.RawToken`, which is already persisted — **no new column, no EF migration**. The web page collapses five tabs into three self-hiding bands. The PWA's own on-device reader gets the same nested-disclosure treatment.

**Tech Stack:** .NET 10 / C# 14, Blazor WASM, MudBlazor, xUnit v3 + FluentAssertions + Moq, bUnit.

## Global Constraints

- License header on every new file: `// SPDX-License-Identifier: MIT` / `// Copyright (c) 2026 Sorcha Contributors` (Razor uses `@* … *@`).
- File-scoped namespaces. Test naming `MethodName_Scenario_ExpectedBehavior`.
- **Never** hard-code `<Version>` in a `.csproj`.
- **No EF migration is permitted in this work.** If you think you need one, you have taken a wrong turn — re-read §5.2 of the spec.
- `dotnet build` before `dotnet test` (stale DLLs cause phantom failures). `dotnet test` takes ONE project.
- Do **not** `git add -A`. Stage explicit paths — the working tree carries unrelated untracked work.
- Branch is already created: `feature/my-credentials-redesign`.

**Spec:** `docs/superpowers/specs/2026-07-14-my-credentials-redesign-design.md`

---

## File Structure

| File | Responsibility |
|---|---|
| **Create** `src/Services/Sorcha.Wallet.Service/Services/Implementation/SdJwtClaimProjection.cs` | The single authority: raw SD-JWT → (reconstructed claims JSON, disclosable claim names). |
| **Create** `tests/Sorcha.Wallet.Service.Tests/Services/SdJwtClaimProjectionTests.cs` | Nested + flat + malformed cases. The regression guard. |
| **Modify** `src/Services/Sorcha.Wallet.Service/Services/Implementation/InboundCredentialDetector.cs` | Delegate to the projection; delete the broken flat decoder. |
| **Modify** `src/Services/Sorcha.Wallet.Service/Endpoints/CredentialEndpoints.cs` | Emit `disclosableClaims` on the list response. |
| **Modify** `src/Services/Sorcha.Wallet.Service/Services/PresentationRequestService.cs` | Stop declaring every claim disclosable. |
| **Modify** `src/Apps/Sorcha.UI/Sorcha.UI.Components.User/Services/User/Credentials/CredentialApiService.cs` | Carry `DisclosableClaims`; build a claim-**name** summary; never stringify raw JSON; delete the dead presentation-request client. |
| **Modify** `src/Apps/Sorcha.UI/Sorcha.UI.Components.User/Models/User/Credentials/CredentialCardViewModel.cs` | Add `DisplayName` + `ClaimSummary`. |
| **Modify** `src/Apps/Sorcha.UI/Sorcha.UI.Components.User/Components/Credentials/CredentialCard.razor` | Identity + one-line claim-name summary. No values. |
| **Modify** `src/Apps/Sorcha.UI/Sorcha.UI.Components.User/Components/Credentials/CredentialAcceptCard.razor` | Truthful padlocks; humanised name. |
| **Modify** `src/Apps/Sorcha.UI/Sorcha.UI.Components.User/Components/Credentials/CredentialCardList.razor` | Drop the duplicate status chips; keep search. |
| **Modify** `src/Apps/Sorcha.UI/Sorcha.UI.Web.Client/Pages/MyCredentials.razor` | Three bands, no `MudTabs`, no Inbox. |
| **Modify** `src/Apps/Sorcha.Wallet.Pwa/Services/SdJwtReader.cs` | Nested disclosures on-device. |
| **Create** `tests/Sorcha.UI.Components.User.Tests/Components/Credentials/CredentialCardTests.cs` | bUnit: no values at rest. |

---

## Task 1: The SD-JWT projection — nested claims + disclosable set

The heart of the change. Everything else consumes this.

**Files:**
- Create: `src/Services/Sorcha.Wallet.Service/Services/Implementation/SdJwtClaimProjection.cs`
- Create: `tests/Sorcha.Wallet.Service.Tests/Services/SdJwtClaimProjectionTests.cs`
- Modify: `src/Services/Sorcha.Wallet.Service/Services/Implementation/InboundCredentialDetector.cs:584-666`

**Interfaces:**
- Consumes: `Sorcha.Cryptography.SdJwt.NestedDisclosure.Reconstruct(Dictionary<string, JsonElement> basePayload, IEnumerable<string> rawDisclosures) → Dictionary<string, object>`
- Produces:
  - `public static SdJwtProjection SdJwtClaimProjection.Project(string? rawToken)`
  - `public sealed record SdJwtProjection(string ClaimsJson, IReadOnlyList<string> DisclosableClaims)`
  - `public static readonly SdJwtProjection SdJwtProjection.Empty` — `("{}", [])`

**The rule that decides a padlock.** A top-level claim is **always disclosed** iff it came directly from the JWT body *and* nothing in its subtree carries an `_sd` array. Everything else in the reconstructed tree is **disclosable** — including a parent object like `address` whose children are individually disclosable, because the holder does control what of it is revealed.

- [ ] **Step 1: Write the failing test**

Create `tests/Sorcha.Wallet.Service.Tests/Services/SdJwtClaimProjectionTests.cs`:

```csharp
// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using FluentAssertions;
using Sorcha.Wallet.Service.Services.Implementation;

namespace Sorcha.Wallet.Service.Tests.Services;

/// <summary>
/// Guards the defect found live on n1 (2026-07-14): a NESTED selective disclosure
/// left <c>address</c> rendering as its raw <c>{"_sd":[…]}</c> digest array on the
/// credential card, while its children leaked out as flat top-level claims.
/// Every pre-existing decoder test used a FLAT SD-JWT, which is why it shipped.
/// </summary>
public class SdJwtClaimProjectionTests
{
    // --- SD-JWT construction helpers (RFC 9901 §4.2.1) ---

    private static string B64Url(byte[] bytes) =>
        Convert.ToBase64String(bytes).TrimEnd('=').Replace('+', '-').Replace('/', '_');

    /// <summary>A disclosure is base64url(JSON([salt, name, value])).</summary>
    private static string Disclosure(string salt, string name, object value)
    {
        var json = JsonSerializer.Serialize(new object[] { salt, name, value });
        return B64Url(Encoding.UTF8.GetBytes(json));
    }

    /// <summary>The digest that appears in an _sd array is base64url(SHA-256(ascii(disclosure))).</summary>
    private static string Digest(string disclosure) =>
        B64Url(SHA256.HashData(Encoding.ASCII.GetBytes(disclosure)));

    private static string Token(object body, params string[] disclosures)
    {
        var header = B64Url(Encoding.UTF8.GetBytes("""{"alg":"ES256","typ":"dc+sd-jwt"}"""));
        var payload = B64Url(JsonSerializer.SerializeToUtf8Bytes(body));
        var jwt = $"{header}.{payload}.c2ln";   // signature is never verified on this path
        return disclosures.Length == 0 ? jwt : jwt + "~" + string.Join("~", disclosures);
    }

    /// <summary>
    /// The n1 shape: address is an OBJECT whose town/line1 are individually
    /// disclosable, so the body carries address:{_sd:[…]} and the disclosures
    /// name the CHILDREN.
    /// </summary>
    private static string NestedToken()
    {
        var town = Disclosure("s1", "town", "Edinburgh");
        var line1 = Disclosure("s2", "line1", "6/2 Warrender Park Terrace");
        var body = new Dictionary<string, object>
        {
            ["vct"] = "https://sorcha.dev/vc/assured-identity/v1",
            ["iss"] = "did:sorcha:org:ws11q",
            ["email"] = "stuart@stuartfraser.net",       // always disclosed — in the body, no _sd
            ["address"] = new Dictionary<string, object>
            {
                ["_sd"] = new[] { Digest(town), Digest(line1) }
            }
        };
        return Token(body, town, line1);
    }

    [Fact]
    public void Project_NestedDisclosure_ReconstructsAddressObject()
    {
        var result = SdJwtClaimProjection.Project(NestedToken());

        using var doc = JsonDocument.Parse(result.ClaimsJson);
        var address = doc.RootElement.GetProperty("address");

        address.ValueKind.Should().Be(JsonValueKind.Object);
        address.GetProperty("town").GetString().Should().Be("Edinburgh");
        address.GetProperty("line1").GetString().Should().Be("6/2 Warrender Park Terrace");
    }

    [Fact]
    public void Project_NestedDisclosure_LeaksNoSdDigestsAtAnyDepth()
    {
        var result = SdJwtClaimProjection.Project(NestedToken());

        // The bug in one assertion: no _sd / _sd_alg key may survive, at any depth.
        result.ClaimsJson.Should().NotContain("_sd");
    }

    [Fact]
    public void Project_NestedDisclosure_DoesNotFlattenChildrenToTopLevel()
    {
        var result = SdJwtClaimProjection.Project(NestedToken());

        using var doc = JsonDocument.Parse(result.ClaimsJson);
        doc.RootElement.TryGetProperty("town", out _).Should().BeFalse(
            "town belongs inside address, not beside it");
        doc.RootElement.TryGetProperty("line1", out _).Should().BeFalse();
    }

    [Fact]
    public void Project_NestedDisclosure_MarksOnlySelectivelyDisclosableClaims()
    {
        var result = SdJwtClaimProjection.Project(NestedToken());

        // address carries an _sd → the holder controls what of it is revealed.
        result.DisclosableClaims.Should().Contain("address");
        // email sits in the body with no _sd → it always travels.
        result.DisclosableClaims.Should().NotContain("email");
    }

    [Fact]
    public void Project_StripsProtocolFields()
    {
        var result = SdJwtClaimProjection.Project(NestedToken());

        using var doc = JsonDocument.Parse(result.ClaimsJson);
        foreach (var field in new[] { "iss", "vct", "sub", "iat", "exp", "cnf" })
            doc.RootElement.TryGetProperty(field, out _).Should().BeFalse($"{field} is a protocol field, not a claim");
    }

    [Fact]
    public void Project_FlatDisclosure_MarksClaimDisclosable()
    {
        var name = Disclosure("s1", "name", "Jane Doe");
        var body = new Dictionary<string, object>
        {
            ["vct"] = "https://sorcha.dev/vc/x/v1",
            ["licenceNumber"] = "BI-2026-0042",           // always disclosed
            ["_sd"] = new[] { Digest(name) }
        };
        var result = SdJwtClaimProjection.Project(Token(body, name));

        using var doc = JsonDocument.Parse(result.ClaimsJson);
        doc.RootElement.GetProperty("name").GetString().Should().Be("Jane Doe");
        doc.RootElement.GetProperty("licenceNumber").GetString().Should().Be("BI-2026-0042");

        result.DisclosableClaims.Should().BeEquivalentTo(["name"]);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("not-a-jwt")]
    public void Project_MalformedInput_ReturnsEmptyProjection(string? input)
    {
        var result = SdJwtClaimProjection.Project(input);

        result.ClaimsJson.Should().Be("{}");
        result.DisclosableClaims.Should().BeEmpty();
    }

    [Fact]
    public void Project_BadDisclosure_KeepsTheRest()
    {
        var good = Disclosure("s1", "name", "Jane Doe");
        var body = new Dictionary<string, object>
        {
            ["vct"] = "https://sorcha.dev/vc/x/v1",
            ["_sd"] = new[] { Digest(good) }
        };
        var token = Token(body, good) + "~!!!not-base64!!!";

        var result = SdJwtClaimProjection.Project(token);

        using var doc = JsonDocument.Parse(result.ClaimsJson);
        doc.RootElement.GetProperty("name").GetString().Should().Be("Jane Doe");
    }
}
```

- [ ] **Step 2: Run the test — verify it fails**

Run: `dotnet test tests/Sorcha.Wallet.Service.Tests/Sorcha.Wallet.Service.Tests.csproj --filter "FullyQualifiedName~SdJwtClaimProjectionTests"`
Expected: **FAIL** — `SdJwtClaimProjection` does not exist (compile error `CS0103`).

- [ ] **Step 3: Write the implementation**

Create `src/Services/Sorcha.Wallet.Service/Services/Implementation/SdJwtClaimProjection.cs`:

```csharp
// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

using System.Text.Json;
using Sorcha.Cryptography.SdJwt;

namespace Sorcha.Wallet.Service.Services.Implementation;

/// <summary>
/// The projection of an SD-JWT VC that the holder UI consumes: the reconstructed
/// claim tree, plus which of its top-level claims the holder actually controls.
/// </summary>
/// <param name="ClaimsJson">
/// Reconstructed claims as a JSON object. Nested disclosures land at their correct
/// depth (<c>/address/town</c> inside <c>address</c>, not beside it) and no
/// <c>_sd</c> / <c>_sd_alg</c> digest array survives at any depth.
/// </param>
/// <param name="DisclosableClaims">
/// Top-level claim names the holder can choose to withhold when presenting.
/// Everything else in <paramref name="ClaimsJson"/> always travels.
/// </param>
public sealed record SdJwtProjection(string ClaimsJson, IReadOnlyList<string> DisclosableClaims)
{
    /// <summary>A malformed or absent token projects to nothing — never to a throw.</summary>
    public static readonly SdJwtProjection Empty = new("{}", []);
}

/// <summary>
/// Decodes an SD-JWT VC into the shape the credential cards render.
///
/// Signature verification is deliberately NOT performed here: this projection is
/// for *display* on the pending-offer card, before the holder has chosen to trust
/// the issuer. Verification runs on accept.
///
/// This is the single authority for both the claim tree and the disclosable set —
/// the ingest path and the list endpoint MUST both come through it. The previous
/// hand-rolled decoder resolved only TOP-LEVEL <c>_sd</c>, so a nested disclosure
/// left <c>address</c> rendering as a raw digest array on a citizen's phone while
/// its children leaked out as flat top-level claims.
/// </summary>
public static class SdJwtClaimProjection
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    /// <summary>
    /// SD-JWT / JWT envelope fields. Not credential claims, so they never reach a card.
    /// <see cref="NestedDisclosure.Reconstruct"/> already drops iss/sub/iat/exp/cnf/_sd/_sd_alg;
    /// these are the ones it leaves behind.
    /// </summary>
    private static readonly HashSet<string> ProtocolFields = new(StringComparer.Ordinal)
    {
        "iss", "sub", "iat", "exp", "nbf", "jti", "aud", "vct",
        "_sd", "_sd_alg", "cnf", "credentialStatus", "type", "status"
    };

    /// <summary>
    /// Projects a raw compact SD-JWT. Never throws — a malformed token yields
    /// <see cref="SdJwtProjection.Empty"/>, because one bad credential must not
    /// take down a holder's whole credential list.
    /// </summary>
    public static SdJwtProjection Project(string? rawToken)
    {
        if (string.IsNullOrWhiteSpace(rawToken)) return SdJwtProjection.Empty;

        try
        {
            // <header>.<body>.<sig>~<disclosure>~…~[<kb-jwt>]
            var segments = rawToken.Split('~');
            var jwtParts = segments[0].Split('.');
            if (jwtParts.Length < 2) return SdJwtProjection.Empty;

            using var bodyDoc = JsonDocument.Parse(Base64Url.Decode(jwtParts[1]));
            if (bodyDoc.RootElement.ValueKind != JsonValueKind.Object) return SdJwtProjection.Empty;

            // Clone: the JsonElements must outlive the JsonDocument's using scope.
            var basePayload = bodyDoc.RootElement
                .EnumerateObject()
                .ToDictionary(p => p.Name, p => p.Value.Clone(), StringComparer.Ordinal);

            // The optional trailing KB-JWT has dots; a disclosure never does.
            var disclosures = segments
                .Skip(1)
                .Where(s => !string.IsNullOrEmpty(s) && !s.Contains('.'))
                .ToArray();

            // Resolve nested _sd digests at their correct depth, stripping _sd/_sd_alg.
            var reconstructed = NestedDisclosure.Reconstruct(basePayload, disclosures);

            // Reconstruct keeps vct/nbf/jti/aud/credentialStatus/type — drop them.
            var claims = reconstructed
                .Where(kvp => !ProtocolFields.Contains(kvp.Key))
                .ToDictionary(kvp => kvp.Key, kvp => kvp.Value, StringComparer.Ordinal);

            var disclosable = ResolveDisclosableClaims(basePayload, claims.Keys);

            return new SdJwtProjection(JsonSerializer.Serialize(claims, JsonOptions), disclosable);
        }
        catch
        {
            return SdJwtProjection.Empty;
        }
    }

    /// <summary>
    /// A top-level claim is ALWAYS disclosed iff it appears verbatim in the JWT body
    /// and nothing in its subtree carries an <c>_sd</c> array. Everything else in the
    /// reconstructed tree is disclosable — including a parent object such as
    /// <c>address</c> whose children are individually disclosable, because the holder
    /// does control what of it is revealed.
    /// </summary>
    private static List<string> ResolveDisclosableClaims(
        IReadOnlyDictionary<string, JsonElement> basePayload,
        IEnumerable<string> reconstructedKeys)
    {
        var alwaysDisclosed = new HashSet<string>(StringComparer.Ordinal);
        foreach (var (key, value) in basePayload)
        {
            if (ProtocolFields.Contains(key)) continue;
            if (!ContainsSd(value)) alwaysDisclosed.Add(key);
        }

        return reconstructedKeys
            .Where(k => !alwaysDisclosed.Contains(k))
            .OrderBy(k => k, StringComparer.Ordinal)
            .ToList();
    }

    /// <summary>True if an <c>_sd</c> array appears anywhere in this subtree.</summary>
    private static bool ContainsSd(JsonElement element) => element.ValueKind switch
    {
        JsonValueKind.Object => element.EnumerateObject()
            .Any(p => p.Name == "_sd" || ContainsSd(p.Value)),
        JsonValueKind.Array => element.EnumerateArray().Any(ContainsSd),
        _ => false
    };

    /// <summary>
    /// Base64url with tolerant padding. The write path emits base64url (RFC 4648 §5);
    /// older payloads used raw base64, so both are accepted.
    /// </summary>
    private static class Base64Url
    {
        public static byte[] Decode(string raw)
        {
            try
            {
                var padded = raw.Replace('-', '+').Replace('_', '/');
                padded += (padded.Length % 4) switch { 2 => "==", 3 => "=", _ => "" };
                return Convert.FromBase64String(padded);
            }
            catch
            {
                return Convert.FromBase64String(raw);
            }
        }
    }
}
```

- [ ] **Step 4: Run the test — verify it passes**

Run: `dotnet build src/Services/Sorcha.Wallet.Service/Sorcha.Wallet.Service.csproj` then
`dotnet test tests/Sorcha.Wallet.Service.Tests/Sorcha.Wallet.Service.Tests.csproj --filter "FullyQualifiedName~SdJwtClaimProjectionTests"`
Expected: **PASS**, 9 tests.

If `Sorcha.Cryptography` is not referenced by `Sorcha.Wallet.Service`, add the ProjectReference — but check first, it almost certainly already is (the service does crypto).

- [ ] **Step 5: Rewire the ingest path onto the projection**

In `InboundCredentialDetector.cs`, replace the body of the extract call at **line 538**:

```csharp
        var claimsJson = ExtractDisclosedClaimsJson(rawToken!) ?? "{}";
```

with:

```csharp
        var claimsJson = SdJwtClaimProjection.Project(rawToken).ClaimsJson;
```

Then **delete** the now-dead `ExtractDisclosedClaimsJson` (lines 584-656) and `JsonElementToValue` (lines 658-666) — the projection owns this now. Leave `DecodePayloadData` alone; it has other callers.

- [ ] **Step 6: Repoint the old decoder tests at the projection**

`tests/Sorcha.Wallet.Service.Tests/Services/InboundCredentialDetectorClaimDecoderTests.cs` calls the method you just deleted. Its four tests use a **real** Forestry DPP token and are valuable — keep them, but change every call site from:

```csharp
        var json = InboundCredentialDetector.ExtractDisclosedClaimsJson(ForestryDppRawToken);
```

to:

```csharp
        var json = SdJwtClaimProjection.Project(ForestryDppRawToken).ClaimsJson;
```

(and the malformed-input test's `Project(input!).ClaimsJson`, which now returns `"{}"` rather than `null` — assert `Should().Be("{}")`). Add `using Sorcha.Wallet.Service.Services.Implementation;` if absent.

- [ ] **Step 7: Run the whole Wallet Service test project**

Run: `dotnet build && dotnet test tests/Sorcha.Wallet.Service.Tests/Sorcha.Wallet.Service.Tests.csproj`
Expected: **PASS**. The four repointed Forestry tests must still pass — that is the proof the flat path did not regress.

- [ ] **Step 8: Commit**

```bash
git add src/Services/Sorcha.Wallet.Service/Services/Implementation/SdJwtClaimProjection.cs \
        src/Services/Sorcha.Wallet.Service/Services/Implementation/InboundCredentialDetector.cs \
        tests/Sorcha.Wallet.Service.Tests/Services/SdJwtClaimProjectionTests.cs \
        tests/Sorcha.Wallet.Service.Tests/Services/InboundCredentialDetectorClaimDecoderTests.cs
git commit -m "fix: resolve nested SD-JWT disclosures instead of leaking _sd digests

The decoder stripped _sd only at the top level, so a nested disclosure left
address rendering as a raw digest array on a citizen's card while its children
leaked out as flat top-level claims. Route both the claim tree and the
disclosable set through one projection over NestedDisclosure.Reconstruct.

Every pre-existing decoder test used a flat SD-JWT, which is why this shipped.

Co-Authored-By: Claude Opus 4.8 (1M context) <noreply@anthropic.com>"
```

---

## Task 2: Serve the disclosable set

**Files:**
- Modify: `src/Services/Sorcha.Wallet.Service/Endpoints/CredentialEndpoints.cs:230-254`
- Modify: `src/Services/Sorcha.Wallet.Service/Services/PresentationRequestService.cs:164`

**Interfaces:**
- Consumes: `SdJwtClaimProjection.Project(string?) → SdJwtProjection` (Task 1)
- Produces: list-response field `disclosableClaims: string[]` on each credential.

**Why here and not at ingest:** the answer is derivable from `CredentialEntity.RawToken`, which is already stored. Persisting it would mean a new column and an EF migration — and an n1 Wallet-DB reset. Derive on read.

- [ ] **Step 1: Add the field to the list response**

In `CredentialEndpoints.cs`, replace the projection at line 230:

```csharp
        var response = filtered.Select(c => new
        {
            c.Id,
            c.Type,
            c.IssuerDid,
            c.SubjectDid,
            c.IssuedAt,
            c.ExpiresAt,
            c.Status,
            c.IssuerOrgName,
            c.IssuanceBlueprintId,
            c.IssuanceTxId,
            c.IssuanceInstanceId,
            c.IssuanceActionId,
            c.ClaimActionId,
            c.RegisterId,
            // Holders need to see what's in a credential before they Accept/Decline
            // it on the Pending tab — see CredentialAcceptCard. Without these
            // fields the card renders "0 claims" against a credential that does
            // have claims, which actively misleads the holder. Payload growth is
            // bounded — claims are typically <2KB and display config is smaller.
            c.ClaimsJson,
            c.DisplayConfigJson,
            c.UsagePolicy,
        });
```

with:

```csharp
        var response = filtered.Select(c => new
        {
            c.Id,
            c.Type,
            c.IssuerDid,
            c.SubjectDid,
            c.IssuedAt,
            c.ExpiresAt,
            c.Status,
            c.IssuerOrgName,
            c.IssuanceBlueprintId,
            c.IssuanceTxId,
            c.IssuanceInstanceId,
            c.IssuanceActionId,
            c.ClaimActionId,
            c.RegisterId,
            // Holders need to see what's in a credential before they Accept/Decline
            // it — see CredentialAcceptCard. Without these fields the card renders
            // "0 claims" against a credential that does have claims, which actively
            // misleads the holder. Payload growth is bounded — claims are typically
            // <2KB and display config is smaller.
            c.ClaimsJson,
            c.DisplayConfigJson,
            c.UsagePolicy,
            // Which claims the holder can withhold when presenting. Derived from the
            // stored raw token rather than persisted, so no column and no migration.
            // Without it every claim renders with an "always disclosed" padlock —
            // the exact opposite of the truth about what the holder must reveal.
            DisclosableClaims = SdJwtClaimProjection.Project(c.RawToken).DisclosableClaims,
        });
```

Add `using Sorcha.Wallet.Service.Services.Implementation;` to the file's usings if not already present.

- [ ] **Step 2: Stop PresentationRequestService declaring every claim disclosable**

`PresentationRequestService.cs:161-175` currently reads:

```csharp
            var claims = ParseClaims(cred.ClaimsJson);
            var disclosable = claims.Keys.ToArray();
```

Replace the second line with:

```csharp
            // Not claims.Keys — that declared EVERY claim disclosable, including the
            // ones baked into the JWT body that always travel. The raw token knows.
            var disclosable = SdJwtClaimProjection.Project(cred.RawToken).DisclosableClaims.ToArray();
```

Add the `using Sorcha.Wallet.Service.Services.Implementation;` if absent.

- [ ] **Step 3: Build and run the Wallet Service tests**

Run: `dotnet build && dotnet test tests/Sorcha.Wallet.Service.Tests/Sorcha.Wallet.Service.Tests.csproj`
Expected: **PASS**. If a `PresentationRequestService` test asserted that all claims are disclosable, it was asserting the bug — update it to expect only the genuinely-disclosable ones, and say so in the commit.

- [ ] **Step 4: Commit**

```bash
git add src/Services/Sorcha.Wallet.Service/Endpoints/CredentialEndpoints.cs \
        src/Services/Sorcha.Wallet.Service/Services/PresentationRequestService.cs
git commit -m "fix: serve the real disclosable-claim set

Derived from the stored RawToken — no column, no migration. Also stops
PresentationRequestService declaring every claim disclosable via claims.Keys.

Co-Authored-By: Claude Opus 4.8 (1M context) <noreply@anthropic.com>"
```

---

## Task 3: Web client — carry the truth, summarise by name, delete the dead client

**Files:**
- Modify: `src/Apps/Sorcha.UI/Sorcha.UI.Components.User/Models/User/Credentials/CredentialCardViewModel.cs`
- Modify: `src/Apps/Sorcha.UI/Sorcha.UI.Components.User/Services/User/Credentials/CredentialApiService.cs`
- Modify: `src/Apps/Sorcha.UI/Sorcha.UI.Components.User/Services/User/Credentials/ICredentialApiService.cs`
- Test: `tests/Sorcha.UI.Core.Tests/Credentials/CredentialApiServiceTests.cs`

**Interfaces:**
- Consumes: list-response `disclosableClaims: string[]` (Task 2)
- Produces:
  - `CredentialCardViewModel.DisplayName` — humanised type (`AssuredIdentityCredential` → `Assured Identity`)
  - `CredentialCardViewModel.ClaimSummary` — one line of claim **names**, e.g. `"Address, email"`
  - `CredentialCardViewModel.DisclosableClaims` — now actually populated

- [ ] **Step 1: Write the failing tests**

Append to `tests/Sorcha.UI.Core.Tests/Credentials/CredentialApiServiceTests.cs` (match the file's existing fixture/mocking style for the HTTP handler; the assertions are what matter):

```csharp
    [Fact]
    public async Task GetCredentialsAsync_PopulatesDisclosableClaimsFromResponse()
    {
        // The card's padlock is only meaningful if this survives the wire.
        var json = """
        [{
          "id": "urn:credential:1", "type": "AssuredIdentityCredential",
          "issuerDid": "did:sorcha:org:ws11q", "subjectDid": "did:sorcha:w:ws11q",
          "status": "Active", "issuedAt": "2026-07-01T00:00:00Z",
          "claimsJson": "{\"email\":\"a@b.c\",\"address\":{\"town\":\"Edinburgh\"}}",
          "disclosableClaims": ["address"]
        }]
        """;
        var service = CreateServiceReturning(json);

        var result = await service.GetCredentialsAsync("ws11q");

        result.Should().ContainSingle();
        result[0].DisclosableClaims.Should().BeEquivalentTo(["address"]);
    }

    [Fact]
    public async Task GetCredentialsAsync_ObjectClaim_NeverRendersAsRawJson()
    {
        // The n1 defect, guarded at the rendering layer: even if the server
        // regressed and sent a digest array, no card may print raw JSON.
        var json = """
        [{
          "id": "urn:credential:1", "type": "AssuredIdentityCredential",
          "issuerDid": "did:sorcha:org:ws11q", "subjectDid": "did:sorcha:w:ws11q",
          "status": "Active", "issuedAt": "2026-07-01T00:00:00Z",
          "claimsJson": "{\"address\":{\"_sd\":[\"zSH_kfTeW2Mlc\"]}}",
          "disclosableClaims": []
        }]
        """;
        var service = CreateServiceReturning(json);

        var result = await service.GetCredentialsAsync("ws11q");

        foreach (var value in result[0].HighlightClaims.Values)
        {
            value.Should().NotContain("_sd");
            value.Should().NotStartWith("{");
        }
    }

    [Fact]
    public async Task GetCredentialsAsync_BuildsClaimSummaryOfNamesNotValues()
    {
        var json = """
        [{
          "id": "urn:credential:1", "type": "AssuredIdentityCredential",
          "issuerDid": "did:sorcha:org:ws11q", "subjectDid": "did:sorcha:w:ws11q",
          "status": "Active", "issuedAt": "2026-07-01T00:00:00Z",
          "claimsJson": "{\"email\":\"stuart@stuartfraser.net\",\"dateOfBirth\":\"1980-01-01\"}",
          "disclosableClaims": ["email"]
        }]
        """;
        var service = CreateServiceReturning(json);

        var result = await service.GetCredentialsAsync("ws11q");

        result[0].ClaimSummary.Should().Contain("Email");
        result[0].ClaimSummary.Should().Contain("Date of birth");
        result[0].ClaimSummary.Should().NotContain("stuart@stuartfraser.net", "a list must never print claim values");
        result[0].ClaimSummary.Should().NotContain("1980");
    }

    [Fact]
    public async Task GetCredentialsAsync_HumanisesTheCredentialType()
    {
        var json = """
        [{
          "id": "urn:credential:1", "type": "AssuredIdentityCredential",
          "issuerDid": "did:sorcha:org:ws11q", "subjectDid": "did:sorcha:w:ws11q",
          "status": "Active", "issuedAt": "2026-07-01T00:00:00Z",
          "claimsJson": "{}", "disclosableClaims": []
        }]
        """;
        var service = CreateServiceReturning(json);

        var result = await service.GetCredentialsAsync("ws11q");

        result[0].DisplayName.Should().Be("Assured Identity");
    }
```

If the test class has no `CreateServiceReturning(string json)` helper, add one that builds a `CredentialApiService` over a stubbed `HttpMessageHandler` returning `json` with 200 — follow whatever mocking the file already uses.

- [ ] **Step 2: Run — verify it fails**

Run: `dotnet test tests/Sorcha.UI.Core.Tests/Sorcha.UI.Core.Tests.csproj --filter "FullyQualifiedName~CredentialApiServiceTests"`
Expected: **FAIL** — `DisplayName` / `ClaimSummary` do not exist.

- [ ] **Step 3: Add the view-model fields**

In `CredentialCardViewModel.cs`, after the `Type` property (line 12) add:

```csharp
    /// <summary>
    /// The credential type as a human reads it — "Assured Identity", not
    /// "AssuredIdentityCredential". Falls back to <see cref="Type"/>.
    /// </summary>
    public string DisplayName { get; set; } = string.Empty;

    /// <summary>
    /// One line naming what is inside, e.g. "Name, date of birth, address".
    /// Claim NAMES only — a list view must never print claim values, or a
    /// holder's address is legible to anyone glancing at their screen.
    /// </summary>
    public string ClaimSummary { get; set; } = string.Empty;
```

- [ ] **Step 4: Carry the field on the wire DTO**

In `CredentialApiService.cs`, in `private class CredentialListItem` (line 507), after `UsagePolicy`:

```csharp
        /// <summary>Claims the holder may withhold when presenting. Server-derived from the raw token.</summary>
        public List<string>? DisclosableClaims { get; set; }
```

- [ ] **Step 5: Map it, humanise, summarise**

In `MapToCardViewModel` (line 301), inside the object initialiser after `HighlightClaims = BuildHighlightClaims(claims, displayConfig),` add:

```csharp
            DisclosableClaims = item.DisclosableClaims ?? [],
            DisplayName = Humanise(item.Type),
            ClaimSummary = BuildClaimSummary(claims),
```

Then add these three helpers to the class:

```csharp
    /// <summary>
    /// "AssuredIdentityCredential" → "Assured Identity". Splits PascalCase and drops
    /// the redundant "Credential" suffix — every card on the page is a credential.
    /// </summary>
    private static string Humanise(string? type)
    {
        if (string.IsNullOrWhiteSpace(type)) return string.Empty;

        var trimmed = type.EndsWith("Credential", StringComparison.Ordinal) && type.Length > "Credential".Length
            ? type[..^"Credential".Length]
            : type;

        var spaced = System.Text.RegularExpressions.Regex.Replace(
            trimmed, "(?<=[a-z0-9])(?=[A-Z])", " ");

        return spaced.Length == 0 ? type : char.ToUpperInvariant(spaced[0]) + spaced[1..];
    }

    /// <summary>
    /// A single line naming what the credential holds — names only, never values.
    /// Caps at four names so a fat credential cannot blow the card open.
    /// </summary>
    private static string BuildClaimSummary(IReadOnlyDictionary<string, object?> claims)
    {
        var names = claims.Keys
            .Where(k => !k.StartsWith('_'))
            .Select(HumaniseClaimName)
            .ToList();

        if (names.Count == 0) return string.Empty;
        if (names.Count <= 4) return string.Join(", ", names);

        return string.Join(", ", names.Take(4)) + $" and {names.Count - 4} more";
    }

    /// <summary>"dateOfBirth" → "Date of birth". Sentence case, not Title Case.</summary>
    private static string HumaniseClaimName(string key)
    {
        var spaced = System.Text.RegularExpressions.Regex.Replace(
            key, "(?<=[a-z0-9])(?=[A-Z])", " ").ToLowerInvariant();
        return spaced.Length == 0 ? key : char.ToUpperInvariant(spaced[0]) + spaced[1..];
    }
```

- [ ] **Step 6: Stop the raw-JSON leak at the rendering layer**

Replace `StringifyClaimValue` (line 417):

```csharp
    /// <summary>
    /// Renders a claim value for display. An object or array NEVER renders as raw
    /// JSON — that is how an unresolved SD-JWT digest array reached a citizen's
    /// card on n1. The server should not send one, and this layer must not be
    /// capable of printing it if it does.
    /// </summary>
    private static string StringifyClaimValue(object? value) => value switch
    {
        null => string.Empty,
        string s => s,
        JsonElement el => el.ValueKind switch
        {
            JsonValueKind.String => el.GetString() ?? string.Empty,
            JsonValueKind.Number => el.ToString(),
            JsonValueKind.True or JsonValueKind.False => el.GetBoolean().ToString(),
            JsonValueKind.Null => string.Empty,
            JsonValueKind.Object => SummariseObject(el),
            JsonValueKind.Array => $"{el.GetArrayLength()} item{(el.GetArrayLength() == 1 ? "" : "s")}",
            _ => string.Empty
        },
        _ => value.ToString() ?? string.Empty
    };

    /// <summary>
    /// A nested object renders as its field names, not its JSON. Protocol keys are
    /// dropped so a stray digest array degrades to an empty string, never a blob.
    /// </summary>
    private static string SummariseObject(JsonElement el)
    {
        var fields = el.EnumerateObject()
            .Where(p => !p.Name.StartsWith('_'))
            .Select(p => p.Name)
            .ToList();

        return fields.Count == 0 ? string.Empty : string.Join(", ", fields);
    }
```

And filter protocol keys out of the fallback in `BuildHighlightClaims` (line 359):

```csharp
        return claims
            .Where(kvp => kvp.Value is not null && !kvp.Key.StartsWith('_'))
            .Take(6)
            .ToDictionary(kvp => kvp.Key, kvp => StringifyClaimValue(kvp.Value));
```

- [ ] **Step 7: Delete the dead presentation-request client**

`GetPresentationRequestsAsync` calls `GET /api/v1/presentations?wallet={address}` — **a route that does not exist**. The 404 is swallowed and it has returned an empty list for every user since it was written.

Delete from `CredentialApiService.cs`: `GetPresentationRequestsAsync` (~line 119-140) and `MapToPresentationViewModel` (~line 279) **if it has no other caller** (check: `GetPresentationRequestDetailAsync` may share it — if so, keep the mapper and delete only the list method). Delete the matching member from `ICredentialApiService`.

- [ ] **Step 8: Run — verify it passes**

Run: `dotnet build && dotnet test tests/Sorcha.UI.Core.Tests/Sorcha.UI.Core.Tests.csproj`
Expected: **PASS**. The build will fail in `MyCredentials.razor` (it still calls the deleted method) — that is expected and Task 5 fixes it. To keep the tree green, do Step 7 **after** Task 5, or accept a red build across the two commits. **Preferred: move Step 7 into Task 5** and commit this task without it.

- [ ] **Step 9: Commit**

```bash
git add src/Apps/Sorcha.UI/Sorcha.UI.Components.User/Models/User/Credentials/CredentialCardViewModel.cs \
        src/Apps/Sorcha.UI/Sorcha.UI.Components.User/Services/User/Credentials/CredentialApiService.cs \
        tests/Sorcha.UI.Core.Tests/Credentials/CredentialApiServiceTests.cs
git commit -m "feat: carry the disclosable set; summarise claims by name, not value

- DisclosableClaims now actually populated (it never was — every padlock lied)
- DisplayName humanises the type; ClaimSummary names claims without printing them
- An object claim can no longer render as raw JSON

Co-Authored-By: Claude Opus 4.8 (1M context) <noreply@anthropic.com>"
```

---

## Task 4: The cards

**Files:**
- Modify: `src/Apps/Sorcha.UI/Sorcha.UI.Components.User/Components/Credentials/CredentialCard.razor`
- Modify: `src/Apps/Sorcha.UI/Sorcha.UI.Components.User/Components/Credentials/CredentialAcceptCard.razor`
- Modify: `src/Apps/Sorcha.UI/Sorcha.UI.Components.User/Components/Credentials/CredentialCardList.razor`
- Create: `tests/Sorcha.UI.Components.User.Tests/Components/Credentials/CredentialCardTests.cs`

**Interfaces:**
- Consumes: `CredentialCardViewModel.{DisplayName, ClaimSummary, DisclosableClaims}` (Task 3)

- [ ] **Step 1: Write the failing bUnit test**

Create `tests/Sorcha.UI.Components.User.Tests/Components/Credentials/CredentialCardTests.cs`:

```csharp
// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

using Bunit;
using FluentAssertions;
using MudBlazor.Services;
using Sorcha.UI.Core.Components.Credentials;
using Sorcha.UI.Core.Models.Credentials;

namespace Sorcha.UI.Components.User.Tests.Components.Credentials;

public class CredentialCardTests : TestContext
{
    public CredentialCardTests()
    {
        Services.AddMudServices();
        JSInterop.Mode = JSRuntimeMode.Loose;
    }

    private static CredentialCardViewModel Card() => new()
    {
        CredentialId = "urn:credential:1",
        Type = "AssuredIdentityCredential",
        DisplayName = "Assured Identity",
        IssuerName = "Acme Identity Assurance Services",
        Status = CredentialStatus.Active,
        IssuedAt = DateTimeOffset.Parse("2026-07-01T00:00:00Z"),
        ClaimSummary = "Address, email",
        HighlightClaims = new() { ["email"] = "stuart@stuartfraser.net" },
        DisclosableClaims = ["address"]
    };

    [Fact]
    public void CredentialCard_AtRest_ShowsNoClaimValues()
    {
        var cut = RenderComponent<CredentialCard>(p => p.Add(c => c.Credential, Card()));

        // The whole point: a list must not print the holder's personal data.
        cut.Markup.Should().NotContain("stuart@stuartfraser.net");
    }

    [Fact]
    public void CredentialCard_AtRest_ShowsTheClaimNameSummary()
    {
        var cut = RenderComponent<CredentialCard>(p => p.Add(c => c.Credential, Card()));

        cut.Markup.Should().Contain("Address, email");
    }

    [Fact]
    public void CredentialCard_UsesTheHumanisedName()
    {
        var cut = RenderComponent<CredentialCard>(p => p.Add(c => c.Credential, Card()));

        cut.Markup.Should().Contain("Assured Identity");
        cut.Markup.Should().NotContain("AssuredIdentityCredential");
    }

    [Fact]
    public void CredentialAcceptCard_LocksReflectDisclosability()
    {
        var vm = Card();
        vm.HighlightClaims = new() { ["address"] = "Edinburgh", ["email"] = "a@b.c" };
        vm.DisclosableClaims = ["address"];   // email always travels

        var cut = RenderComponent<CredentialAcceptCard>(p => p.Add(c => c.Credential, vm));

        // One open padlock (address, holder-controlled) and one closed (email, always disclosed).
        cut.Markup.Should().Contain("🔓");
        cut.Markup.Should().Contain("🔒");
    }
}
```

- [ ] **Step 2: Run — verify it fails**

Run: `dotnet test tests/Sorcha.UI.Components.User.Tests/Sorcha.UI.Components.User.Tests.csproj --filter "FullyQualifiedName~CredentialCardTests"`
Expected: **FAIL** — the card still prints values.

If the test project has no bUnit reference, add `bunit` to `tests/Sorcha.UI.Components.User.Tests/Sorcha.UI.Components.User.Tests.csproj` (the Onboarding/Presentation component tests already there will show the exact package + version to match — do not introduce a new one).

- [ ] **Step 3: Rewrite the claims block on `CredentialCard.razor`**

Replace lines 30-57 (the `@if (Credential.HighlightClaims.Count > 0)` block) with:

```razor
        @if (!string.IsNullOrEmpty(Credential.ClaimSummary))
        {
            <MudDivider Class="my-2" Style="@($"border-color: {Credential.DisplayConfig.TextColor}; opacity: 0.3")" />
            @* Names only. A list view never prints claim values — a holder's address
               should not be legible to anyone glancing at their screen. Values live
               behind View. *@
            <MudText Typo="Typo.caption"
                     Style="@($"color: {Credential.DisplayConfig.TextColor}; opacity: 0.75")">
                @Credential.ClaimSummary
            </MudText>
        }
```

And on line 15, render the humanised name:

```razor
                    <MudText Typo="Typo.subtitle1" Style="@($"color: {Credential.DisplayConfig.TextColor}; font-weight: 600")">
                        @(string.IsNullOrEmpty(Credential.DisplayName) ? Credential.Type : Credential.DisplayName)
                    </MudText>
```

Add an Archive-reason chip. Replace `GetStatusText()` (line 129) with:

```csharp
    private string GetStatusText() => Credential.Status switch
    {
        Consumed => "Used",
        Expired => Credential.ExpiresAt.HasValue
            ? $"Expired {Credential.ExpiresAt.Value:MMM d, yyyy}"
            : "Expired",
        Revoked => "Revoked by issuer",
        _ => Credential.Status
    };
```

- [ ] **Step 4: Fix the padlocks on `CredentialAcceptCard.razor`**

The markup at lines 49-56 is already correct — it was only ever wrong because `DisclosableClaims` was empty. Task 3 fixed that. Two changes here:

Line 18 — humanised name:

```razor
                    <MudText Typo="Typo.subtitle1" Style="font-weight: 600">@(string.IsNullOrEmpty(Credential.DisplayName) ? Credential.Type : Credential.DisplayName)</MudText>
```

Lines 27-34 — the issuer is already printed on line 19, so drop the duplicate "From …" subtitle and keep only the blueprint provenance:

```razor
        @* Provenance. The issuer name is already on the header row — don't say it twice. *@
        @if (!string.IsNullOrEmpty(Credential.OriginatingBlueprintName))
        {
            <MudText Typo="Typo.caption" Class="mb-3" Style="opacity: 0.7">
                via @Credential.OriginatingBlueprintName
            </MudText>
        }
```

- [ ] **Step 5: Drop the duplicate status chips from `CredentialCardList.razor`**

The page now owns the Active/Archive split, so filtering by status again inside the list is the same taxonomy applied twice. Replace lines 8-34 with:

```razor
<MudStack Spacing="3">
    @* Search only. Status is the page's job now (Your credentials vs Archive) —
       filtering by it again in here was the same taxonomy applied twice. *@
    @if (Credentials.Count > 4)
    {
        <MudStack Row="true" Spacing="2" AlignItems="AlignItems.Center">
            <MudSpacer />
            <MudTextField @bind-Value="_searchText" Placeholder="Search by type or issuer..."
                          Adornment="Adornment.Start" AdornmentIcon="@Icons.Material.Filled.Search"
                          Immediate="true" Variant="Variant.Outlined" Margin="Margin.Dense"
                          Class="search-field" Style="max-width: 300px" />
        </MudStack>
    }
```

Then delete `_selectedStatus` (line 78) and `OnStatusFilterChanged` (lines 103-106), and simplify `FilteredCredentials` (line 81):

```csharp
    private IEnumerable<CredentialCardViewModel> FilteredCredentials
    {
        get
        {
            var filtered = Credentials.AsEnumerable();

            if (!string.IsNullOrWhiteSpace(_searchText))
            {
                var search = _searchText.Trim();
                filtered = filtered.Where(c =>
                    c.Type.Contains(search, StringComparison.OrdinalIgnoreCase) ||
                    c.DisplayName.Contains(search, StringComparison.OrdinalIgnoreCase) ||
                    c.IssuerName.Contains(search, StringComparison.OrdinalIgnoreCase) ||
                    c.IssuerDid.Contains(search, StringComparison.OrdinalIgnoreCase));
            }

            return filtered.OrderByDescending(c => c.IssuedAt);
        }
    }
```

Remove the now-unused `@using static Sorcha.UI.Core.Models.Credentials.CredentialStatus` on line 6 if the compiler flags it.

- [ ] **Step 6: Run — verify it passes**

Run: `dotnet build && dotnet test tests/Sorcha.UI.Components.User.Tests/Sorcha.UI.Components.User.Tests.csproj --filter "FullyQualifiedName~CredentialCardTests"`
Expected: **PASS**, 4 tests.

- [ ] **Step 7: Commit**

```bash
git add src/Apps/Sorcha.UI/Sorcha.UI.Components.User/Components/Credentials/ \
        tests/Sorcha.UI.Components.User.Tests/Components/Credentials/CredentialCardTests.cs
git commit -m "feat: cards show identity + claim names; padlocks tell the truth

The list no longer prints the holder's personal data. The accept card's locks
now reflect real disclosability. The list's duplicate status chips are gone —
the page owns that split now.

Co-Authored-By: Claude Opus 4.8 (1M context) <noreply@anthropic.com>"
```

---

## Task 5: The page — three bands, zero tabs

**Files:**
- Modify: `src/Apps/Sorcha.UI/Sorcha.UI.Web.Client/Pages/MyCredentials.razor`
- Modify: `src/Apps/Sorcha.UI/Sorcha.UI.Components.User/Services/User/Credentials/{ICredentialApiService,CredentialApiService}.cs` (Task 3 Step 7, deferred here)

**Interfaces:**
- Consumes: `CredentialCardList`, `CredentialAcceptCard` (Task 4)

- [ ] **Step 1: Replace the tabs with bands**

In `MyCredentials.razor`, replace the whole `else` branch (lines 73-187, the `<MudTabs>…</MudTabs>` block) with:

```razor
    else if (_credentials.Count == 0)
    {
        <MudPaper Elevation="0" Class="pa-8 d-flex flex-column align-center justify-center" Style="min-height: 200px">
            <MudIcon Icon="@Icons.Material.Filled.Badge" Size="MudBlazor.Size.Large" Color="Color.Default" Class="mb-4" />
            <MudText Typo="Typo.h6" Color="Color.Secondary">No credentials yet</MudText>
            <MudText Typo="Typo.body2" Color="Color.Secondary" Class="mt-1">
                Credentials will appear here when they are issued to you.
            </MudText>
        </MudPaper>
    }
    else
    {
        @* Band 1 — Needs you. Anything waiting on the holder. Absent when empty. *@
        @if (_pendingCredentials.Count > 0)
        {
            <MudStack Row="true" AlignItems="AlignItems.Center" Spacing="2" Class="mb-3">
                <MudIcon Icon="@Icons.Material.Filled.HourglassEmpty" Color="Color.Warning" />
                <MudText Typo="Typo.h6">Needs you</MudText>
                <MudChip T="string" Color="Color.Warning" Size="MudBlazor.Size.Small" Variant="Variant.Filled">
                    @_pendingCredentials.Count
                </MudChip>
            </MudStack>

            <MudGrid Class="mb-6">
                @foreach (var credential in _pendingCredentials)
                {
                    var cred = credential;
                    <MudItem xs="12" sm="12" md="6" lg="4">
                        <CredentialAcceptCard Credential="cred"
                                              IsLoading="_acceptingCredentialId == cred.CredentialId"
                                              OnAccept="@(() => OnAcceptCredential(cred))"
                                              OnDecline="@(() => OnDeclineCredential(cred))" />
                    </MudItem>
                }
            </MudGrid>
        }

        @* Band 2 — what the holder actually holds. *@
        @if (_activeCredentials.Count > 0)
        {
            <MudText Typo="Typo.h6" Class="mb-3">Your credentials</MudText>
            <CredentialCardList Credentials="_activeCredentials"
                                OnViewClick="OnViewCredential"
                                OnPresentClick="OnPresentCredential" />
        }

        @* Band 3 — Archive. Expired and revoked together: both are "no longer usable",
           and the difference is a property of the card (its status chip), not a place
           you navigate to. Collapsed, so it costs nothing at rest. *@
        @if (_archivedCredentials.Count > 0)
        {
            <MudExpansionPanels Elevation="0" Class="mt-6">
                <MudExpansionPanel Expanded="false">
                    <TitleContent>
                        <MudStack Row="true" AlignItems="AlignItems.Center" Spacing="2">
                            <MudIcon Icon="@Icons.Material.Filled.Inventory2" Color="Color.Default" Size="MudBlazor.Size.Small" />
                            <MudText Typo="Typo.body1">Archive</MudText>
                            <MudText Typo="Typo.caption" Color="Color.Secondary">
                                @_archivedCredentials.Count no longer usable
                            </MudText>
                        </MudStack>
                    </TitleContent>
                    <ChildContent>
                        <CredentialCardList Credentials="_archivedCredentials"
                                            OnViewClick="OnViewCredential"
                                            OnPresentClick="OnPresentCredential" />
                    </ChildContent>
                </MudExpansionPanel>
            </MudExpansionPanels>
        }
    }
```

- [ ] **Step 2: Replace the state fields and the loader**

Replace the field block (lines 191-201) with:

```csharp
    private List<CredentialCardViewModel> _credentials = new();
    private List<CredentialCardViewModel> _pendingCredentials = new();
    private List<CredentialCardViewModel> _activeCredentials = new();
    private List<CredentialCardViewModel> _archivedCredentials = new();
    private List<WalletDto> _wallets = new();
    private string _selectedWallet = string.Empty;
    private bool _loading = true;
    private bool _serviceError;
    private string? _acceptingCredentialId;
```

Replace `LoadAllAsync` (lines 235-273) with:

```csharp
    private async Task LoadAllAsync()
    {
        _loading = true;
        _serviceError = false;
        StateHasChanged();

        try
        {
            _credentials = await CredentialApi.GetCredentialsAsync(_selectedWallet);

            _pendingCredentials = _credentials.Where(c => c.IsPending).ToList();

            _activeCredentials = _credentials
                .Where(c => c.Status == CredentialStatus.Active)
                .ToList();

            // Expired and revoked are one thing to a holder: no longer usable. The
            // reason survives on each card's status chip.
            _archivedCredentials = _credentials
                .Where(c => c.Status is CredentialStatus.Expired or CredentialStatus.Revoked)
                .ToList();

            _loading = false;
        }
        catch
        {
            _serviceError = true;
            _loading = false;
        }
    }
```

- [ ] **Step 3: Delete the dead presentation-request code**

From `MyCredentials.razor`, delete `OnOpenRequest` (lines 438-472) and `FormatTimeRemaining` (lines 474-480), and drop the now-unused `@using Sorcha.UI.Core.Models.Credentials` members if the compiler flags them.

Then complete Task 3 Step 7: delete `GetPresentationRequestsAsync` from `CredentialApiService.cs` **and** `ICredentialApiService.cs`. It calls `GET /api/v1/presentations?wallet={address}`, a route the Wallet Service does not register — the 404 has been swallowed into an empty list for every user since it shipped.

Keep `GetPresentationRequestDetailAsync` and `PresentationRequestDialog` if anything else references them; if nothing does, delete those too.

- [ ] **Step 4: Build and run everything touched**

Run:
```bash
dotnet build
dotnet test tests/Sorcha.UI.Core.Tests/Sorcha.UI.Core.Tests.csproj
dotnet test tests/Sorcha.UI.Components.User.Tests/Sorcha.UI.Components.User.Tests.csproj
```
Expected: **PASS**, clean build with no unused-using warnings.

- [ ] **Step 5: Commit**

```bash
git add src/Apps/Sorcha.UI/Sorcha.UI.Web.Client/Pages/MyCredentials.razor \
        src/Apps/Sorcha.UI/Sorcha.UI.Components.User/Services/User/Credentials/
git commit -m "feat: My Credentials is one page, not five tabs

Needs you / Your credentials / collapsed Archive, each absent when empty.
Expired and revoked merge — the reason moves onto the card's chip.

Deletes the Inbox tab: it called GET /api/v1/presentations?wallet=, a route the
Wallet Service never registered, and has rendered empty for every user since it
shipped. What a real verifier-request inbox would need is in spec section 6.1.

Co-Authored-By: Claude Opus 4.8 (1M context) <noreply@anthropic.com>"
```

---

## Task 6: The PWA reads nested disclosures on-device

The PWA is the phone in the two-device proximity run. Its `SdJwtReader` reads **only** the disclosure segments, so it never prints `{"_sd":…}` — but it flattens nested children to top level and dumps an object value as raw JSON.

**Files:**
- Modify: `src/Apps/Sorcha.Wallet.Pwa/Services/SdJwtReader.cs`
- Test: `tests/Sorcha.Wallet.Pwa.Tests/Services/SdJwtReaderTests.cs`

**Interfaces:**
- Produces: `SdJwtReader.ReadDisclosedClaims(string?) → IReadOnlyList<DisclosedClaim>` — unchanged signature; nested values now render structurally.

**Constraint:** this runs in Blazor **WASM**. Use BCL JSON only — no `Sorcha.Cryptography` (it P/Invokes libsodium and cannot load in the browser). That is *why* the PWA has its own reader rather than sharing the server's.

- [ ] **Step 1: Write the failing test**

Append to `tests/Sorcha.Wallet.Pwa.Tests/Services/SdJwtReaderTests.cs`:

```csharp
    [Fact]
    public void ReadDisclosedClaims_ObjectValue_RendersFieldNamesNotRawJson()
    {
        // A disclosure whose VALUE is an object must not print as {"town":"Edinburgh"}.
        var disclosure = Disclosure("s1", "address", new Dictionary<string, object>
        {
            ["town"] = "Edinburgh",
            ["line1"] = "6/2 Warrender Park Terrace"
        });
        var token = $"{Jwt()}~{disclosure}";

        var claims = SdJwtReader.ReadDisclosedClaims(token);

        var address = claims.Single(c => c.Name == "address");
        address.Value.Should().NotContain("{");
        address.Value.Should().NotContain("\"town\"");
        address.Value.Should().Contain("Edinburgh");
    }

    [Fact]
    public void ReadDisclosedClaims_NeverLeaksSdDigests()
    {
        var disclosure = Disclosure("s1", "address", new Dictionary<string, object>
        {
            ["_sd"] = new[] { "zSH_kfTeW2Mlc" }
        });
        var token = $"{Jwt()}~{disclosure}";

        var claims = SdJwtReader.ReadDisclosedClaims(token);

        claims.Single(c => c.Name == "address").Value.Should().NotContain("_sd");
    }
```

Add these helpers to the test class if it does not already have equivalents:

```csharp
    private static string B64Url(byte[] b) =>
        Convert.ToBase64String(b).TrimEnd('=').Replace('+', '-').Replace('/', '_');

    private static string Disclosure(string salt, string name, object value) =>
        B64Url(System.Text.Encoding.UTF8.GetBytes(
            System.Text.Json.JsonSerializer.Serialize(new object[] { salt, name, value })));

    private static string Jwt() =>
        $"{B64Url(System.Text.Encoding.UTF8.GetBytes("""{"alg":"ES256"}"""))}." +
        $"{B64Url(System.Text.Encoding.UTF8.GetBytes("""{"vct":"x"}"""))}.c2ln";
```

- [ ] **Step 2: Run — verify it fails**

Run: `dotnet test tests/Sorcha.Wallet.Pwa.Tests/Sorcha.Wallet.Pwa.Tests.csproj --filter "FullyQualifiedName~SdJwtReaderTests"`
Expected: **FAIL** — the object dumps as raw JSON via `value.GetRawText()`.

- [ ] **Step 3: Fix `JsonValueToString`**

Replace it (lines 87-95 of `SdJwtReader.cs`):

```csharp
    /// <summary>
    /// Renders a disclosure value for the card. An object or array NEVER renders as
    /// raw JSON — that is how an unresolved SD-JWT digest array reached a citizen's
    /// card on the web. This reader is the on-device equivalent and must not repeat it.
    /// </summary>
    private static string JsonValueToString(JsonElement value) => value.ValueKind switch
    {
        JsonValueKind.String => value.GetString() ?? string.Empty,
        JsonValueKind.True => "Yes",
        JsonValueKind.False => "No",
        JsonValueKind.Null => string.Empty,
        JsonValueKind.Number => value.GetRawText(),
        JsonValueKind.Object => SummariseObject(value),
        JsonValueKind.Array => SummariseArray(value),
        _ => string.Empty,
    };

    /// <summary>
    /// A nested object renders as "field: value" pairs, dropping SD-JWT protocol keys
    /// so a digest array degrades to an empty string rather than a blob of base64.
    /// </summary>
    private static string SummariseObject(JsonElement value)
    {
        var parts = value.EnumerateObject()
            .Where(p => !p.Name.StartsWith('_'))
            .Select(p => $"{p.Name}: {JsonValueToString(p.Value)}")
            .Where(s => !s.EndsWith(": ", StringComparison.Ordinal))
            .ToList();

        return string.Join(", ", parts);
    }

    private static string SummariseArray(JsonElement value)
    {
        var parts = value.EnumerateArray()
            .Select(JsonValueToString)
            .Where(s => !string.IsNullOrEmpty(s))
            .ToList();

        return string.Join(", ", parts);
    }
```

- [ ] **Step 4: Run — verify it passes**

Run: `dotnet build && dotnet test tests/Sorcha.Wallet.Pwa.Tests/Sorcha.Wallet.Pwa.Tests.csproj`
Expected: **PASS**, existing tests still green.

- [ ] **Step 5: Commit**

```bash
git add src/Apps/Sorcha.Wallet.Pwa/Services/SdJwtReader.cs \
        tests/Sorcha.Wallet.Pwa.Tests/Services/SdJwtReaderTests.cs
git commit -m "fix: PWA renders a nested disclosure structurally, not as raw JSON

The PWA is the device in the two-device proximity run, and a credential read
straight from its raw token on-device still dumped an object value as JSON.

Co-Authored-By: Claude Opus 4.8 (1M context) <noreply@anthropic.com>"
```

---

## Task 7: Verify against n1

The bug was found on a real device against a real credential. Prove the fix the same way — a green test suite is not the same as a fixed screen.

- [ ] **Step 1: Full build + the four touched test projects**

```bash
dotnet build
dotnet test tests/Sorcha.Wallet.Service.Tests/Sorcha.Wallet.Service.Tests.csproj
dotnet test tests/Sorcha.UI.Core.Tests/Sorcha.UI.Core.Tests.csproj
dotnet test tests/Sorcha.UI.Components.User.Tests/Sorcha.UI.Components.User.Tests.csproj
dotnet test tests/Sorcha.Wallet.Pwa.Tests/Sorcha.Wallet.Pwa.Tests.csproj
```
Expected: **PASS** on all four.

- [ ] **Step 2: Confirm no migration was created**

```bash
git status --short | grep -i migration
```
Expected: **no output.** If a migration appeared, you took the wrong turn — the disclosable set is derived from `RawToken` on read, not persisted. Revert it.

- [ ] **Step 3: Check the existing AIAS credential renders**

The credential in the bug report is already in Stuart's wallet on n1 (`AssuredIdentityCredential` from Acme Identity Assurance Services, with the nested `address`). Because `ClaimsJson` is repaired **at ingest**, an already-stored credential keeps its bad `ClaimsJson` until it is re-ingested.

**This is the one thing that will bite.** Confirm which is true and report it:
- The list endpoint derives `disclosableClaims` from `RawToken` on read → **the padlocks fix themselves** for existing rows.
- `ClaimsJson` was written at ingest → **an existing row keeps its `{"_sd":…}` address** until re-issued.

If the second holds, the UI guard from Task 3 Step 6 stops it rendering as a blob (it will degrade to an empty string or a field-name list), but the address will not be *right* until a fresh credential is issued. Say so plainly rather than claiming the fix is complete. If a backfill is wanted, that is a follow-up: re-project `ClaimsJson` from `RawToken` for existing rows.

- [ ] **Step 4: Open the page and look at it**

Deploy or run locally, sign in as a citizen holding the AIAS credential, and confirm:
- No tabs.
- No `{"_sd":…}` anywhere.
- The Active card shows a claim-name summary and **no** values.
- Archive is collapsed and holds any expired/revoked credentials.
- A pending offer shows open padlocks on the disclosable claims and closed on the rest.

- [ ] **Step 5: Push and open the PR**

```bash
git push -u origin feature/my-credentials-redesign
gh pr create --fill
```

---

## Self-Review

**Spec coverage:**
- §3 zero-tab page → Task 5. §3.1 identity + claim-name summary → Tasks 3, 4. §3.2 offer card distinct → Task 4.
- §4 nested `_sd` (server + UI guard) → Task 1, Task 3 Step 6.
- §5 padlocks; §5.1 the missing information; §5.2 derive from `RawToken`, no migration → Tasks 1, 2, 3.
- §6 delete Inbox → Task 5 Step 3. §6.1 not-in-scope → recorded in the spec, restated in the Task 5 commit message.
- §7 PWA `SdJwtReader` → Task 6.
- §8 tests → Tasks 1, 3, 4, 6. §10 SC-1…SC-6 → Task 7.

**Known sharp edge, deliberately surfaced rather than hidden:** `ClaimsJson` is written at **ingest**, so credentials already in a wallet keep their bad value until re-issued. The disclosable set *is* derived on read, so padlocks self-heal. Task 7 Step 3 forces this to be checked and stated honestly instead of being papered over by the UI guard.

**Type consistency:** `SdJwtProjection(ClaimsJson, DisclosableClaims)` is produced in Task 1 and consumed in Task 2. `CredentialCardViewModel.{DisplayName, ClaimSummary, DisclosableClaims}` produced in Task 3, consumed in Task 4 and the bUnit tests. `CredentialCardList` keeps its `Credentials` / `OnViewClick` / `OnPresentClick` parameters, so Task 5's call sites are unchanged.
