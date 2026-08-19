// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

using FluentAssertions;

using Sorcha.Cryptography.Utilities;
using Sorcha.Wallet.Contracts.Constants;

using Xunit;

namespace Sorcha.Register.Service.Tests.Governance;

/// <summary>
/// Issue #1464 — a governance <c>Add</c> leaves the member UNKEYED, and that is deliberate. This
/// test exists to stop the derivation being re-added.
/// </summary>
/// <remarks>
/// <para>
/// The tempting fix is: a classical <c>ws1</c> address is Bech32 over <c>[network][publicKey]</c>,
/// so "the key IS the identity" — derive it from the subject DID and the member is keyed with no
/// new proposal field, deterministically, and unforgeably. Every part of that is true, and it
/// derives the WRONG KEY.
/// </para>
/// <para>
/// The address encodes the wallet's <b>primary</b> key. Governance does not use the primary key: a
/// roster attestation records the organisation's <b>slot-100</b> key
/// (<c>sorcha:register-attestation</c>), <c>GovernanceSigningService</c> signs approvals there, and
/// <c>GovernanceKeyMatcher</c> authorises by matching it. Proven live on n1 (2026-08-18) — for one
/// wallet the address-derived key was <c>VHWQB/…</c> and the slot-100 key <c>thfb6l9P…</c>.
/// </para>
/// <para>
/// A derived key is therefore <b>non-empty and wrong</b>, which is strictly worse than empty: empty
/// fails loudly at the <c>ApplyOperation</c> promotion guard, while wrong passes every check and is
/// excluded silently at tally time as <c>KeyNotTheRosterKey</c>. The member looks seated and can
/// never act.
/// </para>
/// <para>
/// The slot-100 key cannot be recovered from the identity, and resolving it from a local wallet at
/// enactment would break determinism — nodes that do not host that wallet would fold different
/// bytes into the same transaction. It has to arrive IN the sealed proposal, proven by the target.
/// Until that exists, unkeyed-and-unpromotable is the correct, honest state.
/// </para>
/// </remarks>
public class GovernanceAddLeavesMemberUnkeyedTests
{
    private static readonly WalletUtilities Wallets = new();

    [Fact]
    public void TheAddressEncodesThePrimaryKey_WhichIsNotTheGovernanceKey()
    {
        // The structural fact the reverted fix got wrong, asserted so the reasoning is testable
        // rather than a comment: what a ws1 address yields is the key that MADE the address, and
        // governance signs at a different derivation path entirely.
        var primaryKey = Enumerable.Range(0, 32).Select(i => (byte)i).ToArray();
        var address = Wallets.PublicKeyToWallet(primaryKey, network: 0);
        address.Should().NotBeNull();

        var recovered = Wallets.WalletToPublicKey(address!);

        recovered.Should().NotBeNull();
        recovered!.Value.PublicKey.Should().Equal(primaryKey,
            "an address round-trips its OWN key — which is the primary key, not the slot-100 "
            + "governance key a roster attestation records");

        SorchaDerivationPaths.RegisterAttestation.Should().NotBeNullOrWhiteSpace(
            "governance keys live at a derivation path, which is exactly why they cannot be read "
            + "out of an address");
    }

    [Fact]
    public void NoDerivationHelperIsWiredIntoTheAddPath()
    {
        // Guards against re-introduction by name. RosterKeyDerivation was deleted; if it comes
        // back, whoever adds it has to face this test and the remarks above.
        var registerServiceAssembly = typeof(Sorcha.Register.Service.Services.IGovernanceSigningService).Assembly;

        registerServiceAssembly.GetTypes()
            .Where(t => t.Name.Contains("RosterKeyDerivation", StringComparison.Ordinal))
            .Should().BeEmpty(
                "deriving a roster key from the subject DID yields the wallet's PRIMARY key, not the "
                + "slot-100 governance key, so it seats a member that can never sign — see #1464");
    }
}
