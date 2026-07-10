// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

using System.Globalization;
using System.Text;
using Org.BouncyCastle.Math;

namespace Sorcha.Cryptography.Secp256k1.Siwe;

/// <summary>Options for verifying an inbound SIWE proof (Sorcha as relying party, Feature 180).</summary>
/// <param name="ExpectedNonce">When set, the message's nonce must equal this (replay protection).</param>
/// <param name="ExpectedDomain">When set, the message's domain must equal this (case-insensitive).</param>
/// <param name="NowUtc">Clock for the validity-window check; defaults to <see cref="DateTimeOffset.UtcNow"/>.</param>
public sealed record SiweValidationOptions(string? ExpectedNonce = null, string? ExpectedDomain = null, DateTimeOffset? NowUtc = null);

/// <summary>The outcome of verifying a SIWE proof.</summary>
public sealed record SiweVerificationResult(bool Valid, string? Address, string? Reason);

/// <summary>
/// Verifies a Sign-In With Ethereum (EIP-4361) message + signature (Feature 180): parse → EIP-191 digest
/// → public-key recovery → the recovered address must equal the message's <c>address</c> → nonce / domain
/// / validity-window checks. Fail-closed and never throws — any problem yields <c>Valid=false</c>.
/// </summary>
public static class SiweVerifier
{
    /// <summary>Verify <paramref name="message"/> against its 65-byte <paramref name="signature"/> and <paramref name="options"/>.</summary>
    public static SiweVerificationResult Verify(string message, ReadOnlySpan<byte> signature, SiweValidationOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);

        if (!SiweFormatter.TryParse(message, out var msg))
        {
            return new SiweVerificationResult(false, null, "Malformed SIWE message.");
        }

        if (signature.Length != 65)
        {
            return new SiweVerificationResult(false, null, "Signature must be 65 bytes (r‖s‖v).");
        }

        try
        {
            var digest = Eip191.PersonalSignDigest(Encoding.UTF8.GetBytes(message));
            var r = new BigInteger(1, signature[..32].ToArray());
            var s = new BigInteger(1, signature[32..64].ToArray());
            var v = signature[64];
            var recoveryId = v >= 27 ? v - 27 : v;

            var key = Secp256k1Recovery.RecoverFromDigest(digest, r, s, recoveryId);
            if (key is null)
            {
                return new SiweVerificationResult(false, null, "Could not recover the signer.");
            }

            var recovered = EthereumAddress.FromPublicKey(key);
            if (!string.Equals(recovered, msg.Address, StringComparison.OrdinalIgnoreCase))
            {
                return new SiweVerificationResult(false, null, "Recovered address does not match the message address.");
            }

            if (options.ExpectedNonce is { Length: > 0 } nonce && !string.Equals(nonce, msg.Nonce, StringComparison.Ordinal))
            {
                return new SiweVerificationResult(false, null, "Nonce mismatch.");
            }

            if (options.ExpectedDomain is { Length: > 0 } domain && !string.Equals(domain, msg.Domain, StringComparison.OrdinalIgnoreCase))
            {
                return new SiweVerificationResult(false, null, "Domain mismatch.");
            }

            var now = options.NowUtc ?? DateTimeOffset.UtcNow;
            if (TryParseTimestamp(msg.ExpirationTime, out var exp) && now > exp)
            {
                return new SiweVerificationResult(false, null, "Message has expired.");
            }

            if (TryParseTimestamp(msg.NotBefore, out var notBefore) && now < notBefore)
            {
                return new SiweVerificationResult(false, null, "Message is not yet valid.");
            }

            return new SiweVerificationResult(true, recovered, null);
        }
        catch
        {
            return new SiweVerificationResult(false, null, "Verification error.");
        }
    }

    private static bool TryParseTimestamp(string? value, out DateTimeOffset timestamp)
    {
        timestamp = default;
        return value is { Length: > 0 }
            && DateTimeOffset.TryParse(value, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind, out timestamp);
    }
}
