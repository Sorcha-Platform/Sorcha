// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

using FluentAssertions;
using Sorcha.Tenant.Models.Persona;
using Sorcha.UI.Core.Models.Persona;
using Xunit;

namespace Sorcha.UI.Components.User.Tests.Persona;

/// <summary>
/// Issue #1271 (UT-008, point 2) — <c>PersonaAttribute&lt;T&gt;.Source</c> and <c>VerifiedBy</c> are
/// carried all the way to the UI and then dropped on the floor. Nothing rendered them, so the
/// citizen could not tell a value they had typed themselves from one backed by a credential.
/// <para>
/// That gap is not cosmetic. UT-001 was a wrongful rejection caused by exactly this confusion
/// between two different notions of "verified" — a self-asserted persona value versus a verified
/// account claim. The wording here is deliberately blunt about which one it is.
/// </para>
/// </summary>
public sealed class PersonaProvenanceTests
{
    [Fact]
    public void ASelfAssertedValueSaysSo_WithoutClaimingVerification()
    {
        var label = PersonaProvenance.Describe(PersonaAttributeSource.SelfAsserted, verifiedBy: null);

        label.Should().Be("You entered this");
        label.Should().NotContainEquivalentOf("verified",
            "the whole point is that a self-asserted value has NOT been verified by anyone");
    }

    [Fact]
    public void ACredentialBackedValueNamesItsIssuer()
    {
        var label = PersonaProvenance.Describe(
            PersonaAttributeSource.VerifiedCredential, verifiedBy: "did:sorcha:aias");

        label.Should().Be("Verified by did:sorcha:aias");
    }

    [Fact]
    public void ACredentialBackedValueWithNoIssuerStillReadsAsVerified()
    {
        // VerifiedBy is nullable on the contract, so the label must not degrade into
        // "Verified by " with a dangling preposition.
        PersonaProvenance.Describe(PersonaAttributeSource.VerifiedCredential, verifiedBy: null)
            .Should().Be("Verified by a credential");
    }

    [Fact]
    public void AnUnknownSourceIsDescribedAsSelfAsserted_TheSaferClaim()
    {
        // Overclaiming verification is the dangerous direction: it is what let a self-asserted value
        // read as assured. An unrecognised source therefore falls back to the weaker statement.
        PersonaProvenance.Describe((PersonaAttributeSource)99, verifiedBy: null)
            .Should().Be("You entered this");
    }
}
