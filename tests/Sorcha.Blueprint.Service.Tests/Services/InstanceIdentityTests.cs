// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

using Sorcha.Blueprint.Service.Services.Implementation;

namespace Sorcha.Blueprint.Service.Tests.Services;

/// <summary>
/// Foundational tests for the Feature 145 deterministic, ledger-anchored instance identity
/// (data-model Entity 4). The id must be reproducible on every node from on-ledger facts and
/// must not be ambiguous under field-boundary collisions.
/// </summary>
public class InstanceIdentityTests
{
    [Fact]
    public void Derive_SameInputs_ProducesSameId()
    {
        var a = InstanceIdentity.Derive("reg-1", "bp-1", "txhash-1");
        var b = InstanceIdentity.Derive("reg-1", "bp-1", "txhash-1");

        a.Should().Be(b);
    }

    [Fact]
    public void Derive_DifferentStartingTx_ProducesDifferentId()
    {
        var first = InstanceIdentity.Derive("reg-1", "bp-1", "txhash-1");
        var second = InstanceIdentity.Derive("reg-1", "bp-1", "txhash-2");

        first.Should().NotBe(second);
    }

    [Fact]
    public void Derive_OutputIsLowercaseHexSha256()
    {
        var id = InstanceIdentity.Derive("reg-1", "bp-1", "txhash-1");

        id.Should().HaveLength(64);
        id.Should().MatchRegex("^[0-9a-f]{64}$");
    }

    [Fact]
    public void Derive_FieldBoundaryShift_DoesNotCollide()
    {
        // Without a field separator, ("ab","c",...) and ("a","bc",...) would hash identical
        // concatenations. The 0x1F separator must keep them distinct.
        var x = InstanceIdentity.Derive("ab", "c", "tx");
        var y = InstanceIdentity.Derive("a", "bc", "tx");

        x.Should().NotBe(y);
    }

    [Theory]
    [InlineData("", "bp", "tx")]
    [InlineData("reg", "", "tx")]
    [InlineData("reg", "bp", "")]
    [InlineData("reg", "bp", "   ")]
    public void Derive_MissingInput_Throws(string registerId, string blueprintId, string startingActionTxHash)
    {
        var act = () => InstanceIdentity.Derive(registerId, blueprintId, startingActionTxHash);

        act.Should().Throw<ArgumentException>();
    }
}
