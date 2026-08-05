// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

using System;
using System.Buffers.Text;
using System.Collections.Generic;
using System.Security.Cryptography;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Sorcha.Verifier.Engine;
using Sorcha.Verifier.Engine.Models;
using Xunit;
using Sorcha.Verification.Abstractions;

namespace Sorcha.Verifier.Tests.Services;

/// <summary>
/// #1195 Phase 2 (final review C-1 / I-2). The verifier engine must accept a delegation-free
/// SERVER-CUSTODY presentation — a holder-<c>cnf</c> credential whose KB-JWT is signed directly by
/// the credential's own holder key, with NO device delegation credential (design §2). This is what
/// the Task-6 "bind to device" flow posts. The delegation model (F114/F127) stays byte-identical and
/// is covered by <see cref="DisclosureAnchoringTests"/> and the Feature 138 replay suite.
/// </summary>
public sealed class ServerCustodyPresentationTests
{
    private const string Vct = VpValidatorTestHarness.Vct;

    /// <summary>
    /// A validator with a stub status-list cache (never consulted on the server-custody path — there
    /// is no delegation and therefore no status_list block) and a caller-supplied issuer resolver +
    /// RequireIssuerSignature policy. Defaults reproduce the offline v1 contract (opt-out resolver,
    /// requireIssuerSignature false).
    /// </summary>
    private static VerifiablePresentationValidator BuildValidator(
        IIssuerKeyResolver? issuerKeys = null,
        bool requireIssuerSignature = false)
    {
        var statusList = new Mock<IStatusListCache>();
        statusList
            .Setup(s => s.CheckAsync(
                It.IsAny<string>(), It.IsAny<int>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(StatusListVerdict.Active);

        return new VerifiablePresentationValidator(
            statusList.Object,
            issuerKeys ?? new OptOutIssuerKeyResolver(),
            TimeProvider.System,
            NullLogger<VerifiablePresentationValidator>.Instance,
            requireIssuerSignature: requireIssuerSignature);
    }

    // ─────────────────────────── C-1: server-custody accept / reject ─────────────────────────────

    [Fact]
    public async Task ValidateAsync_ServerCustody_HolderSignedNoDelegation_Accepts_WithClaimsAndLiveLayer()
    {
        // The delegation-free, HOLDER-signed present (P-256 holder). BEFORE the C-1 fix this failed
        // with "Delegation credential is required for citizen wallet presentations" — that is the
        // reproduction of the review finding.
        var bundle = TestVpFactory.MintServerCustodyP256(
            Vct, VpValidatorTestHarness.Claims(("givenName", "Sarah")),
            VpValidatorTestHarness.ClientId, VpValidatorTestHarness.Nonce);

        var outcome = await BuildValidator().ValidateAsync(
            VpValidatorTestHarness.Session(), bundle.VpToken, delegationCredential: null);

        outcome.Accepted.Should().BeTrue(string.Join(", ", outcome.Errors));
        outcome.DisclosedClaims.Should().ContainKey("givenName");

        // The holder-key binding verified — the LivePresentation layer passes and its verdict trail
        // records server-custody honestly (no fabricated delegation). This layer Pass IS the
        // "HolderKeyVerified == true" signal (VerificationOutcome exposes no separate flag).
        var live = outcome.Layers.Should().ContainSingle(l => l.Layer == ValidationLayer.LivePresentation).Subject;
        live.Status.Should().Be(VerificationStatus.Verified);
        live.Detail.Should().ContainKey("delegation")
            .WhoseValue.Should().Contain("server-custody (none)");
    }

    [Fact]
    public async Task ValidateAsync_ServerCustody_Ed25519HolderSigned_Accepts()
    {
        // The realistic default-wallet path: an Ed25519 holder key signs its own KB-JWT (EdDSA).
        // Verified via BouncyCastle through the same alg-dispatched VerifyJwsSignature.
        var bundle = TestVpFactory.MintServerCustodyEd25519(
            Vct, VpValidatorTestHarness.Claims(("givenName", "Sarah")),
            VpValidatorTestHarness.ClientId, VpValidatorTestHarness.Nonce);

        var outcome = await BuildValidator().ValidateAsync(
            VpValidatorTestHarness.Session(), bundle.VpToken, delegationCredential: null);

        outcome.Accepted.Should().BeTrue(string.Join(", ", outcome.Errors));
        outcome.DisclosedClaims.Should().ContainKey("givenName");
    }

    [Fact]
    public async Task ValidateAsync_ServerCustody_DeviceSignedNoDelegation_RejectsNamed_KeyBindingMismatch()
    {
        // The wrong-key negative on the REAL path: cnf is the holder key but the KB-JWT was signed
        // by a DIFFERENT device key with no delegation to legitimise it. Must reject LOUDLY with a
        // named error DISTINCT from the delegation-model errors, and emit zero claims.
        var bundle = TestVpFactory.MintServerCustodyP256(
            Vct, VpValidatorTestHarness.Claims(("givenName", "Sarah")),
            VpValidatorTestHarness.ClientId, VpValidatorTestHarness.Nonce,
            kbSignedByHolder: false);

        var outcome = await BuildValidator().ValidateAsync(
            VpValidatorTestHarness.Session(), bundle.VpToken, delegationCredential: null);

        outcome.Accepted.Should().BeFalse();
        outcome.Errors.Should().Contain(e =>
                e.Contains("Key binding mismatch", StringComparison.Ordinal) &&
                e.Contains("cnf key", StringComparison.Ordinal),
            "a KB-JWT not signed by the credential's cnf key must be named, not conflated with device-key errors");
        outcome.Errors.Should().NotContain(e => e.Contains("device key", StringComparison.OrdinalIgnoreCase),
            "server-custody has no device key — the error must not blame one");
        outcome.DisclosedClaims.Should().BeEmpty();
    }

    [Fact]
    public async Task ValidateAsync_ServerCustody_Ed25519DeviceSigned_RejectsNamed_KeyBindingMismatch()
    {
        // Same negative on the Ed25519-holder path: holder cnf is OKP, KB-JWT signed by a P-256
        // device key. Reject named, zero claims.
        var bundle = TestVpFactory.MintServerCustodyEd25519(
            Vct, VpValidatorTestHarness.Claims(("givenName", "Sarah")),
            VpValidatorTestHarness.ClientId, VpValidatorTestHarness.Nonce,
            kbSignedByHolder: false);

        var outcome = await BuildValidator().ValidateAsync(
            VpValidatorTestHarness.Session(), bundle.VpToken, delegationCredential: null);

        outcome.Accepted.Should().BeFalse();
        outcome.Errors.Should().Contain(e => e.Contains("Key binding mismatch", StringComparison.Ordinal));
        outcome.DisclosedClaims.Should().BeEmpty();
    }

    [Fact]
    public async Task ValidateAsync_ServerCustody_MissingCnf_NoDelegation_RejectsNamed()
    {
        // LOUD failure: a credential with no cnf.jwk presented server-custody (no delegation) has no
        // holder key to bind the KB-JWT to. Rejected by name at the cnf-extraction step.
        var issuer = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        var (disclosure, hash) = TestVpFactory.MintDisclosure(
            "givenName", JsonSerializer.SerializeToElement("Sarah"));
        var credentialPayload = new Dictionary<string, object>
        {
            ["iss"] = "did:sorcha:org:test",
            ["iat"] = DateTimeOffset.UtcNow.ToUnixTimeSeconds(),
            ["vct"] = Vct,
            ["_sd"] = new[] { hash },
            ["_sd_alg"] = "sha-256",
            // no cnf
        };
        var credentialJwt = TestVpFactory.SignEs256(
            new Dictionary<string, object> { ["alg"] = "ES256", ["typ"] = "dc+sd-jwt" },
            credentialPayload, issuer);
        // A syntactically-valid KB-JWT (its signer is irrelevant — cnf extraction rejects first).
        var kbJwt = TestVpFactory.SignEs256(
            new Dictionary<string, object> { ["alg"] = "ES256", ["typ"] = "kb+jwt" },
            new Dictionary<string, object>
            {
                ["iat"] = DateTimeOffset.UtcNow.ToUnixTimeSeconds(),
                ["exp"] = DateTimeOffset.UtcNow.AddSeconds(120).ToUnixTimeSeconds(),
                ["aud"] = VpValidatorTestHarness.ClientId,
                ["nonce"] = VpValidatorTestHarness.Nonce,
            },
            ECDsa.Create(ECCurve.NamedCurves.nistP256));
        var vpToken = $"{credentialJwt}~{disclosure}~{kbJwt}";

        var outcome = await BuildValidator().ValidateAsync(
            VpValidatorTestHarness.Session(), vpToken, delegationCredential: null);

        outcome.Accepted.Should().BeFalse();
        outcome.Errors.Should().Contain(e => e.Contains("cnf.jwk", StringComparison.OrdinalIgnoreCase),
            "a credential missing its holder binding must be named, never silently accepted");
        outcome.DisclosedClaims.Should().BeEmpty();
    }

    // ─────────────────────────── I-2: the issuer-signature leg ───────────────────────────────────

    [Fact]
    public async Task ValidateAsync_ServerCustody_IssuerKeyResolves_RequireIssuerSignature_Accepts_IssuerVerified()
    {
        // I-2 (a): with RequireIssuerSignature ON and the issuer key RESOLVABLE, a holder-signed
        // delegation-free present passes both gates and reports the issuer signature verified.
        var bundle = TestVpFactory.MintServerCustodyP256(
            Vct, VpValidatorTestHarness.Claims(("givenName", "Sarah")),
            VpValidatorTestHarness.ClientId, VpValidatorTestHarness.Nonce);

        var registry = new JwkRegistryIssuerKeyResolver();
        registry.Register(
            bundle.Issuer,
            JsonDocument.Parse(TestVpFactory.ToJwk(bundle.IssuerKey)).RootElement);

        var outcome = await BuildValidator(registry, requireIssuerSignature: true)
            .ValidateAsync(VpValidatorTestHarness.Session(), bundle.VpToken, delegationCredential: null);

        outcome.Accepted.Should().BeTrue(string.Join(", ", outcome.Errors));
        outcome.IssuerSignature.Should().Be(IssuerSignatureStatus.Verified);
        outcome.Layers.Should().Contain(l =>
            l.Layer == ValidationLayer.IssuerSignature && l.Status == VerificationStatus.Verified);
    }

    [Fact]
    public async Task ValidateAsync_ServerCustody_IssuerKeyUnresolved_RequireIssuerSignature_RejectsNamed()
    {
        // I-2 (b): with RequireIssuerSignature ON but NO resolvable issuer key, the second gate must
        // reject LOUDLY, naming the requirement — proving the issuer leg is enforced, not silent.
        var bundle = TestVpFactory.MintServerCustodyP256(
            Vct, VpValidatorTestHarness.Claims(("givenName", "Sarah")),
            VpValidatorTestHarness.ClientId, VpValidatorTestHarness.Nonce);

        var outcome = await BuildValidator(new OptOutIssuerKeyResolver(), requireIssuerSignature: true)
            .ValidateAsync(VpValidatorTestHarness.Session(), bundle.VpToken, delegationCredential: null);

        outcome.Accepted.Should().BeFalse();
        outcome.Errors.Should().Contain(e => e.Contains("RequireIssuerSignature", StringComparison.Ordinal),
            "an unresolved issuer key under RequireIssuerSignature must name the requirement it failed");
        outcome.Layers.Should().Contain(l =>
            l.Layer == ValidationLayer.IssuerSignature && l.Status == VerificationStatus.Failed);
    }
}
