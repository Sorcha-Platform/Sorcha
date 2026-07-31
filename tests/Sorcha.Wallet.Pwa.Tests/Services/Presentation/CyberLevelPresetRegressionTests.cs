// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

using System;
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
/// AIAS M3 — proves a Cyber Level credential satisfies the builtin Cyber Level preset through the
/// real path a verify surface uses: <see cref="VctUris.CyberLevelV1"/> → <see cref="DefaultPresetCatalogue"/>
/// → <see cref="DcqlRequestBuilder"/> → <see cref="PresentationEngine.MatchQuery"/>.
/// </summary>
/// <remarks>
/// Mirrors <see cref="AssuredIdentityPresetRegressionTests"/>, which exists because the PWA once
/// reported "None of your credentials match" for a credential the holder genuinely held. The Cyber
/// Level credential is the one the AIAS demo's verify moment turns on, and until this preset existed
/// it could be issued but never requested — so that same failure was guaranteed for it.
/// <para>
/// Asserting the catalogue merely *contains* an entry would not catch a preset whose claims the
/// credential does not carry, or whose vct is subtly wrong; only running it through the DCQL builder
/// and the matcher does. Note the builder call is deliberate: a flat <c>Claims</c> list would make
/// <c>DcqlRequestParser.SplitClaims</c> treat optional claims as required and silently mis-model the
/// preset.
/// </para>
/// </remarks>
public class CyberLevelPresetRegressionTests
{
    private static readonly PresentationEngine Engine =
        new(TimeProvider.System, NullLogger<PresentationEngine>.Instance);

    // No configured presets ⇒ builtin fallback, same as production where "VerifierPresets" is unset
    // (which is every surface today — the kiosk sets no such config, and the two WASM surfaces
    // cannot practically be configured per-deployment).
    private static readonly DefaultPresetCatalogue Catalogue = new(Options.Create(new VerifierPresetsOptions()));

    [Fact]
    public void MatchQuery_CyberLevelCredential_SatisfiesCyberLevelPreset()
    {
        var preset = Catalogue.GetByKey("cyber-level");
        preset.Should().NotBeNull();
        preset!.RequiredVct.Should().Be(VctUris.CyberLevelV1);

        // A credential carrying exactly what the AIAS cyber blueprint marks `disclosable`.
        var cred = Cred(VctUris.CyberLevelV1, "level", "portrait", "givenName", "familyName");
        var request = RequestFromPreset(preset);

        var result = Engine.MatchQuery(request, [cred]);

        result.Satisfiable.Should().BeTrue();
        result.UnsatisfiedRequiredQueryIds.Should().BeEmpty();
        var queryMatch = result.PerQuery.Should().ContainSingle().Subject;
        queryMatch.IsSatisfiable.Should().BeTrue();
        queryMatch.Candidates.Should().ContainSingle().Which.Credential.Should().BeSameAs(cred);
    }

    [Fact]
    public void MatchQuery_CyberLevelCredential_WithOnlyRequiredClaims_StillSatisfies()
    {
        // The optional claims are optional: a holder who discloses only level + portrait must still
        // satisfy the check, or the "selective disclosure" the design promises is not real.
        var preset = Catalogue.GetByKey("cyber-level")!;
        var cred = Cred(VctUris.CyberLevelV1, "level", "portrait");

        var result = Engine.MatchQuery(RequestFromPreset(preset), [cred]);

        result.Satisfiable.Should().BeTrue();
    }

    [Fact]
    public void MatchQuery_AssuredIdentityCredential_DoesNotSatisfyCyberLevelPreset()
    {
        // The preset must discriminate on vct. A holder who only ever completed Assured Identity
        // must NOT pass a Cyber Level check — that is precisely the brittleness recorded in #1351,
        // and the reason a multi-type preset (not a loosened single one) is the right fix later.
        var preset = Catalogue.GetByKey("cyber-level")!;
        var assuredId = Cred(VctUris.AssuredIdentityV1, "level", "portrait");

        var result = Engine.MatchQuery(RequestFromPreset(preset), [assuredId]);

        result.Satisfiable.Should().BeFalse();
    }

    // ── builders (mirrors AssuredIdentityPresetRegressionTests) ────────────────

    private static CachedCredential Cred(string vct, params string[] claims) => new()
    {
        Id = Guid.NewGuid(),
        Vct = vct,
        RawSdJwt = "header.body.sig~",
        AvailableClaimNames = claims,
    };

    private static ParsedPresentationRequest RequestFromPreset(
        Sorcha.UI.Components.User.Models.Verification.VerificationPreset preset)
    {
        var query = DcqlRequestBuilder.Build(
            [DcqlCredentialAsk.SdJwt(preset.Key, preset.RequiredVct, preset.RequiredClaims, preset.OptionalClaims)]);

        return new ParsedPresentationRequest
        {
            ClientId = "did:sorcha:org:verifier",
            ResponseUri = "https://verifier.test/cb",
            Nonce = "nonce-1",
            State = "state-1",
            Query = query,
            RequiredVct = preset.RequiredVct,
            RequiredClaims = preset.RequiredClaims,
            OptionalClaims = preset.OptionalClaims,
        };
    }
}
