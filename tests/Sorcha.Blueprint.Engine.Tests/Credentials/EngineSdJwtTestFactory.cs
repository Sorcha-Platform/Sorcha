// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

using System.Security.Cryptography;

using Sorcha.Blueprint.Engine.Credentials;
using Sorcha.Blueprint.Engine.Credentials.Sources;
using Sorcha.Blueprint.Models.Credentials;
using Sorcha.Cryptography.SdJwt;

namespace Sorcha.Blueprint.Engine.Tests.Credentials;

/// <summary>
/// Shared helpers for the feature-135 trust-flip tests: mints REAL signed SD-JWT VCs and
/// assembles a real <see cref="CredentialVerifier"/> wired through the real
/// <see cref="SdJwtVcFormatHandler"/> + <see cref="TrustEvaluator"/>. The only test doubles are
/// at genuine boundaries — <see cref="IIssuerKeyResolver"/> (key lookup), <see cref="IIssuerDirectory"/>
/// (register trust), and <see cref="IStatusListChecker"/> (revocation). Crypto is never mocked,
/// so the suites prove the honest signature path rather than the retired SignatureValid=false shortcut.
/// </summary>
internal static class EngineSdJwtTestFactory
{
    /// <summary>A minted SD-JWT VC together with the issuer public key needed to verify it.</summary>
    public sealed record MintedSdJwt(string Raw, byte[] IssuerPublicKey, string Algorithm, string IssuerId, string? Kid);

    /// <summary>
    /// Mints a real ES256-signed SD-JWT VC. <paramref name="vct"/> and any <paramref name="statusClaim"/>
    /// are always placed in the issuer payload (non-disclosable) so the format handler can read the
    /// credential type and status reference deterministically. <paramref name="dataClaims"/> are the
    /// selectively-disclosable subject claims; <paramref name="disclosable"/> defaults to all of them.
    /// </summary>
    public static MintedSdJwt MintEs256(
        string vct,
        string issuer,
        Dictionary<string, object>? dataClaims = null,
        IEnumerable<string>? disclosable = null,
        Dictionary<string, object>? statusClaim = null,
        string? kid = null)
    {
        using var ecdsa = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        var privateKey = ecdsa.ExportECPrivateKey();
        var publicKey = ecdsa.ExportSubjectPublicKeyInfo();

        dataClaims ??= new Dictionary<string, object>();
        // vct + status stay non-disclosable: type-match and status extraction read them from the payload.
        var allClaims = new Dictionary<string, object> { ["vct"] = vct };
        foreach (var (k, v) in dataClaims)
            allClaims[k] = v;
        if (statusClaim is not null)
            allClaims["status"] = statusClaim;

        var disclosableList = (disclosable ?? dataClaims.Keys).ToList();

        var svc = new SdJwtService();
        var token = svc.CreateTokenAsync(
            allClaims, disclosableList, issuer, subject: "did:sorcha:holder:test",
            privateKey, "ES256", kid: kid).GetAwaiter().GetResult();

        return new MintedSdJwt(token.RawToken, publicKey, "ES256", issuer, kid);
    }

    /// <summary>An IETF <c>status.status_list</c> claim object for a credential carrying a status reference.</summary>
    public static Dictionary<string, object> IetfStatusClaim(string uri, int index) =>
        new() { ["status_list"] = new Dictionary<string, object> { ["uri"] = uri, ["idx"] = index } };

    /// <summary>
    /// A fake key resolver returning a fixed public key + algorithm for any token. Mirrors the
    /// service-layer x5c→DID resolver without the network dependency.
    /// </summary>
    public sealed class FakeIssuerKeyResolver(byte[] publicKey, string algorithm, string? signingKeyId = null) : IIssuerKeyResolver
    {
        public bool ReturnNull { get; init; }

        public Task<IssuerKeyResolution?> ResolveAsync(string rawSdJwt, CancellationToken ct = default) =>
            Task.FromResult(ReturnNull
                ? null
                : new IssuerKeyResolution
                {
                    PublicKey = publicKey,
                    Algorithm = algorithm,
                    SigningKeyId = signingKeyId
                });
    }

    /// <summary>An issuer directory that resolves every issuer (register source vouches for anyone).</summary>
    public sealed class AlwaysResolvesDirectory : IIssuerDirectory
    {
        public IReadOnlyList<string> AssertionMethodKeyIds { get; init; } = [];
        public Task<IssuerDirectoryEntry> LookupAsync(string issuerId, CancellationToken ct = default) =>
            Task.FromResult(new IssuerDirectoryEntry
            {
                Resolved = true,
                AssertionMethodKeyIds = AssertionMethodKeyIds,
                RegisterHeight = 1
            });
    }

    /// <summary>A status checker returning a fixed bit for any reference.</summary>
    public sealed class FakeStatusChecker(StatusListBit bit) : IStatusListChecker
    {
        public Task<StatusListBit> CheckAsync(StatusReference statusRef, CancellationToken ct = default) =>
            Task.FromResult(bit);
    }

    /// <summary>Builds a real <see cref="SdJwtVcFormatHandler"/> for <paramref name="minted"/> with real crypto.</summary>
    public static SdJwtVcFormatHandler BuildHandler(
        MintedSdJwt minted,
        IIssuerDirectory? directory = null,
        IStatusListChecker? statusChecker = null,
        IIssuerKeyResolver? keyResolver = null)
    {
        directory ??= new AlwaysResolvesDirectory();
        keyResolver ??= new FakeIssuerKeyResolver(minted.IssuerPublicKey, minted.Algorithm, minted.Kid);

        var registry = new TrustResolverRegistry(
        [
            new RegisterTrustSourceResolver(directory),
            new DidAllowlistTrustSourceResolver(directory)
        ]);
        var evaluator = new TrustEvaluator(registry, statusChecker);
        return new SdJwtVcFormatHandler(new SdJwtService(), keyResolver, evaluator);
    }

    /// <summary>Flips one character in the issuer JWT signature segment to corrupt the signature.</summary>
    public static string TamperSignature(string rawSdJwt)
    {
        var parts = rawSdJwt.Split('~');
        var jwt = parts[0].Split('.');
        var sig = jwt[2];
        // Swap the first character for a different base64url char.
        var swapped = (sig[0] == 'A' ? 'B' : 'A') + sig[1..];
        jwt[2] = swapped;
        parts[0] = string.Join('.', jwt);
        return string.Join('~', parts);
    }
}
