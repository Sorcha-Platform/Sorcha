# Credential VCT Decoupling Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Make a credential's `vct` a canonical URI decoupled from its display label, drop the non-standard `type` claim, and convert every credential type to a `VctUris`-anchored URI so the citizen-wallet verifier stops reporting "None of your credentials match".

**Architecture:** Split the overloaded `CredentialIssuanceConfig.CredentialType` into an explicit `Vct` (canonical URI, machine identity) + `DisplayName` (authored human label), both optional with fallbacks. Every SD-JWT VC carries `vct` only (SD-JWT VC has no `type` claim). Every credential type gets one `VctUris` constant; a parametrised conformance test asserts every blueprint literal equals its constant so JSON and C# cannot drift. Matching is case-sensitive exact everywhere (spec-conformant).

**Tech Stack:** .NET 10 / C# 14, System.Text.Json, xUnit v3 + FluentAssertions + Moq, Blazor WASM (the PWA reader must stay libsodium-free).

## Global Constraints

- License header on every new file: `// SPDX-License-Identifier: MIT` / `// Copyright (c) 2026 Sorcha Contributors`. File-scoped namespaces. Test naming `MethodName_Scenario_ExpectedBehavior`.
- **SD-JWT VC conformance (draft-ietf-oauth-sd-jwt-vc §3.2.2.1):** `vct` is the sole type claim, a **case-sensitive** `StringOrURI`; there is **no** `type` claim; matching is **case-sensitive exact**. Do not reintroduce a `type` claim; do not make VCT matching case-insensitive.
- **VCT URIs are lowercase kebab-case:** `https://sorcha.dev/vc/{type}/v1`.
- **Never** hard-code `<Version>` in a `.csproj`. **No EF migration** — `CredentialEntity.Type` is an existing column; we change what value flows into it, not its schema.
- `dotnet build` before `dotnet test`. `dotnet test` takes ONE project. `--filter` does NOT isolate tests in this repo (MTP) — run the whole project and read totals. Record each project's baseline before editing it.
- **Never `git add -A`** — the tree carries unrelated untracked user work. Stage explicit paths only.
- The PWA (`Sorcha.Wallet.Pwa`) is Blazor WASM: BCL only, no `Sorcha.Cryptography` reference.

**Spec:** `docs/superpowers/specs/2026-07-15-credential-vct-decoupling-design.md`

---

## File Structure

| File | Change |
|---|---|
| `src/Common/Sorcha.Cryptography/SdJwt/VctUris.cs` (or wherever `VctUris` lives — grep) | Add one constant per credential type |
| `src/Common/Sorcha.Blueprint.Models/Credentials/CredentialIssuanceConfig.cs` | Add `Vct` + `DisplayName` |
| `src/Core/Sorcha.Blueprint.Engine/Credentials/CredentialIssuer.cs:44-45,75,84` | `vct` only; `Vct ?? CredentialType`; DisplayName → display config |
| `src/Services/Sorcha.Wallet.Service/Endpoints/CredentialEndpoints.cs:705-706` | Mirror: `vct` only |
| `src/Services/Sorcha.Wallet.Service/Services/Implementation/EfCoreCitizenCredentialEventStream.cs` (`BuildPayload`) | Populate `DisplayMeta.credentialName` |
| `src/Apps/Sorcha.Wallet.Pwa/Services/ISyncService.cs` (`ToCachedCredential`) | Map `DisplayMeta.credentialName` → `DisplayLabel` |
| `src/Core/Sorcha.Blueprint.Engine/Credentials/CredentialVerifier.cs` (`TypeMatches`) | `OrdinalIgnoreCase` → `Ordinal` |
| `src/Services/Sorcha.Wallet.Service/Credentials/CredentialMatcher.cs:64,138` | `OrdinalIgnoreCase` → `Ordinal` |
| `src/Apps/Sorcha.UI/Sorcha.UI.Components.User/Services/User/Verification/DefaultPresetCatalogue.cs:17` | Reference `VctUris.AssuredIdentityV1` |
| Blueprint validation seam (grep: where `CredentialIssuanceConfig` is validated at publish) | `Vct` absolute-URI check |
| All blueprints under `demos/`, `walkthroughs/`, `blueprints/` | Convert every credential type to URI + `displayName` |
| Test projects (Engine, Wallet.Service, UI.Core, Wallet.Pwa, a blueprint-conformance test) | New tests per task |

---

## Task 1: VctUris registry — one constant per credential type

**Files:**
- Modify: `VctUris` (grep `class VctUris` / `CitizenDeviceDelegationV1` to find it — reported at `Sorcha.Cryptography` VctUris.cs)
- Test: the same test project that covers `VctUris` today (grep), else add `VctUrisTests` in the owning project's test project

**Interfaces:**
- Produces: `public const string VctUris.AssuredIdentityV1`, `.DrivingLicenceV1`, `.BlueBadgeV1`, `.MembershipV1`, `.LicenceV1`, `.CouncilDigitalIdV1`, `.VerifiedInvoiceV1`, `.TradeFinanceV1`, `.PlanningPermissionV1`, `.BuildingWarrantV1`, `.CompletionCertificateV1`, `.JobAssignmentV1`, `.ServiceCompletionV1`, `.ForestProductDppV1`, `.CyberEssentialsUacV1`, `.RefurbishmentCertificateV1` — all `https://sorcha.dev/vc/{kebab}/v1`.

- [ ] **Step 1: Open `VctUris` and confirm the existing shape**

Run: grep for the class and read it.
```bash
grep -rn "class VctUris\|CitizenDeviceDelegationV1" src/Common --include=*.cs
```
Expected: one file with `public const string CitizenDeviceDelegationV1 = "https://sorcha.dev/vc/citizen-device-delegation/v1";`. Match its exact style.

- [ ] **Step 2: Write a failing test asserting the new constants exist with correct values**

Add to the `VctUris` test file (or create it, license header + file-scoped namespace):

```csharp
[Theory]
[InlineData("AssuredIdentityV1", "https://sorcha.dev/vc/assured-identity/v1")]
[InlineData("DrivingLicenceV1", "https://sorcha.dev/vc/driving-licence/v1")]
[InlineData("BlueBadgeV1", "https://sorcha.dev/vc/blue-badge/v1")]
[InlineData("MembershipV1", "https://sorcha.dev/vc/membership/v1")]
public void VctUris_CanonicalConstants_HaveExpectedLowercaseUri(string field, string expected)
{
    var value = (string)typeof(VctUris).GetField(field)!.GetValue(null)!;
    value.Should().Be(expected);
    value.Should().Be(value.ToLowerInvariant(), "VCT URIs are lowercase kebab-case");
}
```

- [ ] **Step 3: Run — verify it fails**

Run: `dotnet build <owning project> && dotnet test <owning test project>`
Expected: FAIL — `AssuredIdentityV1` field not found.

- [ ] **Step 4: Add the constants**

Add to `VctUris` (adjust the `{kebab}` slugs to match §6 of the spec exactly):

```csharp
public const string AssuredIdentityV1 = "https://sorcha.dev/vc/assured-identity/v1";
public const string DrivingLicenceV1 = "https://sorcha.dev/vc/driving-licence/v1";
public const string BlueBadgeV1 = "https://sorcha.dev/vc/blue-badge/v1";
public const string MembershipV1 = "https://sorcha.dev/vc/membership/v1";
public const string LicenceV1 = "https://sorcha.dev/vc/licence/v1";
public const string CouncilDigitalIdV1 = "https://sorcha.dev/vc/council-digital-id/v1";
public const string VerifiedInvoiceV1 = "https://sorcha.dev/vc/verified-invoice/v1";
public const string TradeFinanceV1 = "https://sorcha.dev/vc/trade-finance/v1";
public const string PlanningPermissionV1 = "https://sorcha.dev/vc/planning-permission/v1";
public const string BuildingWarrantV1 = "https://sorcha.dev/vc/building-warrant/v1";
public const string CompletionCertificateV1 = "https://sorcha.dev/vc/completion-certificate/v1";
public const string JobAssignmentV1 = "https://sorcha.dev/vc/job-assignment/v1";
public const string ServiceCompletionV1 = "https://sorcha.dev/vc/service-completion/v1";
public const string ForestProductDppV1 = "https://sorcha.dev/vc/forest-product-dpp/v1";
public const string CyberEssentialsUacV1 = "https://sorcha.dev/vc/cyber-essentials-uac/v1";
public const string RefurbishmentCertificateV1 = "https://sorcha.dev/vc/refurbishment-certificate/v1";
```

- [ ] **Step 5: Run — verify it passes, then commit**

Run: `dotnet build <owning project> && dotnet test <owning test project>` → PASS.
```bash
git add <VctUris.cs> <VctUris test file>
git commit -m "feat: [vct] add canonical VCT constants for every credential type"
```

---

## Task 2: Add `Vct` + `DisplayName` to `CredentialIssuanceConfig`

**Files:**
- Modify: `src/Common/Sorcha.Blueprint.Models/Credentials/CredentialIssuanceConfig.cs`
- Test: `tests/Sorcha.Blueprint.Models.Tests/…` (grep for the existing config test; if none, assert via a serialization round-trip test in the Engine test project)

**Interfaces:**
- Produces: `CredentialIssuanceConfig.Vct` (`string?`, JSON `vct`), `CredentialIssuanceConfig.DisplayName` (`string?`, JSON `displayName`).

- [ ] **Step 1: Write the failing test — JSON round-trips the two new fields**

```csharp
[Fact]
public void CredentialIssuanceConfig_RoundTrips_VctAndDisplayName()
{
    var json = """
    {"credentialType":"AssuredIdentityCredential",
     "vct":"https://sorcha.dev/vc/assured-identity/v1",
     "displayName":"Assured Identity",
     "recipientParticipantId":"applicant",
     "claimMappings":[{"claimName":"x","sourceField":"/x"}]}
    """;
    var cfg = JsonSerializer.Deserialize<CredentialIssuanceConfig>(json, JsonDefaults.Api)!;
    cfg.Vct.Should().Be("https://sorcha.dev/vc/assured-identity/v1");
    cfg.DisplayName.Should().Be("Assured Identity");
}
```

- [ ] **Step 2: Run — verify it fails** (`Vct`/`DisplayName` don't exist). `dotnet build` will error on the missing members.

- [ ] **Step 3: Add the fields** after `CredentialType` (line 22):

```csharp
    /// <summary>
    /// Canonical SD-JWT VC type identifier (the <c>vct</c> claim). MUST be an absolute URI,
    /// lowercase kebab-case: <c>https://sorcha.dev/vc/{type}/v1</c>. This is the machine
    /// matching identity — a verifier's request and the held credential match on this string
    /// (case-sensitive exact, per SD-JWT VC §3.2.2.1). When null, <see cref="CredentialType"/>
    /// is used as the vct (defensive fallback for hand-authored/legacy configs).
    /// </summary>
    [JsonPropertyName("vct")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? Vct { get; set; }

    /// <summary>
    /// Human-readable label shown on the credential card (e.g. "Assured Identity"). Decoupled
    /// from <see cref="Vct"/> so display never depends on parsing the URI. When null, the wallet
    /// falls back to humanising the vct.
    /// </summary>
    [JsonPropertyName("displayName")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? DisplayName { get; set; }
```

- [ ] **Step 4: Run — verify it passes.** `dotnet build src/Common/Sorcha.Blueprint.Models/... && dotnet test <the test project>` → PASS.

- [ ] **Step 5: Commit**
```bash
git add src/Common/Sorcha.Blueprint.Models/Credentials/CredentialIssuanceConfig.cs <test file>
git commit -m "feat: [vct] add explicit Vct + DisplayName to CredentialIssuanceConfig"
```

---

## Task 3: Issuer emits `vct` only (drop `type`), from `Vct ?? CredentialType`

**Files:**
- Modify: `src/Core/Sorcha.Blueprint.Engine/Credentials/CredentialIssuer.cs:44-45` (+ display config at ~84)
- Modify: `src/Services/Sorcha.Wallet.Service/Endpoints/CredentialEndpoints.cs:705-706` (mirror)
- Test: `tests/Sorcha.Blueprint.Engine.Tests/Credentials/CredentialIssuerTests.cs`

**Interfaces:**
- Consumes: `CredentialIssuanceConfig.Vct`, `.DisplayName` (Task 2).
- Produces: minted SD-JWT whose payload has `vct` (= `Vct ?? CredentialType`) and **no** `type` claim; `IssuedCredentialInfo.Type` = the same vct string.

- [ ] **Step 1: Write the failing tests**

Add to `CredentialIssuerTests.cs` (match the file's existing factory/mocking):

```csharp
[Fact]
public async Task IssueAsync_WithVct_WritesVctClaim_AndNoTypeClaim()
{
    var config = MakeConfig();               // existing helper
    config.Vct = "https://sorcha.dev/vc/assured-identity/v1";
    config.CredentialType = "AssuredIdentityCredential";

    var info = await _issuer.IssueAsync(config, Data(), IssuerDid, RecipientDid, Key(), "EdDSA");

    info.Claims["vct"].Should().Be("https://sorcha.dev/vc/assured-identity/v1");
    info.Claims.Should().NotContainKey("type", "SD-JWT VC has no type claim");
    info.Type.Should().Be("https://sorcha.dev/vc/assured-identity/v1");
}

[Fact]
public async Task IssueAsync_WithoutVct_FallsBackToCredentialType()
{
    var config = MakeConfig();
    config.Vct = null;
    config.CredentialType = "LegacyBareName";

    var info = await _issuer.IssueAsync(config, Data(), IssuerDid, RecipientDid, Key(), "EdDSA");

    info.Claims["vct"].Should().Be("LegacyBareName");
    info.Claims.Should().NotContainKey("type");
}
```

- [ ] **Step 2: Run — verify both fail** (`type` still written; `vct` still = CredentialType even when `Vct` set). Run the Engine test project.

- [ ] **Step 3: Change the issuer.** Replace `CredentialIssuer.cs:44-45`:

```csharp
        // SD-JWT VC (draft-ietf-oauth-sd-jwt-vc §3.2.2.1): vct is the SOLE type claim, a
        // case-sensitive URI. There is no `type` claim in the profile — do not write one.
        var vct = string.IsNullOrWhiteSpace(config.Vct) ? config.CredentialType : config.Vct;
        claims["vct"] = vct;
```

Change line 75 `Type = config.CredentialType,` → `Type = vct,`.

Confirm the display-config block (~84) carries `DisplayName`. If `DisplayConfigJson` is built from `config.DisplayConfig`, thread `config.DisplayName` into it so it reaches the credential (the exact shape depends on `CredentialDisplayConfig` / how `DisplayConfigJson` is serialized — **read lines 84-95 first**; the credential's stored display carrier must end up with the credential name so Task 4 can surface it). If `CredentialDisplayConfig` has no name field, carry `DisplayName` on `IssuedCredentialInfo` and persist it where `CredentialEntity` display data is written — trace `IssuedCredentialInfo` → `CredentialEntity` and put the name on the entity's display JSON.

- [ ] **Step 4: Mirror in the Wallet Service direct-issue path.** `CredentialEndpoints.cs:705-706` currently:
```csharp
claims["type"] = request.CredentialType;
claims["vct"] = request.CredentialType;
```
Replace with (add a `Vct` to `IssueCredentialRequest` mirroring the config, or fall back to `CredentialType`):
```csharp
claims["vct"] = string.IsNullOrWhiteSpace(request.Vct) ? request.CredentialType : request.Vct;
```
Read `IssueCredentialRequest` first; add a nullable `Vct` property if absent.

- [ ] **Step 5: Run — verify passing.** `dotnet build && dotnet test tests/Sorcha.Blueprint.Engine.Tests/...` → PASS. Also run `tests/Sorcha.Wallet.Service.Tests` (baseline first) — any test asserting a `type` claim was asserting the non-standard artefact; update it to assert `vct` only and note it in the commit.

- [ ] **Step 6: Commit**
```bash
git add src/Core/Sorcha.Blueprint.Engine/Credentials/CredentialIssuer.cs \
        src/Services/Sorcha.Wallet.Service/Endpoints/CredentialEndpoints.cs \
        tests/Sorcha.Blueprint.Engine.Tests/Credentials/CredentialIssuerTests.cs
git commit -m "fix: [vct] issue vct only (drop non-standard type claim); use Vct ?? CredentialType"
```

---

## Task 4: Carry the authored display name to the PWA card

**Files:**
- Modify: `src/Services/Sorcha.Wallet.Service/Services/Implementation/EfCoreCitizenCredentialEventStream.cs` (`BuildPayload`)
- Modify: `src/Apps/Sorcha.Wallet.Pwa/Services/ISyncService.cs` (`ToCachedCredential`, ~line 273)
- Test: `tests/Sorcha.Wallet.Pwa.Tests/...` (sync mapping) + a Wallet.Service test for `BuildPayload`

**Interfaces:**
- Consumes: the credential's stored display name (Task 3).
- Produces: `CachedCredentialPayload.DisplayMeta.credentialName` populated on sync-out; `CachedCredential.DisplayLabel` populated on the PWA so `CredentialDisplay.Name` shows it instead of `Humanize(vct)`.

- [ ] **Step 1: Read the three shapes first**

Read `CachedCredentialPayload` (`Sorcha.CitizenWallet.Abstractions/Models/CachedCredentialPayload.cs` — `DisplayMeta` is `JsonObject?`), `EfCoreCitizenCredentialEventStream.BuildPayload` (currently sets `Vct = entity.Type`, no label), and `ISyncService.ToCachedCredential` (`:273`, currently `DisplayLabel` unset). Note the x-review shape of `DisplayMeta` (`credentialName`, `issuerName`, `colourTheme`).

- [ ] **Step 2: Write the failing PWA mapping test**

```csharp
[Fact]
public void ToCachedCredential_PopulatesDisplayLabel_FromDisplayMetaCredentialName()
{
    var payload = new CachedCredentialPayload
    {
        Id = "urn:credential:x",
        Vct = "https://sorcha.dev/vc/assured-identity/v1",
        DisplayMeta = new JsonObject { ["credentialName"] = "Assured Identity" }
    };
    var cached = SyncService.ToCachedCredential(payload);   // make static or expose a seam if needed
    cached.DisplayLabel.Should().Be("Assured Identity");
}
```

- [ ] **Step 3: Run — verify it fails** (`DisplayLabel` null).

- [ ] **Step 4: Populate on both sides**

In `EfCoreCitizenCredentialEventStream.BuildPayload`, set `DisplayMeta.credentialName` from the entity's stored display name (from Task 3). If the entity stores display JSON, parse the credential name out of it; if Task 3 added a dedicated column, read that.

In `ISyncService.ToCachedCredential`, map it:
```csharp
DisplayLabel = payload.DisplayMeta?["credentialName"]?.GetValue<string>(),
```

- [ ] **Step 5: Run — verify passing.** Build + run `tests/Sorcha.Wallet.Pwa.Tests` and `tests/Sorcha.Wallet.Service.Tests` (baselines first). The existing `CredentialDisplayTests.Humanize` table must still pass (fallback path unchanged when `DisplayLabel` is null).

- [ ] **Step 6: Commit**
```bash
git add src/Services/Sorcha.Wallet.Service/Services/Implementation/EfCoreCitizenCredentialEventStream.cs \
        src/Apps/Sorcha.Wallet.Pwa/Services/ISyncService.cs <the two test files>
git commit -m "feat: [vct] carry authored displayName through sync to the PWA card"
```

---

## Task 5: Case-sensitive VCT matching everywhere (spec conformance)

**Files:**
- Modify: `src/Core/Sorcha.Blueprint.Engine/Credentials/CredentialVerifier.cs` (`TypeMatches`, ~line 216)
- Modify: `src/Services/Sorcha.Wallet.Service/Credentials/CredentialMatcher.cs:64,138`
- Test: the existing tests for each (grep `CredentialVerifierTests`, `CredentialMatcherTests`)

**Interfaces:** none new — behaviour-preserving under the single-definition invariant; removes non-standard case leniency.

- [ ] **Step 1: Write a failing test proving a case-mismatched requirement no longer matches**

In `CredentialMatcherTests`:
```csharp
[Fact]
public void Match_TypeCaseMismatch_DoesNotMatch()
{
    var cred = MakeCredential(type: "https://sorcha.dev/vc/assured-identity/v1");
    var req  = MakeRequirement(type: "https://sorcha.dev/vc/Assured-Identity/v1"); // wrong case
    _matcher.Match(cred, req).Should().BeFalse("SD-JWT VC vct matching is case-sensitive");
}
```

- [ ] **Step 2: Run — verify it fails** (today `OrdinalIgnoreCase` returns true).

- [ ] **Step 3: Change both comparisons.** In `CredentialVerifier.TypeMatches` and `CredentialMatcher.cs:64,138`, replace `StringComparison.OrdinalIgnoreCase` with `StringComparison.Ordinal` for the **type/vct** comparison only (do not touch unrelated comparisons in those files). Leave `PresentationEngine.MatchCandidates` as-is (already `Ordinal`).

- [ ] **Step 4: Run — verify passing.** Build + run both test projects (baselines first). Every existing type-match test uses consistent casing (they'll pass); if any deliberately relied on case-folding, it was asserting non-conformant behaviour — update + note it.

- [ ] **Step 5: Commit**
```bash
git add src/Core/Sorcha.Blueprint.Engine/Credentials/CredentialVerifier.cs \
        src/Services/Sorcha.Wallet.Service/Credentials/CredentialMatcher.cs <test files>
git commit -m "fix: [vct] case-sensitive vct matching (SD-JWT VC / DCQL conformance)"
```

---

## Task 6: Publish-time validation — `Vct` must be an absolute URI

**Files:**
- Modify: the blueprint publish validation seam that already validates `CredentialIssuanceConfig` (grep: `credentialIssuanceConfig` / `CredentialType` in a validator; likely `Sorcha.Blueprint.Service` publish validation or `Sorcha.Blueprint.Engine` `SchemaValidator`/blueprint validation)
- Test: that validator's test project

- [ ] **Step 1: Locate the seam.** `grep -rn "CredentialIssuanceConfig" src/Services/Sorcha.Blueprint.Service src/Core/Sorcha.Blueprint.Engine --include=*.cs` and find where issuance config is validated at publish. Read it.

- [ ] **Step 2: Write the failing test**

```csharp
[Fact]
public void Validate_VctNotAbsoluteUri_Fails()
{
    var cfg = MakeConfig();
    cfg.Vct = "not a uri";
    var result = _validator.Validate(cfg);   // match the real API
    result.IsValid.Should().BeFalse();
    result.Errors.Should().Contain(e => e.Contains("vct", StringComparison.OrdinalIgnoreCase));
}
```

- [ ] **Step 3: Run — verify it fails.**

- [ ] **Step 4: Add the rule.** When `Vct` is non-null, require `Uri.TryCreate(cfg.Vct, UriKind.Absolute, out _)`; else emit a validation error. Do not URI-validate the `CredentialType` fallback.

- [ ] **Step 5: Run — verify passing. Commit.**
```bash
git add <validator> <validator test>
git commit -m "feat: [vct] reject a non-URI vct at blueprint publish"
```

---

## Task 7: Verifier presets reference `VctUris`

**Files:**
- Modify: `src/Apps/Sorcha.UI/Sorcha.UI.Components.User/Services/User/Verification/DefaultPresetCatalogue.cs:17`
- Test: existing preset/DCQL tests (grep `DefaultPresetCatalogue`, `DcqlRoundTripTests`)

- [ ] **Step 1: Replace the literal.** `DefaultPresetCatalogue.cs:17` `private const string AssuredIdentityVct = "https://sorcha.dev/vc/assured-identity/v1";` → `= VctUris.AssuredIdentityV1;` (add the `using`). Grep for any other C# file with a `sorcha.dev/vc/...` literal that is a *production* reference (not a test fixture) and point it at the constant.

- [ ] **Step 2: Run the affected test projects** (UI.Core, Verifier). They should pass unchanged (same string, now sourced from the constant).

- [ ] **Step 3: Commit**
```bash
git add src/Apps/Sorcha.UI/Sorcha.UI.Components.User/Services/User/Verification/DefaultPresetCatalogue.cs
git commit -m "refactor: [vct] verifier presets reference VctUris constants"
```

---

## Task 8: Convert every blueprint + the parametrised conformance test (the completeness guarantee)

This task is TDD in the large: the conformance test is written **first** and fails until every blueprint is converted. The test — not a hand-enumerated file list — is what proves no site was missed.

**Files:**
- Create: `tests/Sorcha.Blueprint.<engine-or-models>.Tests/Credentials/BlueprintVctConformanceTests.cs`
- Modify: every blueprint under `demos/`, `walkthroughs/`, `blueprints/` that issues or requires a credential type in the §6 table

- [ ] **Step 1: Enumerate the sites**
```bash
grep -rln "credentialType\|\"type\":" demos walkthroughs blueprints --include=*.json | sort -u
```
Then per bare name from the §6 table, list every occurrence (issue + require):
```bash
for t in AssuredIdentityCredential DrivingLicenceCredential BlueBadgeCredential MembershipCredential LicenseCredential CouncilDigitalIdCredential VerifiedInvoiceCredential TradeFinanceCredential PlanningPermissionCredential BuildingWarrantCredential CompletionCertificateCredential JobAssignmentCredential ServiceCompletionCredential ForestProductDPPCredential CyberEssentialsUacPosture RefurbishmentCertificateCredential; do
  echo "== $t"; grep -rln "$t" demos walkthroughs blueprints --include=*.json
done
```

- [ ] **Step 2: Write the failing conformance test**

A data-driven test that loads every blueprint JSON, and for each `credentialIssuanceConfig` asserts `vct` is present + equals the `VctUris` constant for that credential (mapped by the human `displayName` or an explicit name→constant table in the test), and that every `credentialRequirements[].type` naming a platform type equals the same constant. Build a `Dictionary<string,string>` in the test mapping each bare name → its `VctUris` value; walk all blueprint files; collect violations; assert empty with a message listing every offending file+path.

```csharp
// Skeleton — fill the map from §6, walk demos/ walkthroughs/ blueprints/, assert no violations.
public class BlueprintVctConformanceTests
{
    private static readonly Dictionary<string, string> Canonical = new()
    {
        ["AssuredIdentityCredential"] = VctUris.AssuredIdentityV1,
        ["DrivingLicenceCredential"] = VctUris.DrivingLicenceV1,
        // … all 16 from §6 …
    };

    [Fact]
    public void EveryBlueprint_UsesCanonicalVct_ForIssuanceAndRequirements()
    {
        var repoRoot = FindRepoRoot();      // walk up to the dir containing Sorcha.sln
        var violations = new List<string>();
        foreach (var file in EnumerateBlueprintJson(repoRoot))   // demos/ walkthroughs/ blueprints/
        {
            var doc = JsonNode.Parse(File.ReadAllText(file))!;
            CheckIssuanceVct(doc, file, violations);     // credentialIssuanceConfig.vct == Canonical[knownType]
            CheckRequirementTypes(doc, file, violations); // credentialRequirements[].type: if a URI or a known bare name, must equal a Canonical value
        }
        violations.Should().BeEmpty(because:
            "every credential type must use its canonical VctUris URI:\n" + string.Join("\n", violations));
    }
}
```

- [ ] **Step 3: Run — verify it fails**, listing every unconverted site. This list is your worklist.

- [ ] **Step 4: Convert each blueprint** from the Step 3 list. For each `credentialIssuanceConfig`:
  - keep `credentialType` as-is (fallback + readable id),
  - add `"vct": "<canonical URI>"`,
  - add `"displayName": "<label from §6>"`.
  For each `credentialRequirements[].type` that names a platform type, replace the bare name with the canonical URI.

Re-run the conformance test after each file (or each type) and watch violations shrink to zero.

- [ ] **Step 5: Run — verify the conformance test passes.** Then rebuild + run the **full** affected test suites (Engine, Wallet.Service, UI.Core, Wallet.Pwa) — several existing walkthrough/integration tests assert bare-name types; update them to the canonical URIs (they are the same conversion). Baselines first; every change is the mechanical bare-name→URI swap.

- [ ] **Step 6: Commit** (stage the blueprint files + the test + any updated existing tests explicitly — this is the one task most at risk of `git add -A`; list paths)
```bash
git add tests/.../BlueprintVctConformanceTests.cs \
        demos/... walkthroughs/... blueprints/... <updated existing tests>
git commit -m "feat: [vct] convert every credential type to its canonical VctUris URI + displayName"
```

---

## Task 9: End-to-end regression — the reported bug

**Files:**
- Test: `tests/Sorcha.Wallet.Pwa.Tests/...` (or the DCQL/verifier test project where `PresentationEngine` matching is covered)

- [ ] **Step 1: Write the test that reproduces "None of your credentials match"**

Mint (or fixture) an Assured-Identity credential with `vct = VctUris.AssuredIdentityV1`; build the `DefaultPresetCatalogue` "confirm-identity"/"age-over-18" request (which now sources `VctUris.AssuredIdentityV1`); run `PresentationEngine.MatchQuery`; assert `Satisfiable == true` and the credential is a candidate.

```csharp
[Fact]
public void MatchQuery_AssuredIdentityCredential_SatisfiesAssuredIdentityPreset()
{
    var cred = CachedCredentialWithVct(VctUris.AssuredIdentityV1);
    var query = DcqlFromPreset(DefaultPresetCatalogue.ConfirmIdentity());  // match real API
    var result = _engine.MatchQuery(query, new[] { cred });
    result.Satisfiable.Should().BeTrue();
}
```

- [ ] **Step 2: Run — verify it passes** (Tasks 1-8 make it green). If it fails, the wiring from constant → preset → engine has a gap — fix before proceeding.

- [ ] **Step 3: Commit**
```bash
git add <test file>
git commit -m "test: [vct] Assured Identity credential satisfies the verifier preset (regression)"
```

---

## Task 10: Docs sync

- [ ] **Step 1:** Update `.claude/skills/verifiable-credentials/SKILL.md` and `.claude/skills/blueprint-builder/SKILL.md`: `credentialIssuanceConfig` now carries `vct` (canonical URI) + `displayName`; `credentialType` is a fallback/short-name; SD-JWT VC carries `vct` only (no `type` claim); VCT matching is case-sensitive; new blueprints use `VctUris` + a canonical URI. Update the example JSON blocks (they currently show bare `credentialType`).
- [ ] **Step 2:** If `docs/reference/API-DOCUMENTATION.md` documents the issue endpoint's `credentialType`, add `vct`.
- [ ] **Step 3: Commit**
```bash
git add .claude/skills/verifiable-credentials/SKILL.md .claude/skills/blueprint-builder/SKILL.md docs/reference/API-DOCUMENTATION.md
git commit -m "docs: [vct] canonical vct + displayName; vct-only, case-sensitive"
```

---

## Self-Review

**Spec coverage:** §2 fields → Task 2. §2.1 vct-only → Task 3. §3 display carrier → Tasks 3-4. §4 case-sensitive → Task 5. §5 VctUris + conformance → Tasks 1, 7, 8. §6 full conversion → Task 8. §7 publish validation → Task 6. §8 re-issue (no code — a runtime reality, covered by the design note). §10 tests → each task + Task 9. §12 standards → Tasks 3, 5. Docs → Task 10.

**Known reads-before-edit (honest):** Tasks 3 (display-config threading, `IssueCredentialRequest`), 4 (`BuildPayload`, `ToCachedCredential`, `CachedCredentialPayload`), 6 (validation seam) require reading the exact current method bodies — the plan gives the precise file:line and the transformation, and the failing test pins the outcome. This is intended: the surrounding code must be read, not guessed.

**Ordering:** 1 (constants) → 2 (fields) → 3 (issuer) → 4 (display) → 5 (matcher) → 6 (validation) → 7 (presets) → 8 (blueprints + conformance) → 9 (e2e) → 10 (docs). Task 8's conformance test depends on Task 1's constants; Task 9 depends on 7 + 8.

**The completeness guarantee:** Task 8's parametrised conformance test is what makes "convert *every* type in lockstep" verifiable rather than hopeful — a missed issuer or requirer is a red test, not a silent phone-only no-match.
