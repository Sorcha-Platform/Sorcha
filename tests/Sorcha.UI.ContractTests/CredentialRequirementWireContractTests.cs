// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

using System.Text.Json;
using FluentAssertions;
using Sorcha.Blueprint.Models.Credentials;
using Xunit;

namespace Sorcha.UI.ContractTests;

/// <summary>
/// The UI POSTs <see cref="CredentialRequirement"/> objects to Wallet Service's
/// <c>POST /api/v1/wallets/{address}/credentials/match</c>. The client serialises with
/// <c>JsonDefaults.Api</c> (SorchaJson — camelCase properties + kebab-case string enums).
///
/// Wallet Service used to bind the body with ASP.NET's DEFAULT options, which know only the
/// PascalCase names from each enum's type-level <c>[JsonConverter(typeof(JsonStringEnumConverter))]</c>.
/// SorchaJson's kebab converter lives in the <c>Converters</c> collection, which OUTRANKS that
/// attribute — so the client sent <c>"fail-closed"</c> / <c>"sorcha-wallet"</c> and the binder threw.
///
/// Live consequence (n1, 2026-07-28): every match request was rejected 400 in ~1ms, before the
/// handler ran, and the client turned a non-success status into an EMPTY match list. The AIAS Cyber
/// gate therefore told a citizen holding a valid, Active, correctly-typed Assured Identity
/// credential that they had "No matching credential — check your wallet". The endpoint had never
/// worked from the web UI; it went unnoticed because M1's actions declare no credentialRequirements.
/// </summary>
public class CredentialRequirementWireContractTests
{
    private static readonly JsonSerializerOptions ClientOptions = Sorcha.Serialization.SorchaJson.Options;

    /// <summary>Wallet Service's minimal-API body binder, built exactly as its Program.cs does.</summary>
    private static readonly JsonSerializerOptions ServerOptions = BuildServerOptions();

    /// <summary>ASP.NET's binder when a service does NOT call ConfigureHttpJsonOptions — the broken state.</summary>
    private static readonly JsonSerializerOptions UnconfiguredServerOptions = new(JsonSerializerDefaults.Web);

    private static JsonSerializerOptions BuildServerOptions()
    {
        var options = new JsonSerializerOptions(JsonSerializerDefaults.Web);
        Sorcha.Serialization.SorchaJson.Configure(options);
        return options;
    }

    private static CredentialRequirement MakeCyberGateRequirement() => new()
    {
        Type = "https://sorcha.dev/vc/assured-identity/v1",
        PresentationSource = PresentationSource.SorchaWallet,
        RequiredClaims =
        [
            new ClaimConstraint { ClaimName = "givenName" },
            new ClaimConstraint { ClaimName = "familyName" }
        ]
    };

    [Fact]
    public void ClientSerialisedRequirement_IsReadableByTheServersOwnBinder()
    {
        var json = JsonSerializer.Serialize(MakeCyberGateRequirement(), ClientOptions);

        var act = () => JsonSerializer.Deserialize<CredentialRequirement>(json, ServerOptions);

        act.Should().NotThrow(
            "the wallet-service body binder must be able to read what the UI actually sends; "
            + "a binding failure here is a 400 the client reports as 'no matching credential'");

        var roundTripped = JsonSerializer.Deserialize<CredentialRequirement>(json, ServerOptions);
        roundTripped!.PresentationSource.Should().Be(PresentationSource.SorchaWallet,
            "the gate must still route through the SorchaWallet presentation lifecycle");
        roundTripped.Type.Should().Be("https://sorcha.dev/vc/assured-identity/v1");
    }

    [Fact]
    public void PinnedVocabulary_IsNowReadableByAnyBinder_WhichIsWhyPinningIsTheRightMechanism()
    {
        // THIS TEST USED TO ASSERT THE OPPOSITE, and the change is deliberate.
        //
        // It characterised the live n1 failure above: SorchaJson's kebab converter outranked each
        // enum's type-level [JsonConverter], so the client sent "sorcha-wallet" and an unconfigured
        // binder threw. That was true while PresentationSource took whatever the ambient naming
        // policy said.
        //
        // The blueprint AUTHORING VOCABULARY is now pinned per-member with [JsonStringEnumMemberName]
        // (#1623), because it is a published contract: shipped walkthrough blueprints author these
        // values literally, blueprint.schema.json advertises them to agents, and they are serialised
        // into the canonical definition behind a publication id. A member name overrides the naming
        // policy, so BOTH sides now write "SorchaWallet" — where the client used to write
        // "sorcha-wallet" and blueprints wrote "SorchaWallet", two spellings for one value.
        //
        // The consequence is worth asserting rather than merely noting: a pinned value is readable
        // by ANY binder, configured or not. Pinning does not just stabilise the spelling, it removes
        // this entire class of binding failure for the values it covers.
        var json = JsonSerializer.Serialize(MakeCyberGateRequirement(), ClientOptions);

        json.Should().Contain("\"SorchaWallet\"",
            "the pin fixes the wire value against any naming policy");

        var act = () => JsonSerializer.Deserialize<CredentialRequirement>(json, UnconfiguredServerOptions);

        act.Should().NotThrow(
            "a pinned vocabulary value no longer depends on the reader being configured — which is "
            + "precisely why pinning, not a naming policy, is what holds a published contract");
    }

    [Fact]
    public void AnUnpinnedEnum_StillNeedsTheServiceToConfigureJson()
    {
        // The original hazard, preserved. It is closed for the pinned vocabulary and NOT closed in
        // general: an enum that takes the ambient policy still reaches the wire kebab-cased, and a
        // service that skips ConfigureHttpJsonOptions still cannot read it. Kept so the reason for
        // that call remains testable rather than only asserted in a comment someone later tidies away.
        var json = JsonSerializer.Serialize(
            new UnpinnedProbe { Mode = UnpinnedMode.FailClosedProbe }, ClientOptions);

        json.Should().Contain("fail-closed-probe", "an unpinned enum takes the ambient kebab policy");

        var act = () => JsonSerializer.Deserialize<UnpinnedProbe>(json, UnconfiguredServerOptions);

        act.Should().Throw<JsonException>(
            "default options know only the declared member names, so a service that skips "
            + "ConfigureHttpJsonOptions still rejects what a Sorcha client sends for an unpinned enum");
    }

    private sealed class UnpinnedProbe
    {
        public UnpinnedMode Mode { get; set; }
    }

    private enum UnpinnedMode
    {
        FailClosedProbe,
    }

    [Fact]
    public void BlueprintAuthoredPascalCase_StillBinds()
    {
        // Blueprints declare "SorchaWallet" (PascalCase) on the wire — the fix for the kebab
        // direction must NOT break reading authored blueprint JSON.
        const string authored = """
            {"type":"https://sorcha.dev/vc/assured-identity/v1","presentationSource":"SorchaWallet"}
            """;

        var fromClient = JsonSerializer.Deserialize<CredentialRequirement>(authored, ClientOptions);
        var fromServer = JsonSerializer.Deserialize<CredentialRequirement>(authored, ServerOptions);

        fromClient!.PresentationSource.Should().Be(PresentationSource.SorchaWallet);
        fromServer!.PresentationSource.Should().Be(PresentationSource.SorchaWallet);
    }

    [Fact]
    public void WalletServiceStillConfiguresItsJsonBinder()
    {
        // The regression guard that matters. The round-trip tests above build their own options, so
        // they stay green even if Program.cs stops configuring the binder — which is exactly the
        // defect that shipped. Assert the call is actually present in the service.
        var repoRoot = FindRepoRoot();
        var program = Path.Combine(repoRoot, "src", "Services", "Sorcha.Wallet.Service", "Program.cs");

        File.Exists(program).Should().BeTrue($"expected Wallet Service Program.cs at {program}");
        File.ReadAllText(program).Should().Contain("ConfigureHttpJsonOptions",
            "Wallet Service must configure its minimal-API JSON binder with SorchaJson, or every "
            + "request body carrying a kebab-case enum is rejected 400 before the handler runs");
    }

    private static string FindRepoRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !Directory.Exists(Path.Combine(dir.FullName, ".git")))
            dir = dir.Parent;
        return dir?.FullName ?? throw new InvalidOperationException("repo root not found");
    }
}
