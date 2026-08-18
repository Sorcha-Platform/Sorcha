// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

using FluentAssertions;

using Sorcha.Cryptography.Utilities;
using Sorcha.Register.Service.Services;

using Xunit;

namespace Sorcha.Register.Service.Tests.Governance;

/// <summary>
/// Issue #1464 — a governance-added roster member is keyed from its own subject DID.
/// </summary>
/// <remarks>
/// <para>
/// Both <c>Add</c> sites used to write <c>PublicKey = string.Empty</c>, on the stated assumption
/// that the key would be "recorded when they first attest". No attest path was ever built, so the
/// member stayed unkeyed forever — and since roster authority is matched by key, it could never
/// sign a governance transaction. Transferring ownership to one made the register permanently
/// ungovernable (demonstrated live on n1).
/// </para>
/// <para>
/// The fix needs no new proposal field and no join flow, because a classical <c>ws1</c> address is
/// Bech32 over <c>[network][publicKey]</c> — the key IS the identity. These tests prove the
/// derivation round-trips against a REAL address produced by the platform's own encoder, rather
/// than against a hand-written fixture that could encode the same misunderstanding as the code.
/// </para>
/// </remarks>
public class RosterKeyDerivationTests
{
    private static readonly WalletUtilities Wallets = new();

    private static byte[] SampleEd25519Key(byte seed) =>
        [.. Enumerable.Range(0, 32).Select(i => (byte)(seed + i))];

    [Fact]
    public void DerivedKeyRoundTripsTheAddressItCameFrom()
    {
        var publicKey = SampleEd25519Key(7);
        var address = Wallets.PublicKeyToWallet(publicKey, network: 0);
        address.Should().NotBeNull("the platform's own encoder must produce an address for a valid key");

        var derived = RosterKeyDerivation.TryDerivePublicKey($"did:sorcha:w:{address}");

        derived.Should().NotBeNull();
        Convert.FromBase64String(derived!).Should().Equal(publicKey,
            "the key recovered from the DID must be byte-identical to the one the address encodes — "
            + "anything else would put a non-matching key on the roster, which is #1464 again");
    }

    [Fact]
    public void TheDerivedEncodingIsWhatGovernanceKeyMatcherAccepts()
    {
        // Deriving the right BYTES is not enough: the roster stores a STRING, and authority is
        // decided by GovernanceKeyMatcher decoding it. A derivation that used a different encoding
        // would round-trip in the test above and still never match a signature.
        var publicKey = SampleEd25519Key(11);
        var address = Wallets.PublicKeyToWallet(publicKey, network: 0);

        var derived = RosterKeyDerivation.TryDerivePublicKey($"did:sorcha:w:{address}");

        Sorcha.Register.Models.GovernanceKeyMatcher.Matches(derived, publicKey)
            .Should().BeTrue("the stored form must match the raw key a transaction signature carries");
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("did:sorcha:org:something")]      // org DID, not wallet-bearing
    [InlineData("did:sorcha:genesis:validator")]  // the SSR ceremony subject
    [InlineData("did:sorcha:w:")]                 // wallet DID with no address
    [InlineData("did:sorcha:w:not-an-address")]
    public void ASubjectThatCannotBeKeyedReturnsNull(string? subject)
    {
        // Null means "cannot be keyed from the identity alone", never "any key will do". The caller
        // leaves the attestation unkeyed and the ApplyOperation guard refuses to promote it.
        RosterKeyDerivation.TryDerivePublicKey(subject).Should().BeNull();
    }

    [Fact]
    public void APqcAddressIsRefusedRatherThanKeyedWithItsHash()
    {
        // A ws2 address is Bech32m over [network][SHA-256(publicKey)]. Storing that hash in
        // PublicKey would compare a hash against raw signature bytes in GovernanceKeyMatcher,
        // never match, and silently rebuild the exact ungovernable state this fix removes — so it
        // must come back null and fail closed instead.
        RosterKeyDerivation.TryDerivePublicKey("did:sorcha:w:ws21qsomepqcaddressthatisnotclassical")
            .Should().BeNull();
    }

    [Fact]
    public void ExtractWalletAddressAcceptsOnlyTheWalletDidForm()
    {
        RosterKeyDerivation.ExtractWalletAddress("did:sorcha:w:ws11qabc").Should().Be("ws11qabc");
        RosterKeyDerivation.ExtractWalletAddress("did:sorcha:org:abc").Should().BeNull();
        RosterKeyDerivation.ExtractWalletAddress("ws11qabc").Should().BeNull();
    }
}
