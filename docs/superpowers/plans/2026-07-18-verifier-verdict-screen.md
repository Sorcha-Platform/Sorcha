# Verifier Verdict Screen Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Replace the bare "Verification Complete" card on both the web desk verifier and the PWA doorstep verifier with the rich, preset-adaptive `VerdictTrailPanel` (portrait + verdict lead, four-layer trust trail a tap below), driven by a real `VerificationOutcome`, and make AIAS issue an `age_over_18` boolean so the "Age over 18?" preset has a claim to match.

**Architecture:** Both live verify screens share the HAIP transport (`IVerificationTransport` → `VerificationSessionQr`). HAIP verifies server-side and its `/result` endpoint already returns the authoritative `VerificationResult` (isValid, verifiedClaims, errors) plus the raw vp_token. We surface that as a `VerificationOutcome` (with a four-layer trail synthesised from HAIP's real result) through the transport seam, hand it to `VerdictViewModel.From(preset, outcome)`, and render the shared `VerdictTrailPanel` on both hosts. AIAS's `age_over_18` is derived from `dateOfBirth` at issue time in the single live claim-assembly site (`ActionExecutionService.IssueCredentialFromActionAsync`), driven by an explicit `age_over_18` claim mapping in the blueprint.

**Tech Stack:** .NET 10 / C# 14, Blazor (Server for `Sorcha.Verifier`, WASM for `Sorcha.Wallet.Pwa`), MudBlazor 9.2.0 + CSS isolation, xUnit v3 + FluentAssertions + Moq + bUnit, `System.Text.Json` (JsonElement, never JsonNode — WASM-safe).

## Global Constraints

- **License header on every new `.cs`:** `// SPDX-License-Identifier: MIT` then `// Copyright (c) 2026 Sorcha Contributors`. Every new `.razor` gets the same two lines inside a leading `@* … *@` block. File-scoped namespaces.
- **`Sorcha.UI.Components.User` RootNamespace is `Sorcha.UI.Core`** — files under `Components.User/...` folders declare namespaces rooted at `Sorcha.UI.Core` (e.g. `Sorcha.UI.Core.Components.Verify`), NOT `Sorcha.UI.Components.User.*`. The audience folder is filesystem metadata only. (Models keep their existing `Sorcha.UI.Components.User.Models.Verification` namespace — match the file already there; do not "fix" it.)
- **WASM-safe** in `Sorcha.UI.Components.User`, `Sorcha.Verifier.Engine`, and `Sorcha.Wallet.Pwa`: BCL + `System.Text.Json` only. `JsonElement`, never `JsonNode`. No server-only types, no `HttpClient` inside the engine.
- **No `ISnackbar`** anywhere (CI gate `scripts/check-no-snackbar.ps1`). User feedback via `IInlineFeedback`; but these verdict screens render inline content, not toasts — no feedback surface needed.
- **`dotnet build` before `dotnet test`.** `dotnet test` takes ONE project path; `--filter` does NOT isolate under Microsoft.Testing.Platform — run the whole project and read the `Failed: N, Passed: N` line. **Record each test project's baseline pass/fail counts before editing it** and compare after.
- **Never `git add -A`.** The working tree carries the user's untracked work (`walkthroughs/_storyboards/`, `tests/Sorcha.UI.E2E.Tests/Docker/StoryboardWalkthroughTests.cs`, modified `.gitignore`). Stage only the explicit paths each task names.
- **Branch:** all work on `feature/verifier-verdict-screen` (already checked out). Do NOT create a new branch. Commit after each task with `git add <explicit paths>`.
- **Test naming:** `MethodName_Scenario_ExpectedBehavior`.
- **Unified versioning:** never add `<Version>` to any `.csproj`.

## Key existing types (consumed across tasks — exact shapes)

From `Sorcha.Verifier.Engine.Models` (`src/Common/Sorcha.Verifier.Engine/Models/VerifierSession.cs`):

```csharp
public enum IssuerSignatureStatus { NotVerified, Verified }
public enum ValidationLayer { LivePresentation, IssuerSignature, Revocation, RegisterAnchor }
public enum LayerStatus { Pass, Fail, Unverified }

public sealed record ValidationLayerResult
{
    public required ValidationLayer Layer { get; init; }
    public required LayerStatus Status { get; init; }
    public required string Headline { get; init; }
    public IReadOnlyDictionary<string, string> Detail { get; init; } = new Dictionary<string, string>();
}

public sealed record VerificationOutcome
{
    public required bool Accepted { get; init; }
    public required IReadOnlyDictionary<string, object?> DisclosedClaims { get; init; }
    public required IReadOnlyList<string> Errors { get; init; }
    public required DateTimeOffset CompletedAt { get; init; }
    public IssuerSignatureStatus IssuerSignature { get; init; } = IssuerSignatureStatus.NotVerified;
    public IReadOnlyList<ValidationLayerResult> Layers { get; init; } = [];
}
```

From `Sorcha.UI.Components.User.Models.Verification` (`VerificationPreset.cs`) — a `sealed record`:

```csharp
public sealed record VerificationPreset(
    string Key, string Label, string Purpose, string RequiredVct,
    IReadOnlyList<string> RequiredClaims, IReadOnlyList<string> OptionalClaims,
    IReadOnlyList<string> KnownCredentialClaims);
```

`VerdictViewModel.From(VerificationPreset question, VerificationOutcome outcome)` (`Models/Verification/VerdictViewModel.cs`) already builds headline/issuer/portrait/AgeOver18/Disclosed/Withheld/Layers/RegisterAnchorId/CredentialId. It reads `IssuerDid`/`CredentialId` from the `IssuerSignature` layer's `Detail["iss"]`/`Detail["jti"]`, and `RegisterAnchorId` from the disclosed `registerAnchor` claim.

---

## Task 1: `age_over_18` derivation at AIAS issuance

**Why first:** it is independent of the UI work, unblocks the "Age over 18?" screen's data, and is the smallest self-contained deliverable.

**Files:**
- Create: `src/Services/Sorcha.Blueprint.Service/Services/Implementation/AgeClaimDeriver.cs`
- Modify: `src/Services/Sorcha.Blueprint.Service/Services/Implementation/ActionExecutionService.cs` (inside `IssueCredentialFromActionAsync`, immediately after the `var claims = BuildClaimsFromMappings(...)` line ~2366)
- Modify: `demos/AIAS/blueprints/aias-assured-identity.template.json` (add `age_over_18` claim mapping + disclosable entry)
- Test: `tests/Sorcha.Blueprint.Service.Tests/Services/AgeClaimDeriverTests.cs`

**Interfaces:**
- Produces: `AgeClaimDeriver.TryDeriveAgeOver(string? dateOfBirth, DateOnly today, int threshold, out bool isOver)` — returns `false` (and does not set a meaningful `isOver`) when `dateOfBirth` is null/unparseable; returns `true` with the computed boolean otherwise. `AgeClaimDeriver.AgeOverClaimThreshold(string claimName, out int threshold)` — matches `^age_over_(\d+)$`, extracts NN.

- [ ] **Step 1: Record the test-project baseline**

Run: `cd C:/Projects/Sorcha && dotnet build tests/Sorcha.Blueprint.Service.Tests/Sorcha.Blueprint.Service.Tests.csproj`
Then: `dotnet test tests/Sorcha.Blueprint.Service.Tests/Sorcha.Blueprint.Service.Tests.csproj`
Record the `Failed: N, Passed: N` line. (If the project name differs, find it: `ls tests | grep -i blueprint.service`.)

- [ ] **Step 2: Write the failing test**

Create `tests/Sorcha.Blueprint.Service.Tests/Services/AgeClaimDeriverTests.cs`:

```csharp
// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

using FluentAssertions;
using Sorcha.Blueprint.Service.Services.Implementation;
using Xunit;

namespace Sorcha.Blueprint.Service.Tests.Services;

public class AgeClaimDeriverTests
{
    private static readonly DateOnly Today = new(2026, 7, 18);

    [Theory]
    [InlineData("2000-05-01", true)]   // 26 — clearly over
    [InlineData("2008-07-17", true)]   // turned 18 yesterday
    [InlineData("2008-07-18", true)]   // 18th birthday today — is over
    [InlineData("2008-07-19", false)]  // 18th birthday tomorrow — not yet
    [InlineData("2020-01-01", false)]  // 6 — under
    public void TryDeriveAgeOver_18_ComputesFromDateOfBirth(string dob, bool expected)
    {
        var ok = AgeClaimDeriver.TryDeriveAgeOver(dob, Today, 18, out var isOver);
        ok.Should().BeTrue();
        isOver.Should().Be(expected);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("not-a-date")]
    [InlineData("2000-13-40")]
    public void TryDeriveAgeOver_UnparseableDob_ReturnsFalse(string? dob)
    {
        AgeClaimDeriver.TryDeriveAgeOver(dob, Today, 18, out _).Should().BeFalse();
    }

    [Theory]
    [InlineData("age_over_18", true, 18)]
    [InlineData("age_over_21", true, 21)]
    [InlineData("age_over_5", true, 5)]
    [InlineData("fullName", false, 0)]
    [InlineData("age_over_", false, 0)]
    [InlineData("ageOver18", false, 0)]
    public void AgeOverClaimThreshold_MatchesPattern(string claim, bool matches, int expected)
    {
        AgeClaimDeriver.AgeOverClaimThreshold(claim, out var t).Should().Be(matches);
        if (matches) t.Should().Be(expected);
    }
}
```

- [ ] **Step 3: Run the test to verify it fails**

Run: `dotnet build tests/Sorcha.Blueprint.Service.Tests/Sorcha.Blueprint.Service.Tests.csproj`
Expected: FAIL to compile — `AgeClaimDeriver` does not exist.

- [ ] **Step 4: Write the implementation**

Create `src/Services/Sorcha.Blueprint.Service/Services/Implementation/AgeClaimDeriver.cs`:

```csharp
// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

using System.Globalization;
using System.Text.RegularExpressions;

namespace Sorcha.Blueprint.Service.Services.Implementation;

/// <summary>
/// Derives EUDI / ISO 18013-5 style <c>age_over_NN</c> boolean claims from a
/// <c>dateOfBirth</c> at credential issue time. Issuing a boolean threshold instead of the
/// birth date is the privacy-preserving pattern the verifier "Age over 18?" preset consumes —
/// the holder proves the threshold without disclosing their date of birth or exact age.
/// </summary>
public static partial class AgeClaimDeriver
{
    [GeneratedRegex(@"^age_over_(\d+)$", RegexOptions.CultureInvariant)]
    private static partial Regex AgeOverPattern();

    /// <summary>
    /// True when <paramref name="claimName"/> is an <c>age_over_NN</c> claim, yielding the threshold NN.
    /// </summary>
    public static bool AgeOverClaimThreshold(string claimName, out int threshold)
    {
        threshold = 0;
        if (string.IsNullOrEmpty(claimName)) return false;
        var m = AgeOverPattern().Match(claimName);
        if (!m.Success) return false;
        return int.TryParse(m.Groups[1].Value, NumberStyles.None, CultureInfo.InvariantCulture, out threshold);
    }

    /// <summary>
    /// Computes whether the holder is at least <paramref name="threshold"/> years old as of
    /// <paramref name="today"/>. Returns <c>false</c> (fail-closed — no claim should be issued)
    /// when the date of birth is null, empty, or not an ISO <c>yyyy-MM-dd</c> date.
    /// </summary>
    public static bool TryDeriveAgeOver(string? dateOfBirth, DateOnly today, int threshold, out bool isOver)
    {
        isOver = false;
        if (string.IsNullOrWhiteSpace(dateOfBirth)) return false;
        if (!DateOnly.TryParseExact(dateOfBirth, "yyyy-MM-dd", CultureInfo.InvariantCulture,
                DateTimeStyles.None, out var dob))
            return false;

        var age = today.Year - dob.Year;
        if (dob > today.AddYears(-age)) age--;   // birthday not yet reached this year
        isOver = age >= threshold;
        return true;
    }
}
```

- [ ] **Step 5: Run the test to verify it passes**

Run: `dotnet build tests/Sorcha.Blueprint.Service.Tests/Sorcha.Blueprint.Service.Tests.csproj && dotnet test tests/Sorcha.Blueprint.Service.Tests/Sorcha.Blueprint.Service.Tests.csproj`
Expected: baseline `Passed` count + new tests, `Failed` unchanged from baseline.

- [ ] **Step 6: Wire the derivation into the live issuance path**

In `src/Services/Sorcha.Blueprint.Service/Services/Implementation/ActionExecutionService.cs`, find `IssueCredentialFromActionAsync`. Immediately after the line `var claims = BuildClaimsFromMappings(config.ClaimMappings, mergedData!, warnings);` (~line 2366), insert:

```csharp
            // Derive age_over_NN booleans from dateOfBirth for any age-threshold claim the blueprint
            // maps (e.g. { "claimName": "age_over_18", "sourceField": "/dob/dateOfBirth" }). Issuing the
            // boolean instead of the raw date is the EUDI/ISO 18013-5 minimal-disclosure pattern the
            // verifier "Age over 18?" preset matches. Fail-closed: if the DOB is missing/unparseable the
            // claim is omitted rather than defaulted.
            var ageToday = DateOnly.FromDateTime(DateTime.UtcNow);
            foreach (var mapping in config.ClaimMappings)
            {
                if (!AgeClaimDeriver.AgeOverClaimThreshold(mapping.ClaimName, out var threshold))
                    continue;

                var dobString = TryResolveJsonPointer(mergedData!, mapping.SourceField, out var dobValue)
                    ? dobValue?.ToString()
                    : null;

                if (AgeClaimDeriver.TryDeriveAgeOver(dobString, ageToday, threshold, out var isOver))
                {
                    claims[mapping.ClaimName] = isOver;
                }
                else
                {
                    claims.Remove(mapping.ClaimName);   // drop the raw-date copy BuildClaimsFromMappings made
                    warnings.Add($"[WARN_CRED_AGE_DERIVE] {mapping.ClaimName}: dateOfBirth missing or unparseable; claim omitted.");
                }
            }
```

Notes for the implementer:
- `TryResolveJsonPointer` is a private static in this same class — call it directly.
- `config` is `actionDef.CredentialIssuanceConfig!`, already assigned in this method as `var config`. `warnings` is the `ICollection<string>` parameter. If `mergedData` is a `Dictionary<string, object>` (not nullable) in this method's signature, drop the `!`.
- `BuildClaimsFromMappings` will have copied the raw date string into `claims["age_over_18"]` first (because the mapping's sourceField resolves to the date); this loop overwrites it with the boolean, or removes it on failure.

- [ ] **Step 7: Add the age claim mapping + disclosable entry to the AIAS blueprint template**

In `demos/AIAS/blueprints/aias-assured-identity.template.json`, inside action 2's `credentialIssuanceConfig.claimMappings` array, add after the `dateOfBirth` mapping:

```json
    { "claimName": "age_over_18", "sourceField": "/dob/dateOfBirth" },
```

And in the same config's `disclosable` array, add `"age_over_18"` (place it right after `"dateOfBirth"`):

```json
    "dateOfBirth", "age_over_18", "email",
```

(Keep the rest of both arrays unchanged. `dateOfBirth` stays disclosable so "Confirm identity" can still optionally reveal it; `age_over_18` is what the age preset discloses.)

- [ ] **Step 8: Build the service and confirm no regressions**

Run: `dotnet build src/Services/Sorcha.Blueprint.Service/Sorcha.Blueprint.Service.csproj`
Then re-run the test project from Step 5. Expected: compiles clean; `Failed` count unchanged from baseline.

- [ ] **Step 9: Commit**

```bash
git add src/Services/Sorcha.Blueprint.Service/Services/Implementation/AgeClaimDeriver.cs \
        src/Services/Sorcha.Blueprint.Service/Services/Implementation/ActionExecutionService.cs \
        demos/AIAS/blueprints/aias-assured-identity.template.json \
        tests/Sorcha.Blueprint.Service.Tests/Services/AgeClaimDeriverTests.cs
git commit -m "feat: [#174] derive age_over_18 at AIAS issuance from dateOfBirth"
```

---

## Task 2: Surface the HAIP verification result as a `VerificationOutcome` through the transport

**Why:** the two host screens currently receive only a `vp_token` string and discard the verdict. HAIP's `/result` already returns the authoritative `VerificationResult` (isValid, verifiedClaims, errors). This task threads that through the transport seam as a `VerificationOutcome` (with a four-layer trail synthesised from HAIP's real result), so both hosts can render the panel from one place.

**Files:**
- Create: `src/Apps/Sorcha.UI/Sorcha.UI.Components.User/Services/User/Verification/HaipOutcomeMapper.cs`
- Modify: `src/Apps/Sorcha.UI/Sorcha.UI.Components.User/Services/User/Verification/IHaipVerifierClient.cs` (extend `HaipPollResult`)
- Modify: `src/Apps/Sorcha.UI/Sorcha.UI.Components.User/Services/User/Verification/HaipVerifierClient.cs` (read the nested `result` object)
- Modify: `src/Apps/Sorcha.UI/Sorcha.UI.Components.User/Services/User/Verification/IVerificationTransport.cs` (add `Outcome` to `VerificationSessionPoll`)
- Modify: `src/Apps/Sorcha.UI/Sorcha.UI.Components.User/Services/User/Verification/HaipVerificationTransport.cs` (populate `Outcome`)
- Modify: `src/Apps/Sorcha.UI/Sorcha.UI.Components.User/Services/User/Verification/NotConfiguredVerificationTransport.cs` (null `Outcome`)
- Test: `tests/Sorcha.UI.Components.User.Tests/Services/Verification/HaipOutcomeMapperTests.cs`
- Test: `tests/Sorcha.UI.Components.User.Tests/Services/Verification/HaipVerificationTransportTests.cs` (extend)

**Interfaces:**
- Produces: `HaipOutcomeMapper.Map(bool accepted, IReadOnlyDictionary<string, object?> disclosedClaims, IReadOnlyList<string> errors, bool holderKeyVerified, string? vpToken, DateTimeOffset completedAt) : VerificationOutcome`.
- Produces: `HaipPollResult(string State, string? VpToken, string? PresentationSubmission, bool? IsValid, IReadOnlyDictionary<string, object?>? VerifiedClaims, IReadOnlyList<string>? Errors, bool HolderKeyVerified)`.
- Produces: `VerificationSessionPoll(bool IsComplete, string? VpToken, string? PresentationSubmission, bool IsTerminal, VerificationOutcome? Outcome)`.
- Consumed by: Task 4 (`VerificationSessionQr`) reads `poll.Outcome`.

- [ ] **Step 1: Record the test-project baseline**

Run: `dotnet build tests/Sorcha.UI.Components.User.Tests/Sorcha.UI.Components.User.Tests.csproj && dotnet test tests/Sorcha.UI.Components.User.Tests/Sorcha.UI.Components.User.Tests.csproj`
Record `Failed: N, Passed: N`.

- [ ] **Step 2: Write the failing mapper test**

Create `tests/Sorcha.UI.Components.User.Tests/Services/Verification/HaipOutcomeMapperTests.cs`:

```csharp
// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

using FluentAssertions;
using Sorcha.UI.Components.User.Services.Verification;
using Sorcha.Verifier.Engine.Models;
using Xunit;

namespace Sorcha.UI.Components.User.Tests.Services.Verification;

public class HaipOutcomeMapperTests
{
    private static readonly DateTimeOffset At = new(2026, 7, 18, 9, 0, 0, TimeSpan.Zero);

    [Fact]
    public void Map_AcceptedResult_ProducesPassOutcomeWithThreeLayers()
    {
        var claims = new Dictionary<string, object?> { ["fullName"] = "Stuart Fraser", ["age_over_18"] = true };

        var outcome = HaipOutcomeMapper.Map(
            accepted: true, disclosedClaims: claims, errors: [], holderKeyVerified: true,
            vpToken: null, completedAt: At);

        outcome.Accepted.Should().BeTrue();
        outcome.DisclosedClaims.Should().ContainKey("fullName");
        outcome.IssuerSignature.Should().Be(IssuerSignatureStatus.Verified);
        outcome.Layers.Should().HaveCount(3);
        outcome.Layers.Should().OnlyContain(l => l.Status == LayerStatus.Pass);
        outcome.Layers.Select(l => l.Layer).Should().BeEquivalentTo(new[]
        {
            ValidationLayer.LivePresentation, ValidationLayer.IssuerSignature, ValidationLayer.Revocation
        });
    }

    [Fact]
    public void Map_RejectedResult_ProducesFailOutcomeAndCarriesErrors()
    {
        var outcome = HaipOutcomeMapper.Map(
            accepted: false, disclosedClaims: new Dictionary<string, object?>(),
            errors: ["nonce mismatch"], holderKeyVerified: false, vpToken: null, completedAt: At);

        outcome.Accepted.Should().BeFalse();
        outcome.Errors.Should().Contain("nonce mismatch");
        outcome.Layers.First(l => l.Layer == ValidationLayer.LivePresentation).Status
            .Should().Be(LayerStatus.Fail);
    }

    [Fact]
    public void Map_ParsesIssuerAndJtiFromVpToken_ForTheTrailAndAnchorLookup()
    {
        // header {"alg":"EdDSA"} . payload {"iss":"did:sorcha:org:ws1qabc","jti":"cred-123"} . sig
        const string header = "eyJhbGciOiJFZERTQSJ9";
        const string payload = "eyJpc3MiOiJkaWQ6c29yY2hhOm9yZzp3czFxYWJjIiwianRpIjoiY3JlZC0xMjMifQ";
        var vp = $"{header}.{payload}.sig~";

        var outcome = HaipOutcomeMapper.Map(
            accepted: true, disclosedClaims: new Dictionary<string, object?>(),
            errors: [], holderKeyVerified: true, vpToken: vp, completedAt: At);

        var issuerLayer = outcome.Layers.First(l => l.Layer == ValidationLayer.IssuerSignature);
        issuerLayer.Detail.Should().ContainKey("iss").WhoseValue.Should().Be("did:sorcha:org:ws1qabc");
        issuerLayer.Detail.Should().ContainKey("jti").WhoseValue.Should().Be("cred-123");
    }
}
```

- [ ] **Step 3: Run to verify it fails**

Run: `dotnet build tests/Sorcha.UI.Components.User.Tests/Sorcha.UI.Components.User.Tests.csproj`
Expected: FAIL to compile — `HaipOutcomeMapper` does not exist.

- [ ] **Step 4: Write the mapper**

Create `src/Apps/Sorcha.UI/Sorcha.UI.Components.User/Services/User/Verification/HaipOutcomeMapper.cs`:

```csharp
// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

using System.Text.Json;
using Sorcha.Verifier.Engine.Models;

namespace Sorcha.UI.Components.User.Services.Verification;

/// <summary>
/// Maps HAIP's authoritative server-side verification result into the engine's
/// <see cref="VerificationOutcome"/> so the shared VerdictTrailPanel can render the four-layer
/// trust trail. HAIP verifies the presentation online (with the real nonce and issuer-key
/// resolution), so an accepted online result means every offline layer passed and the issuer
/// signature was verified. The register-anchor (layer 4) is appended on demand by the panel.
/// WASM-safe — System.Text.Json only.
/// </summary>
public static class HaipOutcomeMapper
{
    /// <summary>Builds a <see cref="VerificationOutcome"/> from HAIP's poll result.</summary>
    public static VerificationOutcome Map(
        bool accepted,
        IReadOnlyDictionary<string, object?> disclosedClaims,
        IReadOnlyList<string> errors,
        bool holderKeyVerified,
        string? vpToken,
        DateTimeOffset completedAt)
    {
        var live = accepted && holderKeyVerified ? LayerStatus.Pass : LayerStatus.Fail;
        var issuer = accepted ? LayerStatus.Pass : LayerStatus.Fail;
        var revocation = accepted ? LayerStatus.Pass : LayerStatus.Fail;

        var (iss, jti) = ExtractIssuerAndJti(vpToken);

        var issuerDetail = new Dictionary<string, string>();
        if (!string.IsNullOrEmpty(iss)) issuerDetail["iss"] = iss!;
        if (!string.IsNullOrEmpty(jti)) issuerDetail["jti"] = jti!;

        var layers = new List<ValidationLayerResult>
        {
            new()
            {
                Layer = ValidationLayer.LivePresentation,
                Status = live,
                Headline = live == LayerStatus.Pass ? "Proved on the holder's own device" : "Live presentation failed",
            },
            new()
            {
                Layer = ValidationLayer.IssuerSignature,
                Status = issuer,
                Headline = issuer == LayerStatus.Pass ? "Signed by the issuer" : "Issuer signature not verified",
                Detail = issuerDetail,
            },
            new()
            {
                Layer = ValidationLayer.Revocation,
                Status = revocation,
                Headline = revocation == LayerStatus.Pass ? "Checked against the issuer's status list" : "Revocation check failed",
            },
        };

        return new VerificationOutcome
        {
            Accepted = accepted,
            DisclosedClaims = disclosedClaims,
            Errors = errors,
            CompletedAt = completedAt,
            // HAIP online verification requires and resolves the issuer signature; an accepted result is Verified.
            IssuerSignature = accepted ? IssuerSignatureStatus.Verified : IssuerSignatureStatus.NotVerified,
            Layers = layers,
        };
    }

    private static (string? iss, string? jti) ExtractIssuerAndJti(string? vpToken)
    {
        if (string.IsNullOrWhiteSpace(vpToken)) return (null, null);
        try
        {
            // SD-JWT VC compact form: <issuer-jwt>~<disclosure>~...~<kb-jwt>. The issuer JWT is first.
            var jwt = vpToken.Split('~', 2)[0];
            var parts = jwt.Split('.');
            if (parts.Length < 2) return (null, null);
            var payloadJson = Base64UrlDecode(parts[1]);
            using var doc = JsonDocument.Parse(payloadJson);
            var root = doc.RootElement;
            var iss = root.TryGetProperty("iss", out var i) ? i.GetString() : null;
            var jti = root.TryGetProperty("jti", out var j) ? j.GetString() : null;
            return (iss, jti);
        }
        catch
        {
            return (null, null);
        }
    }

    private static string Base64UrlDecode(string input)
    {
        var s = input.Replace('-', '+').Replace('_', '/');
        switch (s.Length % 4) { case 2: s += "=="; break; case 3: s += "="; break; }
        return System.Text.Encoding.UTF8.GetString(Convert.FromBase64String(s));
    }
}
```

- [ ] **Step 5: Run the mapper test to verify it passes**

Run: `dotnet build tests/Sorcha.UI.Components.User.Tests/Sorcha.UI.Components.User.Tests.csproj && dotnet test tests/Sorcha.UI.Components.User.Tests/Sorcha.UI.Components.User.Tests.csproj`
Expected: the three new mapper tests pass; `Failed` unchanged from baseline.

- [ ] **Step 6: Extend `HaipPollResult` and read HAIP's nested `result`**

In `IHaipVerifierClient.cs`, replace the `HaipPollResult` record with:

```csharp
/// <summary>Result of polling a HAIP verification request.</summary>
/// <param name="State">Server-side state string (Pending / Submitted / Verified / Denied / Expired / Cancelled).</param>
/// <param name="VpToken">The raw vp_token when submitted; null otherwise.</param>
/// <param name="PresentationSubmission">The OID4VP presentation_submission, when present.</param>
/// <param name="IsValid">HAIP's authoritative validity, when a result object is present.</param>
/// <param name="VerifiedClaims">Disclosed claim name → value, from HAIP's verified result.</param>
/// <param name="Errors">HAIP's rejection reasons, when present.</param>
/// <param name="HolderKeyVerified">Whether HAIP verified the holder key binding.</param>
public sealed record HaipPollResult(
    string State,
    string? VpToken,
    string? PresentationSubmission,
    bool? IsValid = null,
    IReadOnlyDictionary<string, object?>? VerifiedClaims = null,
    IReadOnlyList<string>? Errors = null,
    bool HolderKeyVerified = false);
```

In `HaipVerifierClient.cs`, replace the `PollResultDto` and the `PollResultAsync` return construction. The `/result` payload is `{ requestId, state, result: { isValid, verifiedClaims, errors, holderKeyVerified }, vpToken, presentationSubmission }` (`result` is null pre-submission).

```csharp
    /// <inheritdoc />
    public async Task<HaipPollResult> PollResultAsync(string requestId, CancellationToken ct = default)
    {
        var url = $"/api/v1/verifier/requests/{Uri.EscapeDataString(requestId)}/result";
        using var response = await _http.GetAsync(url, ct);

        if (response.StatusCode == System.Net.HttpStatusCode.Gone)
            return new HaipPollResult("Expired", null, null);

        response.EnsureSuccessStatusCode();

        var result = await response.Content.ReadFromJsonAsync<PollResultDto>(JsonDefaults.Api, ct)
            ?? throw new InvalidOperationException("HAIP verifier returned an empty poll response.");

        IReadOnlyDictionary<string, object?>? claims = null;
        if (result.Result?.VerifiedClaims is { Count: > 0 } vc)
        {
            claims = vc.ToDictionary(kvp => kvp.Key, kvp => JsonElementToObject(kvp.Value));
        }

        return new HaipPollResult(
            result.State ?? "Pending",
            result.VpToken,
            result.PresentationSubmission,
            IsValid: result.Result?.IsValid,
            VerifiedClaims: claims,
            Errors: result.Result?.Errors,
            HolderKeyVerified: result.Result?.HolderKeyVerified ?? false);
    }

    private static object? JsonElementToObject(JsonElement e) => e.ValueKind switch
    {
        JsonValueKind.True => true,
        JsonValueKind.False => false,
        JsonValueKind.String => e.GetString(),
        JsonValueKind.Number => e.TryGetInt64(out var l) ? l : e.GetDouble(),
        JsonValueKind.Null or JsonValueKind.Undefined => null,
        _ => e.GetRawText(),
    };

    private sealed record CreateRequestDto(
        [property: JsonPropertyName("credentialType")] string CredentialType,
        [property: JsonPropertyName("requiredClaims")] IReadOnlyList<string> RequiredClaims);

    private sealed record CreateResultDto(
        [property: JsonPropertyName("requestId")] string? RequestId,
        [property: JsonPropertyName("authorizationRequestUri")] string? AuthorizationRequestUri);

    private sealed record PollResultDto(
        [property: JsonPropertyName("state")] string? State,
        [property: JsonPropertyName("vpToken")] string? VpToken,
        [property: JsonPropertyName("presentationSubmission")] string? PresentationSubmission,
        [property: JsonPropertyName("result")] VerificationResultDto? Result);

    private sealed record VerificationResultDto(
        [property: JsonPropertyName("isValid")] bool IsValid,
        [property: JsonPropertyName("verifiedClaims")] Dictionary<string, JsonElement>? VerifiedClaims,
        [property: JsonPropertyName("errors")] List<string>? Errors,
        [property: JsonPropertyName("holderKeyVerified")] bool HolderKeyVerified);
```

Add `using System.Text.Json;` at the top of `HaipVerifierClient.cs` if not present (it already uses `System.Text.Json` — confirm).

- [ ] **Step 7: Add `Outcome` to `VerificationSessionPoll`**

In `IVerificationTransport.cs`, add the using and extend the record:

```csharp
using Sorcha.Verifier.Engine.Models;
```

```csharp
/// <summary>Result of polling a verification session.</summary>
/// <param name="IsComplete">True once the holder has submitted a presentation.</param>
/// <param name="VpToken">The raw submitted vp_token, or null while pending.</param>
/// <param name="PresentationSubmission">The OID4VP presentation_submission, when present.</param>
/// <param name="IsTerminal">True when the session has reached a non-resumable state.</param>
/// <param name="Outcome">
/// The verification verdict computed from the authoritative HAIP result, populated on completion.
/// Null while pending and for transports that do not produce a verdict (the not-configured stub).
/// </param>
public sealed record VerificationSessionPoll(
    bool IsComplete,
    string? VpToken,
    string? PresentationSubmission,
    bool IsTerminal = false,
    VerificationOutcome? Outcome = null);
```

- [ ] **Step 8: Populate `Outcome` in `HaipVerificationTransport.PollSessionAsync`**

In `HaipVerificationTransport.cs`, update `PollSessionAsync` (the public `IVerificationTransport` method, ~lines 59-71). It must call `PollAsync` and, on completion, map the underlying HAIP result. Because `PollAsync` currently maps to a `VerificationSession` that drops the claims, change `PollSessionAsync` to call `_client.PollResultAsync` directly (or thread the claims through). Simplest: reshape `PollSessionAsync` to use the client result:

```csharp
    /// <inheritdoc />
    public async Task<VerificationSessionPoll> PollSessionAsync(
        string sessionId,
        CancellationToken ct = default)
    {
        HaipPollResult poll;
        try
        {
            poll = await _client.PollResultAsync(sessionId, ct);
        }
        catch (OperationCanceledException) { throw; }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Poll fault for session {SessionId}.", sessionId);
            return new VerificationSessionPoll(IsComplete: false, VpToken: null,
                PresentationSubmission: null, IsTerminal: true, Outcome: null);
        }

        var state = MapState(poll.State);
        var isComplete = state == VerificationSessionState.Complete;
        var isTerminal = state != VerificationSessionState.Pending;

        VerificationOutcome? outcome = null;
        if (isComplete)
        {
            outcome = HaipOutcomeMapper.Map(
                accepted: poll.IsValid ?? true,
                disclosedClaims: poll.VerifiedClaims ?? new Dictionary<string, object?>(),
                errors: poll.Errors ?? [],
                holderKeyVerified: poll.HolderKeyVerified,
                vpToken: poll.VpToken,
                completedAt: DateTimeOffset.UtcNow);
        }

        return new VerificationSessionPoll(
            IsComplete: isComplete,
            VpToken: poll.VpToken,
            PresentationSubmission: poll.PresentationSubmission,
            IsTerminal: isTerminal,
            Outcome: outcome);
    }
```

Keep the existing `StartAsync` / `PollAsync` / `MapState` helpers (other callers and tests use them). `MapState` is already private; reuse it.

- [ ] **Step 9: `NotConfiguredVerificationTransport` returns a null outcome**

In `NotConfiguredVerificationTransport.cs`, confirm its `PollSessionAsync` returns `new VerificationSessionPoll(false, null, null)` — the new optional `Outcome` defaults to null, so likely no change is needed. If it constructs the record positionally with all params, leave `Outcome` unset. Build to confirm.

- [ ] **Step 10: Extend the transport test to assert the outcome is surfaced**

In `tests/Sorcha.UI.Components.User.Tests/Services/Verification/HaipVerificationTransportTests.cs`, add a test that a completed poll surfaces a non-null `Outcome` with the disclosed claims. Mirror the existing `IHaipVerifierClient` mock/stub the file already uses (match its arrangement):

```csharp
    [Fact]
    public async Task PollSessionAsync_Verified_SurfacesOutcomeWithDisclosedClaims()
    {
        // Arrange: mock IHaipVerifierClient.PollResultAsync to return a Verified result with claims.
        // (Use the same mocking style already present in this file.)
        _client.Setup(c => c.PollResultAsync("req-1", It.IsAny<CancellationToken>()))
            .ReturnsAsync(new HaipPollResult(
                "Verified", "eyJ.eyJ.sig~", null,
                IsValid: true,
                VerifiedClaims: new Dictionary<string, object?> { ["fullName"] = "Stuart Fraser" },
                Errors: [],
                HolderKeyVerified: true));

        var poll = await _transport.PollSessionAsync("req-1");

        poll.IsComplete.Should().BeTrue();
        poll.Outcome.Should().NotBeNull();
        poll.Outcome!.Accepted.Should().BeTrue();
        poll.Outcome.DisclosedClaims.Should().ContainKey("fullName");
    }
```

If the existing tests construct `HaipVerificationTransport` with a concrete fake client rather than Moq, adapt: extend that fake's `PollResultAsync` to return the richer `HaipPollResult` and assert the same. Read the file first and match its fixtures.

- [ ] **Step 11: Build and run the whole test project**

Run: `dotnet build tests/Sorcha.UI.Components.User.Tests/Sorcha.UI.Components.User.Tests.csproj && dotnet test tests/Sorcha.UI.Components.User.Tests/Sorcha.UI.Components.User.Tests.csproj`
Expected: new tests pass; `Failed` unchanged from baseline. Also build the library: `dotnet build src/Apps/Sorcha.UI/Sorcha.UI.Components.User/Sorcha.UI.Components.User.csproj`.

- [ ] **Step 12: Commit**

```bash
git add src/Apps/Sorcha.UI/Sorcha.UI.Components.User/Services/User/Verification/HaipOutcomeMapper.cs \
        src/Apps/Sorcha.UI/Sorcha.UI.Components.User/Services/User/Verification/IHaipVerifierClient.cs \
        src/Apps/Sorcha.UI/Sorcha.UI.Components.User/Services/User/Verification/HaipVerifierClient.cs \
        src/Apps/Sorcha.UI/Sorcha.UI.Components.User/Services/User/Verification/IVerificationTransport.cs \
        src/Apps/Sorcha.UI/Sorcha.UI.Components.User/Services/User/Verification/HaipVerificationTransport.cs \
        src/Apps/Sorcha.UI/Sorcha.UI.Components.User/Services/User/Verification/NotConfiguredVerificationTransport.cs \
        tests/Sorcha.UI.Components.User.Tests/Services/Verification/HaipOutcomeMapperTests.cs \
        tests/Sorcha.UI.Components.User.Tests/Services/Verification/HaipVerificationTransportTests.cs
git commit -m "feat: [#174] surface HAIP verification result as VerificationOutcome through the transport"
```

---

## Task 3: Redesign `VerdictTrailPanel` to the approved mockup (identity + age treatments, fail/warn)

**Why:** the orphaned panel uses generic `MudExpansionPanels` and does not match the mockup (portrait-lead identity treatment, age hero, minimal-disclosure statement, single collapsed trust trail). This task rebuilds its markup + CSS and adds a preset treatment discriminator, then updates its bUnit tests.

**Files:**
- Modify: `src/Apps/Sorcha.UI/Sorcha.UI.Components.User/Models/Verification/VerdictViewModel.cs` (add `IsAgeTreatment` + `MinimalDisclosureNote`)
- Modify: `src/Apps/Sorcha.UI/Sorcha.UI.Components.User/Components/Verify/VerdictTrailPanel.razor` (rebuild markup)
- Create: `src/Apps/Sorcha.UI/Sorcha.UI.Components.User/Components/Verify/VerdictTrailPanel.razor.css` (isolated CSS from the mockup)
- Test: `tests/Sorcha.UI.Core.Tests/Verification/VerdictTrailPanelTests.cs` (rewrite for the new structure)

**Interfaces:**
- Consumes: `VerdictViewModel` (Task 2's `VerificationOutcome`).
- Produces: `VerdictViewModel.IsAgeTreatment` (bool) + `VerdictViewModel.MinimalDisclosureNote` (string?), and a `VerdictTrailPanel` with an optional `[Parameter] EventCallback OnVerifyAnother` (renders the foot when set). Stable `data-testid`s: `verdict-trail-panel`, `verdict-banner`, `age-hero`, `portrait`, `holder-name`, `disclosed-claim`, `withheld-claims`, `issuer-line`, `minimal-disclosure`, `verify-trail`, `trail-{Layer}` (e.g. `trail-LivePresentation`), `verify-anchor`, `anchor-na`, `verify-another`.

- [ ] **Step 1: Record the test-project baseline**

Run: `dotnet build tests/Sorcha.UI.Core.Tests/Sorcha.UI.Core.Tests.csproj && dotnet test tests/Sorcha.UI.Core.Tests/Sorcha.UI.Core.Tests.csproj`
Record `Failed: N, Passed: N`.

- [ ] **Step 2: Add the treatment discriminator to `VerdictViewModel`**

In `VerdictViewModel.cs`, add two properties and populate them in `From`:

```csharp
    /// <summary>True when the asked preset is an age-threshold check — drives the age hero treatment.</summary>
    public bool IsAgeTreatment { get; private init; }

    /// <summary>The minimal-disclosure statement shown on the age treatment; null for the identity treatment.</summary>
    public string? MinimalDisclosureNote { get; private init; }
```

In `From(...)`, compute the discriminator from the ASKED question (not from what happened to be disclosed) and set both on the constructed `vm`:

```csharp
        var isAge = question.RequiredClaims.Contains(AgeClaim)
                    && !question.RequiredClaims.Contains("fullName");
```

Then in the object initializer add:

```csharp
            IsAgeTreatment = isAge,
            MinimalDisclosureNote = isAge
                ? "You learned only that they're over 18 — and saw their photo to match the face. "
                  + "You did not learn their name, birth date, or exact age."
                : null,
```

(`AgeClaim` const `"age_over_18"` already exists in the file.)

- [ ] **Step 3: Rewrite the bUnit tests for the new structure**

Replace the body of `tests/Sorcha.UI.Core.Tests/Verification/VerdictTrailPanelTests.cs` test methods (keep the fixture helpers `AgePreset` / `BuildOutcomeWithThreeLayers`, and the `IRegisterAnchorClient` mock + `AddSorchaUserComponents` setup). Add/replace tests to assert the new structure. Read the file first to reuse its `BunitContext`, mock, and `Render<VerdictTrailPanel>` helpers, then write:

```csharp
    [Fact]
    public void AgeTreatment_LeadsWithHero_AndMinimalDisclosureNote()
    {
        var verdict = VerdictViewModel.From(AgePreset, BuildOutcomeWithThreeLayers());
        var cut = Render(verdict);

        cut.Find("[data-testid=age-hero]").TextContent.Should().Contain("Over 18");
        cut.Find("[data-testid=minimal-disclosure]").TextContent.Should().Contain("did not learn their name");
        cut.FindAll("[data-testid=holder-name]").Should().BeEmpty();   // age screen hides the name
    }

    [Fact]
    public void IdentityTreatment_LeadsWithPortraitAndName_AndWithheldLine()
    {
        var verdict = VerdictViewModel.From(IdentityPreset, BuildIdentityOutcome());
        var cut = Render(verdict);

        cut.Find("[data-testid=holder-name]").TextContent.Should().Contain("Stuart Fraser");
        cut.Find("[data-testid=portrait]").Should().NotBeNull();
        cut.Find("[data-testid=withheld-claims]").TextContent.Should().Contain("dateOfBirth");
        cut.FindAll("[data-testid=age-hero]").Should().BeEmpty();
    }

    [Fact]
    public void PassVerdict_ShowsPassBanner()
    {
        var verdict = VerdictViewModel.From(IdentityPreset, BuildIdentityOutcome());
        var cut = Render(verdict);
        cut.Find("[data-testid=verdict-banner]").GetAttribute("class").Should().Contain("verdict-pass");
    }

    [Fact]
    public void FailVerdict_ShowsFailBanner_AndDoesNotPresentDisclosedIdentityAsTrusted()
    {
        var verdict = VerdictViewModel.From(IdentityPreset, BuildRejectedOutcome());
        var cut = Render(verdict);
        cut.Find("[data-testid=verdict-banner]").GetAttribute("class").Should().Contain("verdict-fail");
    }

    [Fact]
    public void WarnVerdict_ShowsWarnBanner_NeverAPlainPass()
    {
        var verdict = VerdictViewModel.From(IdentityPreset, BuildWarnOutcome());
        var cut = Render(verdict);
        var cls = cut.Find("[data-testid=verdict-banner]").GetAttribute("class");
        cls.Should().Contain("verdict-warn");
        cls.Should().NotContain("verdict-pass");
    }

    [Fact]
    public void TrustTrail_RendersFourLayerRows_AndAnchorIsOnDemand()
    {
        var verdict = VerdictViewModel.From(AgePreset, BuildOutcomeWithThreeLayers());
        var cut = Render(verdict);
        cut.Find("[data-testid=trail-LivePresentation]").Should().NotBeNull();
        cut.Find("[data-testid=trail-IssuerSignature]").Should().NotBeNull();
        cut.Find("[data-testid=trail-Revocation]").Should().NotBeNull();
        // Anchor layer is the on-demand affordance until checked.
        AnchorClientMock.Verify(c => c.CheckAsync(It.IsAny<string>(), It.IsAny<string>(),
            It.IsAny<CancellationToken>()), Times.Never);
    }
```

Add the fixtures the new tests need (adapt from the existing `BuildOutcomeWithThreeLayers`):
- `IdentityPreset` — a `VerificationPreset("confirm-identity", "Confirm identity", "…", VctUris.AssuredIdentityV1, ["fullName","portrait"], ["dateOfBirth"], ["age_over_18","portrait","fullName","dateOfBirth"])`.
- `BuildIdentityOutcome()` — `Accepted:true`, `DisclosedClaims: { fullName="Stuart Fraser", portrait="<base64>" }`, three Pass layers, `IssuerSignature=Verified`.
- `BuildRejectedOutcome()` — `Accepted:false`, empty claims, `Errors:["nonce mismatch"]`, `LivePresentation=Fail`.
- `BuildWarnOutcome()` — `Accepted:true`, `IssuerSignature=NotVerified`, `IssuerSignature` layer `Status=Unverified`.

The existing `AgePreset` uses a fixture VCT; keep it. Preserve the existing anchor-click test (rename its assertions to the new testids if it referenced old ones).

- [ ] **Step 4: Run the tests to verify they fail**

Run: `dotnet build tests/Sorcha.UI.Core.Tests/Sorcha.UI.Core.Tests.csproj`
Expected: FAIL — new testids/`IsAgeTreatment` not yet rendered.

- [ ] **Step 5: Rebuild the panel markup**

Replace `VerdictTrailPanel.razor` markup (keep the `@code` block's parameters + anchor logic; add `OnVerifyAnother`). The new markup — banner/age-hero, portrait, chips, "Shared with you" card, minimal-disclosure (age), issuer line, collapsed `<details>` trust trail with the four layers, optional foot:

```razor
@*
    SPDX-License-Identifier: MIT
    Copyright (c) 2026 Sorcha Contributors

    Shared verdict panel (Feature 174). Renders a VerificationOutcome as a preset-adaptive verdict:
    the identity treatment leads with the portrait + name; the age treatment leads with the answer and
    a minimal-disclosure statement. Below, a collapsed four-layer trust trail (Live presentation ·
    Issuer signature · Not revoked · Register-anchored), with the register-anchor checked on demand.
*@
@namespace Sorcha.UI.Core.Components.Verify
@using Sorcha.UI.Components.User.Models.Verification
@using Sorcha.Verifier.Engine
@using Sorcha.Verifier.Engine.Models
@inject IRegisterAnchorClient AnchorClient

<div class="verdict-trail-panel" data-testid="verdict-trail-panel">

    @* ---- Verdict lead: age hero OR identity banner ---- *@
    @if (Verdict.IsAgeTreatment)
    {
        <div class="age-hero @BannerClass" data-testid="age-hero">
            <div class="age-num">@AgeHeadline<span>@AgeSuffix</span></div>
            <div class="age-lbl">@AgeLabel</div>
            <div class="age-sub">The holder proved the threshold. Their birth date was never revealed.</div>
        </div>
    }
    else
    {
        <div class="verdict-banner @BannerClass" data-testid="verdict-banner">
            <span class="tick">@BannerGlyph</span>
            <span>
                <span class="vt">@BannerTitle</span>
                <span class="vs">@BannerSubtitle</span>
            </span>
        </div>
    }

    @* ---- Portrait ---- *@
    @if (!string.IsNullOrEmpty(Verdict.PortraitBase64))
    {
        <div class="portrait-wrap">
            <div class="portrait @(Verdict.IsAgeTreatment ? "sm" : "lg")">
                <img src="@PortraitSrc(Verdict.PortraitBase64)" alt="Holder portrait" data-testid="portrait" />
            </div>
            <div class="compare">@(Verdict.IsAgeTreatment ? "Confirm it's the same person" : "Compare to the person present")</div>
            @if (!Verdict.IsAgeTreatment && HolderName is not null)
            {
                <div class="name" data-testid="holder-name">@HolderName</div>
            }
        </div>
    }

    @* ---- Minimal-disclosure statement (age only) ---- *@
    @if (Verdict.MinimalDisclosureNote is not null)
    {
        <div class="privacy" data-testid="minimal-disclosure">
            <b>Minimal disclosure.</b> @Verdict.MinimalDisclosureNote
        </div>
    }

    @* ---- Shared with you ---- *@
    <div class="disc">
        <h3>Shared with you</h3>
        @foreach (var d in Verdict.Disclosed)
        {
            <div class="row" data-testid="disclosed-claim">
                <span class="lbl">@Humanize(d.Key)</span>
                <span class="val">@DisplayValue(d)</span>
            </div>
        }
        @if (Verdict.Withheld.Count > 0)
        {
            <div class="withheld" data-testid="withheld-claims">
                <b>Withheld:</b> @string.Join(" · ", Verdict.Withheld.Select(Humanize))
            </div>
        }
    </div>

    @* ---- Issuer line ---- *@
    @if (!string.IsNullOrEmpty(Verdict.IssuerDid) || !string.IsNullOrEmpty(Verdict.IssuerDisplayName))
    {
        <div class="issuer" data-testid="issuer-line">
            <span class="seal">✓</span>
            <span>Issued by <b>@(Verdict.IssuerDisplayName ?? Verdict.IssuerDid)</b> · signature @(Verdict.IssuerSignatureVerified ? "verified" : "not verified")</span>
        </div>
    }

    @* ---- Trust trail (collapsed) ---- *@
    <details class="trail" data-testid="verify-trail">
        <summary>How this was verified <span class="caret">›</span></summary>
        <div class="layers">
            @foreach (var layer in Verdict.Layers)
            {
                <div class="layer" data-testid="@($"trail-{layer.Layer}")">
                    <span class="ic @StatusClass(layer.Status)">@StatusGlyph(layer.Status)</span>
                    <span class="lx">
                        <span class="lt">@LayerTitle(layer.Layer)</span>
                        <span class="ld">@layer.Headline</span>
                    </span>
                    <span class="st @StatusClass(layer.Status)">@StatusWord(layer.Status)</span>
                </div>
            }
            @if (!Verdict.Layers.Any(l => l.Layer == ValidationLayer.RegisterAnchor))
            {
                @if (string.IsNullOrEmpty(Verdict.RegisterAnchorId))
                {
                    <div class="layer" data-testid="anchor-na">
                        <span class="ic q">?</span>
                        <span class="lx">
                            <span class="lt">Register-anchored</span>
                            <span class="ld">Not applicable for this credential</span>
                        </span>
                        <span class="st na">n/a</span>
                    </div>
                }
                else
                {
                    <div class="layer layer-action" data-testid="trail-RegisterAnchor">
                        <span class="ic q">?</span>
                        <span class="lx">
                            <span class="lt">Register-anchored</span>
                            <span class="ld">Confirm it exists on the Sorcha register</span>
                        </span>
                        <button class="st act" disabled="@_anchorChecking" @onclick="VerifyAnchorAsync" data-testid="verify-anchor">
                            @(_anchorChecking ? "Checking…" : "Tap to check")
                        </button>
                    </div>
                }
            }
        </div>
    </details>

    @if (OnVerifyAnother.HasDelegate)
    {
        <div class="foot">
            <button class="btn primary" @onclick="OnVerifyAnother" data-testid="verify-another">Verify another</button>
        </div>
    }
</div>
```

Then update `@code` to add the derived display helpers + the `OnVerifyAnother` param, keeping the existing `Verdict` param, `_anchorChecking`, and `VerifyAnchorAsync` (unchanged logic — it appends the `RegisterAnchor` layer). Add:

```csharp
    /// <summary>Optional "verify another" affordance; the foot renders only when a host supplies it.</summary>
    [Parameter] public EventCallback OnVerifyAnother { get; set; }

    private string BannerClass => Verdict.OverallPass
        ? (Verdict.IssuerSignatureVerified ? "verdict-pass" : "verdict-warn")
        : "verdict-fail";

    private string BannerGlyph => Verdict.OverallPass ? "✓" : "✗";
    private string BannerTitle => Verdict.OverallPass
        ? (Verdict.IssuerSignatureVerified ? "Identity verified" : "Verified with reduced assurance")
        : "Not verified";
    private string BannerSubtitle => Verdict.OverallPass
        ? (Verdict.IssuerSignatureVerified ? "Genuine credential · issuer confirmed · not revoked" : "Issuer signature could not be checked")
        : (Verdict.Errors.Count > 0 ? Verdict.Errors[0] : "A verification check failed");

    private string AgeHeadline => Verdict.AgeOver18 == true ? "18" : "18";
    private string AgeSuffix => "+";
    private string AgeLabel => Verdict.AgeOver18 == true ? "Over 18 — confirmed"
        : Verdict.AgeOver18 == false ? "Not over 18" : "Could not confirm";

    private string? HolderName => Verdict.Disclosed
        .FirstOrDefault(d => d.Key is "fullName").Value is { Length: > 0 } n ? n : null;

    private static string DisplayValue(KeyValuePair<string, string> d) =>
        d.Key == "portrait" ? "Shown above"
        : d.Value is "True" or "true" ? "Yes"
        : d.Value is "False" or "false" ? "No"
        : d.Value;

    private static string Humanize(string claim) => claim switch
    {
        "fullName" => "Full name",
        "givenName" => "First name",
        "familyName" => "Last name",
        "dateOfBirth" => "Date of birth",
        "age_over_18" => "Age over 18",
        "portrait" => "Photo",
        "email" => "Email",
        _ => claim,
    };

    private static string LayerTitle(ValidationLayer layer) => layer switch
    {
        ValidationLayer.LivePresentation => "Live presentation",
        ValidationLayer.IssuerSignature => "Issuer signature",
        ValidationLayer.Revocation => "Not revoked",
        ValidationLayer.RegisterAnchor => "Register-anchored",
        _ => layer.ToString(),
    };

    private static string StatusGlyph(LayerStatus s) => s switch
    {
        LayerStatus.Pass => "✓", LayerStatus.Fail => "✗", _ => "?",
    };
    private static string StatusWord(LayerStatus s) => s switch
    {
        LayerStatus.Pass => "Pass", LayerStatus.Fail => "Fail", _ => "Unverified",
    };
    private static string StatusClass(LayerStatus s) => s switch
    {
        LayerStatus.Pass => "ok", LayerStatus.Fail => "bad", _ => "q",
    };

    private static string PortraitSrc(string token) =>
        token.StartsWith("data:") ? token : $"data:image/jpeg;base64,{token}";
```

Add a computed `IssuerSignatureVerified` to `VerdictViewModel` (Step 2 file) so the banner can branch without re-reading layers:

```csharp
    /// <summary>Whether the credential's issuer signature was cryptographically verified (drives warn vs pass).</summary>
    public bool IssuerSignatureVerified { get; private init; }
```

…set in `From`: `IssuerSignatureVerified = outcome.IssuerSignature == IssuerSignatureStatus.Verified,` (add `using Sorcha.Verifier.Engine.Models;` — already present).

- [ ] **Step 6: Create the isolated CSS from the mockup**

Create `VerdictTrailPanel.razor.css` translating the mockup's tokens. Use literal light/dark values with `@media (prefers-color-scheme: dark)` (the panel renders in both hosts; keep it self-contained rather than depending on `--sorcha-*`). Full content:

```css
/* SPDX-License-Identifier: MIT */
/* Copyright (c) 2026 Sorcha Contributors */

.verdict-trail-panel {
    --brand: #4b3fd4; --brand-ink: #3a2fb0;
    --card: #fff; --line: #e7e6f2; --ink: #1b1a2e; --ink-2: #54536b; --ink-3: #8a8aa1;
    --good: #12894f; --good-bg: #e7f6ee; --good-line: #bfe6d0;
    --warn: #a86a12; --warn-bg: #fbf1de; --warn-line: #eed9ad;
    --bad: #c02c39; --bad-bg: #fbe9eb; --bad-line: #f0c4c9;
    --ground: #f5f5fb;
    font-family: -apple-system, "Segoe UI", Roboto, "Helvetica Neue", Arial, sans-serif;
    color: var(--ink); line-height: 1.45; max-width: 420px; margin: 0 auto;
}

/* verdict banner (identity) */
.verdict-banner {
    display: flex; align-items: center; gap: 12px; border-radius: 14px;
    padding: 13px 15px; margin-bottom: 18px; border: 1px solid var(--good-line);
    background: var(--good-bg); color: var(--good);
}
.verdict-banner.verdict-warn { background: var(--warn-bg); border-color: var(--warn-line); color: var(--warn); }
.verdict-banner.verdict-fail { background: var(--bad-bg); border-color: var(--bad-line); color: var(--bad); }
.verdict-banner .tick {
    flex: 0 0 auto; width: 30px; height: 30px; border-radius: 50%; background: currentColor;
    color: #fff; display: grid; place-items: center; font-size: 1rem; font-weight: 700;
}
.verdict-banner .vt { font-weight: 700; font-size: 1.02rem; display: block; }
.verdict-banner .vs { font-size: .76rem; opacity: .85; }

/* age hero */
.age-hero {
    text-align: center; border-radius: 18px; padding: 24px 16px 20px; margin-bottom: 18px;
    border: 1px solid var(--good-line); background: var(--good-bg);
}
.age-hero.verdict-warn { background: var(--warn-bg); border-color: var(--warn-line); }
.age-hero.verdict-fail { background: var(--bad-bg); border-color: var(--bad-line); }
.age-num {
    font-size: 4.6rem; line-height: .9; font-weight: 800; letter-spacing: -.04em; color: var(--good);
    display: inline-flex; align-items: flex-start; gap: 2px;
}
.age-hero.verdict-warn .age-num { color: var(--warn); }
.age-hero.verdict-fail .age-num { color: var(--bad); }
.age-num span { font-size: 2.4rem; font-weight: 700; margin-top: .15em; }
.age-lbl { margin-top: 8px; font-weight: 700; font-size: 1.02rem; color: var(--good); }
.age-hero.verdict-warn .age-lbl { color: var(--warn); }
.age-hero.verdict-fail .age-lbl { color: var(--bad); }
.age-sub { font-size: .78rem; color: var(--ink-2); margin-top: 3px; }

/* portrait */
.portrait-wrap { display: flex; flex-direction: column; align-items: center; text-align: center; margin-bottom: 18px; }
.portrait {
    border-radius: 16px; overflow: hidden; border: 3px solid var(--card);
    box-shadow: 0 0 0 1px var(--line), 0 12px 26px rgba(43, 37, 150, .16); background: #c9c7e4; aspect-ratio: 3 / 4;
}
.portrait.lg { width: 172px; } .portrait.sm { width: 120px; }
.portrait img { display: block; width: 100%; height: 100%; object-fit: cover; }
.compare { margin-top: 10px; font-size: .72rem; color: var(--ink-3); text-transform: uppercase; letter-spacing: .09em; }
.name { margin-top: 8px; font-size: 1.5rem; font-weight: 700; letter-spacing: -.02em; }

/* shared-with-you */
.disc { border: 1px solid var(--line); border-radius: 14px; padding: 14px 15px; margin-bottom: 14px; background: var(--card); }
.disc h3 { margin: 0 0 10px; font-size: .7rem; text-transform: uppercase; letter-spacing: .09em; color: var(--ink-3); font-weight: 700; }
.row { display: flex; justify-content: space-between; gap: 14px; padding: 7px 0; border-top: 1px solid var(--line); font-size: .9rem; }
.row:first-of-type { border-top: 0; }
.row .lbl { color: var(--ink-2); }
.row .val { font-weight: 600; text-align: right; }
.withheld { margin-top: 11px; padding-top: 11px; border-top: 1px dashed var(--line); font-size: .78rem; color: var(--ink-3); }
.withheld b { color: var(--ink-2); font-weight: 600; }

/* privacy */
.privacy {
    background: rgba(75, 63, 212, .08); border: 1px solid rgba(75, 63, 212, .22);
    border-radius: 12px; padding: 11px 13px; margin-bottom: 14px; font-size: .8rem; color: var(--ink-2);
}
.privacy b { color: var(--brand-ink); }

/* issuer */
.issuer {
    display: flex; align-items: center; gap: 10px; font-size: .84rem; color: var(--ink-2);
    padding: 11px 14px; border: 1px solid var(--line); border-radius: 12px; margin-bottom: 14px; background: var(--ground);
}
.issuer .seal { flex: 0 0 auto; width: 20px; height: 20px; border-radius: 50%; background: var(--brand); color: #fff; display: grid; place-items: center; font-size: .66rem; font-weight: 700; }
.issuer b { color: var(--ink); font-weight: 600; }

/* trail */
.trail { border: 1px solid var(--line); border-radius: 14px; overflow: hidden; }
.trail > summary { list-style: none; cursor: pointer; display: flex; align-items: center; gap: 10px; padding: 13px 15px; font-size: .86rem; font-weight: 600; }
.trail > summary::-webkit-details-marker { display: none; }
.trail > summary .caret { margin-left: auto; color: var(--ink-3); transition: transform .2s; }
.trail[open] > summary .caret { transform: rotate(90deg); }
.layers { border-top: 1px solid var(--line); padding: 6px; }
.layer { display: flex; align-items: center; gap: 11px; padding: 11px 10px; border-radius: 10px; }
.layer + .layer { border-top: 1px solid var(--line); }
.layer .ic { flex: 0 0 auto; width: 26px; height: 26px; border-radius: 8px; display: grid; place-items: center; font-size: .8rem; font-weight: 700; }
.ic.ok { background: var(--good-bg); color: var(--good); }
.ic.bad { background: var(--bad-bg); color: var(--bad); }
.ic.q { background: var(--ground); color: var(--ink-2); }
.layer .lx { flex: 1; min-width: 0; }
.layer .lt { font-size: .86rem; font-weight: 600; display: block; }
.layer .ld { font-size: .76rem; color: var(--ink-2); }
.layer .st { font-size: .68rem; font-weight: 700; text-transform: uppercase; letter-spacing: .06em; padding: 3px 8px; border-radius: 999px; border: 0; }
.st.ok { background: var(--good-bg); color: var(--good); }
.st.bad { background: var(--bad-bg); color: var(--bad); }
.st.q, .st.na { background: var(--ground); color: var(--ink-2); }
.st.act { background: var(--brand); color: #fff; cursor: pointer; }

/* foot */
.foot { display: flex; gap: 10px; margin-top: 18px; }
.btn { flex: 1; text-align: center; padding: 12px; border-radius: 12px; font-weight: 600; font-size: .9rem; border: 1px solid var(--line); background: var(--card); color: var(--ink); cursor: pointer; }
.btn.primary { background: var(--brand); border-color: var(--brand); color: #fff; }

@media (prefers-color-scheme: dark) {
    .verdict-trail-panel {
        --brand: #8b80f5; --brand-ink: #a79dff; --card: #191826; --line: #2b2940;
        --ink: #ecebf7; --ink-2: #a9a8c2; --ink-3: #726f90; --ground: #0f0e1a;
        --good: #54d08a; --good-bg: #13291f; --good-line: #245c3c;
        --warn: #e5b45c; --warn-bg: #2c2413; --warn-line: #5e4c22;
        --bad: #f2848d; --bad-bg: #2c1518; --bad-line: #5e2b31;
    }
}
```

- [ ] **Step 7: Run the panel tests to verify they pass**

Run: `dotnet build tests/Sorcha.UI.Core.Tests/Sorcha.UI.Core.Tests.csproj && dotnet test tests/Sorcha.UI.Core.Tests/Sorcha.UI.Core.Tests.csproj`
Expected: new tests pass; `Failed` unchanged from baseline. Also build the library: `dotnet build src/Apps/Sorcha.UI/Sorcha.UI.Components.User/Sorcha.UI.Components.User.csproj`.

- [ ] **Step 8: Commit**

```bash
git add src/Apps/Sorcha.UI/Sorcha.UI.Components.User/Models/Verification/VerdictViewModel.cs \
        src/Apps/Sorcha.UI/Sorcha.UI.Components.User/Components/Verify/VerdictTrailPanel.razor \
        src/Apps/Sorcha.UI/Sorcha.UI.Components.User/Components/Verify/VerdictTrailPanel.razor.css \
        tests/Sorcha.UI.Core.Tests/Verification/VerdictTrailPanelTests.cs
git commit -m "feat: [#174] rebuild VerdictTrailPanel to the approved mockup (identity + age + fail/warn)"
```

---

## Task 4: Thread the outcome through `VerificationSessionQr` and render the panel on the web desk verifier

**Files:**
- Modify: `src/Apps/Sorcha.UI/Sorcha.UI.Components.User/Components/Verify/VerificationSessionQr.razor` (raise the `VerificationOutcome`)
- Modify: `src/Apps/Sorcha.Verifier/Components/Pages/Index.razor` (render the panel)
- Test: `tests/Sorcha.Verifier.Tests/Pages/VerifyPageTests.cs` (assert the panel is wired)

**Interfaces:**
- Consumes: `poll.Outcome` (Task 2), `VerdictViewModel.From` + `VerdictTrailPanel` (Task 3).
- Produces: `VerificationSessionQr` gains `[Parameter] EventCallback<VerificationOutcome?> OnOutcome` (raised alongside the existing `OnCompleted`).

- [ ] **Step 1: Record the desk test baseline**

Run: `dotnet build tests/Sorcha.Verifier.Tests/Sorcha.Verifier.Tests.csproj && dotnet test tests/Sorcha.Verifier.Tests/Sorcha.Verifier.Tests.csproj`
Record `Failed: N, Passed: N`.

- [ ] **Step 2: Add `OnOutcome` to `VerificationSessionQr`**

In `VerificationSessionQr.razor` `@code`, add the parameter and raise it in `PollAsync` on completion (keep `OnCompleted` for back-compat with the PWA history write):

```csharp
    /// <summary>Raised once with the computed verdict when polling reports completion (may be null).</summary>
    [Parameter] public EventCallback<VerificationOutcome?> OnOutcome { get; set; }
```

Add `@using Sorcha.Verifier.Engine.Models` to the top of the file. In `PollAsync`, where it currently does `if (poll.IsComplete) { if (!_disposed) await OnCompleted.InvokeAsync(poll.VpToken ?? ""); return; }`, change to raise both:

```csharp
                if (poll.IsComplete)
                {
                    if (!_disposed)
                    {
                        await OnOutcome.InvokeAsync(poll.Outcome);
                        await OnCompleted.InvokeAsync(poll.VpToken ?? "");
                    }
                    return;
                }
```

- [ ] **Step 3: Write the failing desk wiring test**

In `tests/Sorcha.Verifier.Tests/Pages/VerifyPageTests.cs`, add a content assertion that the desk page now mounts the verdict panel and no longer hardcodes the static success text:

```csharp
    [Fact]
    public void DeskIndexRazor_RendersVerdictTrailPanel_NotHardcodedSuccess()
    {
        var content = File.ReadAllText(IndexRazorPath);   // reuse the path constant already in this file
        content.Should().Contain("VerdictTrailPanel");
        content.Should().Contain("OnOutcome");
        content.Should().NotContain("The credential was presented and verified successfully");
    }
```

(If the file uses a different mechanism to locate `Index.razor`, reuse it. These are file-content tests — the pattern the file already uses.)

- [ ] **Step 4: Run to verify it fails**

Run: `dotnet build tests/Sorcha.Verifier.Tests/Sorcha.Verifier.Tests.csproj && dotnet test tests/Sorcha.Verifier.Tests/Sorcha.Verifier.Tests.csproj`
Expected: the new test FAILS (panel not yet wired).

- [ ] **Step 5: Render the panel on the desk page**

In `src/Apps/Sorcha.Verifier/Components/Pages/Index.razor`, replace the `else { <MudPaper>…Verification Complete…</MudPaper> }` block (lines ~36-46) with the panel, and capture the outcome:

```razor
    else
    {
        @if (_verdict is not null)
        {
            <VerdictTrailPanel Verdict="_verdict" OnVerifyAnother="@Reset" />
        }
        else
        {
            <MudPaper Elevation="2" Class="pa-6 text-center">
                <MudText Typo="Typo.h6" Class="mb-2">Verification received</MudText>
                <MudText Typo="Typo.body2" Color="Color.Secondary" Class="mb-4">
                    The presentation was received but no verdict was available.
                </MudText>
                <MudButton Variant="Variant.Outlined" OnClick="@Reset">Verify another</MudButton>
            </MudPaper>
        }
    }
```

Change the `VerificationSessionQr` element to bind `OnOutcome`:

```razor
        <VerificationSessionQr Question="@_selectedQuestion"
                               CancellationToken="@_pageCts.Token"
                               OnOutcome="@HandleOutcomeAsync"
                               OnCompleted="@HandleCompletedAsync" />
```

In `@code`, add the field + handler and build the verdict:

```csharp
    private VerdictViewModel? _verdict;

    private Task HandleOutcomeAsync(VerificationOutcome? outcome)
    {
        if (outcome is not null && _selectedQuestion is not null)
            _verdict = VerdictViewModel.From(_selectedQuestion, outcome);
        return Task.CompletedTask;
    }
```

Add `@using Sorcha.Verifier.Engine.Models` to the page's usings. In `Reset()`, also clear `_verdict = null;`. Keep `HandleCompletedAsync` (sets `_complete = true`).

- [ ] **Step 6: Run the desk tests + build the app**

Run: `dotnet build src/Apps/Sorcha.Verifier/Sorcha.Verifier.csproj && dotnet test tests/Sorcha.Verifier.Tests/Sorcha.Verifier.Tests.csproj`
Expected: new test passes; `Failed` unchanged from baseline; app builds.

- [ ] **Step 7: Commit**

```bash
git add src/Apps/Sorcha.UI/Sorcha.UI.Components.User/Components/Verify/VerificationSessionQr.razor \
        src/Apps/Sorcha.Verifier/Components/Pages/Index.razor \
        tests/Sorcha.Verifier.Tests/Pages/VerifyPageTests.cs
git commit -m "feat: [#174] render VerdictTrailPanel on the web desk verifier"
```

---

## Task 5: Render the panel on the PWA doorstep verifier + record the real outcome

**Files:**
- Modify: `src/Apps/Sorcha.Wallet.Pwa/Pages/Verify.razor` (render the panel; record the real outcome instead of hardcoded `Pass`)
- Test: `tests/Sorcha.Wallet.Pwa.Tests/Pages/VerifyPageTests.cs` (assert the panel is wired)

**Interfaces:**
- Consumes: `VerificationSessionQr.OnOutcome` (Task 4), `VerdictViewModel` + `VerdictTrailPanel` (Task 3), the existing `IVerificationHistoryStore` + `VerificationRecord` + `Services.VerifyOutcome`.

- [ ] **Step 1: Record the PWA test baseline**

Run: `dotnet build tests/Sorcha.Wallet.Pwa.Tests/Sorcha.Wallet.Pwa.Tests.csproj && dotnet test tests/Sorcha.Wallet.Pwa.Tests/Sorcha.Wallet.Pwa.Tests.csproj`
Record `Failed: N, Passed: N`.

- [ ] **Step 2: Write the failing PWA wiring test**

In `tests/Sorcha.Wallet.Pwa.Tests/Pages/VerifyPageTests.cs`, add:

```csharp
    [Fact]
    public void VerifyRazor_RendersVerdictTrailPanel_NotHardcodedSuccess()
    {
        var content = File.ReadAllText(VerifyRazorPath);   // reuse this file's path constant
        content.Should().Contain("VerdictTrailPanel");
        content.Should().Contain("OnOutcome");
        content.Should().NotContain("The credential was presented and verified successfully");
    }
```

- [ ] **Step 3: Run to verify it fails**

Run: `dotnet build tests/Sorcha.Wallet.Pwa.Tests/Sorcha.Wallet.Pwa.Tests.csproj && dotnet test tests/Sorcha.Wallet.Pwa.Tests/Sorcha.Wallet.Pwa.Tests.csproj`
Expected: the new test FAILS.

- [ ] **Step 4: Render the panel + record the real outcome**

In `src/Apps/Sorcha.Wallet.Pwa/Pages/Verify.razor`:

Add usings: `@using Sorcha.UI.Core.Components.Verify` is already present; add `@using Sorcha.Verifier.Engine.Models`.

Change the `VerificationSessionQr` element to also bind `OnOutcome`:

```razor
        <VerificationSessionQr Question="@_selectedQuestion"
                               CancellationToken="@_pageCts.Token"
                               OnOutcome="@HandleOutcomeAsync"
                               OnCompleted="@HandleCompletedAsync" />
```

Replace the `else { <MudPaper>…Verification Complete…</MudPaper> }` block with:

```razor
    else
    {
        @if (_verdict is not null)
        {
            <VerdictTrailPanel Verdict="_verdict" OnVerifyAnother="@Reset" />
        }
        else
        {
            <MudPaper Elevation="2" Class="pa-6 text-center">
                <MudText Typo="Typo.h6" Class="mb-2">Verification received</MudText>
                <MudButton Variant="Variant.Outlined" OnClick="@Reset">Verify another</MudButton>
            </MudPaper>
        }
    }
```

Add the outcome handler and store the verdict + drive the real history outcome. Add a `_verdict` field and a `_lastOutcome`:

```csharp
    private VerdictViewModel? _verdict;
    private VerificationOutcome? _lastOutcome;

    private Task HandleOutcomeAsync(VerificationOutcome? outcome)
    {
        _lastOutcome = outcome;
        if (outcome is not null && _selectedQuestion is not null)
            _verdict = VerdictViewModel.From(_selectedQuestion, outcome);
        return Task.CompletedTask;
    }
```

In `HandleCompletedAsync`, replace the hardcoded `var historyOutcome = Services.VerifyOutcome.Pass;` with a mapping from the real outcome, and fill the holder/issuer display from disclosed claims:

```csharp
        var historyOutcome = _lastOutcome is null
            ? Services.VerifyOutcome.Fail
            : !_lastOutcome.Accepted
                ? Services.VerifyOutcome.Fail
                : _lastOutcome.IssuerSignature == IssuerSignatureStatus.Verified
                    ? Services.VerifyOutcome.Pass
                    : Services.VerifyOutcome.Warn;

        var holderName = _lastOutcome is not null
            && _lastOutcome.DisclosedClaims.TryGetValue("fullName", out var fn) ? fn?.ToString() ?? "" : "";
```

Use `holderName` for `HolderDisplayName` in the `VerificationRecord` and keep the rest. In `Reset()`, add `_verdict = null; _lastOutcome = null;`.

- [ ] **Step 5: Run the PWA tests + build the app**

Run: `dotnet build src/Apps/Sorcha.Wallet.Pwa/Sorcha.Wallet.Pwa.csproj && dotnet test tests/Sorcha.Wallet.Pwa.Tests/Sorcha.Wallet.Pwa.Tests.csproj`
Expected: new test passes; `Failed` unchanged from baseline; app builds (WASM — confirm no server-only types crept in).

- [ ] **Step 6: Commit**

```bash
git add src/Apps/Sorcha.Wallet.Pwa/Pages/Verify.razor \
        tests/Sorcha.Wallet.Pwa.Tests/Pages/VerifyPageTests.cs
git commit -m "feat: [#174] render VerdictTrailPanel on the PWA doorstep verifier + record real outcome"
```

---

## Task 6: Full-solution build, doc sync, and finish

**Files:**
- Modify: `.claude/skills/sorcha-architecture/SKILL.md` (F155 section — note the panel is now wired into both hosts via the HAIP outcome mapping)
- Modify: `docs/reference/development-status.md` (if it tracks the verify surface — optional, only if a relevant row exists)

- [ ] **Step 1: Build the whole solution**

Run: `dotnet build Sorcha.sln` (or the repo's solution file — `ls *.sln`). Expected: clean build. Fix any cross-project fallout (e.g. a caller of the changed `HaipPollResult`/`VerificationSessionPoll` records elsewhere — grep `new HaipPollResult(` and `new VerificationSessionPoll(` across the repo and update positional call sites; both changes are additive with defaults, so existing positional calls with fewer args still compile).

- [ ] **Step 2: Run the four touched test projects together**

Run each and confirm `Failed` equals its recorded baseline (no new failures):
```
dotnet test tests/Sorcha.Blueprint.Service.Tests/Sorcha.Blueprint.Service.Tests.csproj
dotnet test tests/Sorcha.UI.Components.User.Tests/Sorcha.UI.Components.User.Tests.csproj
dotnet test tests/Sorcha.UI.Core.Tests/Sorcha.UI.Core.Tests.csproj
dotnet test tests/Sorcha.Verifier.Tests/Sorcha.Verifier.Tests.csproj
dotnet test tests/Sorcha.Wallet.Pwa.Tests/Sorcha.Wallet.Pwa.Tests.csproj
```

- [ ] **Step 3: Update the architecture skill**

In `.claude/skills/sorcha-architecture/SKILL.md`, in the "Open Verifier PWA (Feature 155)" section, add a short note: the shared `VerdictTrailPanel` is now wired into both the web desk verifier (`Sorcha.Verifier/Components/Pages/Index.razor`) and the PWA doorstep verifier (`Sorcha.Wallet.Pwa/Pages/Verify.razor`); the verdict is built by mapping HAIP's authoritative `/result` (`HaipOutcomeMapper`) into a `VerificationOutcome` and surfaced through `IVerificationTransport`/`VerificationSessionQr.OnOutcome`; AIAS issues `age_over_18` (derived from `dateOfBirth` via `AgeClaimDeriver`) so the "Age over 18?" preset matches.

- [ ] **Step 4: Commit the docs**

```bash
git add .claude/skills/sorcha-architecture/SKILL.md
git commit -m "docs: [#174] verify verdict screen wired into both hosts + age_over_18 issuance"
```

- [ ] **Step 5: Report status**

Summarise: all four scope pieces landed (panel redesigned, wired into web desk + PWA, `age_over_18` issued), the whole solution builds, and every touched test project matches its baseline. Note deploy targets for the human: `sorcha-verifier`, `sorcha-wallet-pwa`, `sorcha-ui-web`, `wallet-service`, then re-provision the AIAS blueprint + re-claim the credential to pick up `age_over_18`.

---

## Notes for the executor

- **Issuer display name limitation (accepted):** the issuer line shows the issuer DID (or nothing) until an org-name resolver exists client-side — the mockup's "Acme Identity Assurance Services" friendly name is a deferred refinement (design §6 open question "issuer logo vs name"). Do not add a network resolver in this plan.
- **Online path has no Warn in practice:** HAIP online verification resolves the issuer key, so an accepted desk/PWA-online verdict is always `Verified` → Pass. The Warn treatment still renders correctly if `IssuerSignature==NotVerified` ever arrives (e.g. a future offline path). The panel and both hosts handle all three states.
- **Register-anchor layer** stays on-demand (the panel's existing `VerifyAnchorAsync`), driven by a disclosed `registerAnchor` claim + the credential `jti`. AIAS credentials may not disclose `registerAnchor` today → the panel shows "Not applicable"; that is correct and out of scope to change here.
- **Deploy + live-verify** (human, after merge): per the `n1-deploy` skill, `pull` + `up -d --force-recreate --no-deps` for `sorcha-verifier`, `sorcha-wallet-pwa`, `sorcha-ui-web`, `wallet-service`; re-provision the AIAS blueprint (the template edit) and re-claim the AIAS credential so it carries `age_over_18`; run a "Confirm identity" and an "Age over 18?" verification and confirm the new screens render.
