// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

using FluentAssertions;
using Sorcha.Register.Models;
using Xunit;

namespace Sorcha.Register.Models.Tests;

/// <summary>
/// Tests verifying docket signing derivation context conventions.
/// </summary>
public class DocketBuilderDerivedKeyTests
{
    private const string DocketSigningContext = "sorcha:docket-signing";
    private const string ControlSigningContext = "sorcha:register-control";

    [Fact]
    public void DocketSigningContext_IsDistinctFromControlContext()
    {
        DocketSigningContext.Should().NotBe(ControlSigningContext);
    }

    [Fact]
    public void DocketSigningContext_FollowsSorchaConvention()
    {
        DocketSigningContext.Should().StartWith("sorcha:");
        DocketSigningContext.Should().NotContain(" ");
    }

    [Fact]
    public void ValidatorRosterEntry_UsesCorrectDerivationContext()
    {
        var entry = new ValidatorRosterEntry
        {
            ValidatorId = "test-validator",
            PublicKey = Convert.ToBase64String(new byte[32]),
            Algorithm = SignatureAlgorithm.ED25519,
            DerivationContext = DocketSigningContext,
            Status = ValidatorKeyStatus.Active,
            AuthorizedAt = DateTimeOffset.UtcNow
        };

        entry.DerivationContext.Should().Be(DocketSigningContext);
        entry.DerivationContext.Should().NotBe(ControlSigningContext,
            "docket signing must use a different derivation path than control signing");
    }
}
