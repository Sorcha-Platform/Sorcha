// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

using System;
using System.Collections.Generic;
using System.Linq;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Sorcha.CitizenWallet.Abstractions.Constants;
using Sorcha.UI.Components.User.Services.Verification;
using Sorcha.UI.Core.Models.Presentation;
using Sorcha.Verifier.Engine.Dcql;
using Sorcha.Wallet.Pwa.Services.Presentation;
using Xunit;

namespace Sorcha.Wallet.Pwa.Tests.Services.Presentation;

/// <summary>
/// Credential-VCT-decoupling (Task 9) — end-to-end regression for the reported bug where the PWA
/// verifier reported "None of your credentials match" for an Assured Identity credential. Proves
/// that an Assured-Identity credential (<see cref="VctUris.AssuredIdentityV1"/> as its <c>vct</c>)
/// satisfies the verifier's built-in Assured-Identity presets (<see cref="DefaultPresetCatalogue"/>),
/// exercising the exact constant → preset → DCQL query → <see cref="PresentationEngine.MatchQuery"/>
/// path Tasks 1-8 wired up. A first-run pass here is the proof the bug is fixed — a failure would
/// mean that wiring still has a gap.
/// </summary>
public class AssuredIdentityPresetRegressionTests
{
    private static readonly PresentationEngine Engine = new(TimeProvider.System, NullLogger<PresentationEngine>.Instance);

    // No configured presets ⇒ the catalogue falls back to its builtin set (same as production
    // when "VerifierPresets" is unconfigured).
    private static readonly DefaultPresetCatalogue Catalogue = new(Options.Create(new VerifierPresetsOptions()));

    [Fact]
    public void MatchQuery_AssuredIdentityCredential_SatisfiesAssuredIdentityPreset()
    {
        var preset = Catalogue.GetByKey("confirm-identity");
        preset.Should().NotBeNull();
        preset!.RequiredVct.Should().Be(VctUris.AssuredIdentityV1);

        var cred = Cred(VctUris.AssuredIdentityV1, "fullName", "portrait");
        var request = RequestFromPreset(preset);

        var result = Engine.MatchQuery(request, [cred]);

        result.Satisfiable.Should().BeTrue();
        result.UnsatisfiedRequiredQueryIds.Should().BeEmpty();
        var queryMatch = result.PerQuery.Should().ContainSingle().Subject;
        queryMatch.IsSatisfiable.Should().BeTrue();
        queryMatch.Candidates.Should().ContainSingle().Which.Credential.Should().BeSameAs(cred);
    }

    [Fact]
    public void MatchQuery_AssuredIdentityCredential_SatisfiesAgeOver18Preset()
    {
        var preset = Catalogue.GetByKey("age-over-18");
        preset.Should().NotBeNull();
        preset!.RequiredVct.Should().Be(VctUris.AssuredIdentityV1);

        var cred = Cred(VctUris.AssuredIdentityV1, "age_over_18", "portrait");
        var request = RequestFromPreset(preset);

        var result = Engine.MatchQuery(request, [cred]);

        result.Satisfiable.Should().BeTrue();
        result.PerQuery.Should().ContainSingle().Which.Candidates.Should().ContainSingle()
            .Which.Credential.Should().BeSameAs(cred);
    }

    // ── builders (mirrors DcqlMatchTests) ──────────────────────────────────────

    private static CachedCredential Cred(string vct, params string[] claims) => new()
    {
        Id = Guid.NewGuid(),
        Vct = vct,
        RawSdJwt = "header.body.sig~",
        AvailableClaimNames = claims,
    };

    /// <summary>
    /// Builds the DCQL query the verifier would send for a preset via the ONE shared production
    /// builder (<c>DcqlRequestBuilder</c>) — the exact path <c>VerifierEndpoints</c> uses
    /// (<c>DcqlRequestBuilder.Build([DcqlCredentialAsk.SdJwt(id, vct, required, optional)])</c>).
    /// Required vs. optional claims must go through the builder rather than a flat <c>Claims</c>
    /// list: without <c>claim_sets</c>, <see cref="DcqlRequestParser.SplitClaims"/> treats every
    /// listed claim as required, which would silently mis-model the preset's optional claims.
    /// </summary>
    private static ParsedPresentationRequest RequestFromPreset(Sorcha.UI.Components.User.Models.Verification.VerificationPreset preset)
    {
        var query = DcqlRequestBuilder.Build(
            [DcqlCredentialAsk.SdJwt(preset.Key, preset.RequiredVct, preset.RequiredClaims, preset.OptionalClaims)]);

        return new ParsedPresentationRequest
        {
            ClientId = "did:sorcha:org:verifier",
            ResponseUri = "https://verifier.test/cb",
            Nonce = "nonce-1",
            Query = query,
            RequiredVct = preset.RequiredVct,
            RequiredClaims = preset.RequiredClaims,
            OptionalClaims = preset.OptionalClaims,
        };
    }
}
