// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

using Sorcha.Cryptography.Enums;
using Sorcha.Cryptography.Interfaces;
using Sorcha.Cryptography.Utilities;
using Sorcha.Validator.Core;

namespace Sorcha.Register.Service.Services.Implementation;

/// <summary>
/// The signature check the Register's receipt and bundle verify endpoints run (#1733).
/// </summary>
/// <remarks>
/// <para>
/// Both endpoints built <see cref="ReceiptValidator"/> WITHOUT a <see cref="SignatureVerifyFunc"/>,
/// which puts it in proof-only mode: the validator's signature on a receipt was never checked, while
/// the response still carried <c>receiptSignatureValid</c>. Invisible while no receipts existed; #1704
/// made receipts real, signed with the roster's <c>sorcha:docket-signing</c> key over the raw
/// canonical signing data.
/// </para>
/// <para>
/// The signature is checked over exactly those bytes — the wallet signs them as given
/// (<c>isPreHashed</c>), and <see cref="ReceiptValidator.BuildReceiptSigningData"/> rebuilds them.
/// Every failure mode answers false: an algorithm this platform does not know, a key or signature
/// that is not valid, or a crypto error. A check that cannot run must never read as a pass.
/// </para>
/// </remarks>
internal static class ReceiptSignatureVerification
{
    /// <summary>Creates the verify function over <paramref name="crypto"/>.</summary>
    public static SignatureVerifyFunc Create(ICryptoModule crypto) => (publicKey, data, signature, algorithm) =>
    {
        if (publicKey.Length == 0 || signature.Length == 0 || !AlgorithmMapper.TryParseAlgorithm(algorithm, out var network))
        {
            return false;
        }

        try
        {
            // ReceiptValidator's delegate is synchronous; the crypto module completes synchronously
            // for signature verification (CPU-bound, no I/O).
            return crypto.VerifyAsync(signature, data, (byte)network, publicKey).GetAwaiter().GetResult() == CryptoStatus.Success;
        }
        catch (Exception)
        {
            return false;
        }
    };
}
