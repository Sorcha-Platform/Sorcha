// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

using Sorcha.ServiceClients.Http.Utilities;

namespace Sorcha.Wallet.Service.Services.Implementation;

/// <summary>
/// Expresses an organisation's <c>sorcha:citizen-status-signing</c> key as a <c>did:key</c>, for
/// organisations with no issuer DID of their own (#1759). A <c>did:key</c> resolves with no network
/// call and no published document, which is what makes it usable for an org that has no wallet.
/// </summary>
internal static class StatusSignerDidKey
{
    /// <summary>
    /// Returns the <c>did:key</c> and the <c>kid</c> (<c>did:key:z…#z…</c>, the verification method id
    /// the did:key resolver publishes) for a wallet public key.
    /// </summary>
    /// <param name="walletAlgorithm">The wallet algorithm (<c>ED25519</c> or a P-256 alias).</param>
    /// <param name="publicKey">Ed25519: 32 bytes. P-256: 64-byte X‖Y, or 65 bytes with the 0x04 prefix.</param>
    public static (string Did, string Kid) FromPublicKey(string walletAlgorithm, byte[] publicKey)
    {
        ArgumentNullException.ThrowIfNull(publicKey);

        var alg = StatusListTokenSigner.ToJoseAlgorithm(walletAlgorithm);
        byte[] keyBytes = alg switch
        {
            "EdDSA" when publicKey.Length == 32 => publicKey,
            "ES256" => Compress(publicKey),
            _ => throw new ArgumentException(
                $"A {walletAlgorithm} public key of {publicKey.Length} bytes cannot be expressed as a did:key.",
                nameof(publicKey)),
        };

        var multibase = Multicodec.ToMultibasePublicKey(alg == "EdDSA" ? "ED25519" : "P-256", keyBytes)
            ?? throw new InvalidOperationException($"No multicodec for {walletAlgorithm}.");
        var did = $"did:key:{multibase}";
        return (did, $"{did}#{multibase}");
    }

    /// <summary>SEC1 point compression — the form the did:key spec requires for P-256.</summary>
    private static byte[] Compress(byte[] publicKey)
    {
        var xy = publicKey.Length switch
        {
            64 => publicKey,
            65 when publicKey[0] == 0x04 => publicKey[1..],
            _ => throw new ArgumentException(
                $"A P-256 public key must be 64 bytes (X‖Y) or 65 (0x04‖X‖Y), not {publicKey.Length}.",
                nameof(publicKey)),
        };

        var compressed = new byte[33];
        compressed[0] = (byte)(0x02 | (xy[63] & 1));
        Buffer.BlockCopy(xy, 0, compressed, 1, 32);
        return compressed;
    }
}
