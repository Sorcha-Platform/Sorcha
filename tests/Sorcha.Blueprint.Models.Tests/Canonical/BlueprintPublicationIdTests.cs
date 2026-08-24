// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

using System.Security.Cryptography;
using System.Text;
using FluentAssertions;
using Sorcha.Blueprint.Models.Canonical;

namespace Sorcha.Blueprint.Models.Tests.Canonical;

/// <summary>
/// The publication-id preimage (Feature 195, contracts/publication-identity.md §1).
/// </summary>
/// <remarks>
/// Each test here corresponds to one field of the preimage, and each exists because dropping that
/// field produces a plausible-looking id that is wrong in a specific way. The mutations are recorded
/// in <c>specs/195-blueprint-definition-identity/mutations.md</c>.
/// </remarks>
public class BlueprintPublicationIdTests
{
    private const string Register = "b21d862d7aee471c89f844defb7fd108";
    private const string Blueprint = "golden-vector-blueprint";
    private const string Canonical = """{"a":1,"id":"golden-vector-blueprint"}""";

    [Fact]
    public void Compute_IsDeterministic()
    {
        BlueprintPublicationId.Compute(Register, Blueprint, Canonical)
            .Should().Be(BlueprintPublicationId.Compute(Register, Blueprint, Canonical));
    }

    [Fact]
    public void Compute_ReturnsLowercaseHexSha256()
    {
        var id = BlueprintPublicationId.Compute(Register, Blueprint, Canonical);

        id.Should().MatchRegex("^[0-9a-f]{64}$",
            "the value is a transaction id — it must be safe in URLs, Mongo keys and Redis keys");
    }

    /// <summary>
    /// The register must be in the preimage. A definition published to two registers is
    /// byte-identical <b>by construction</b> — same template, same model, same serializer — so
    /// without this the same id would name two different ledger facts and every
    /// <c>(registerId, txId)</c> lookup, receipt and inclusion proof would be ambiguous.
    /// </summary>
    [Fact]
    public void Compute_SameDefinitionOnTwoRegisters_ProducesTwoIds()
    {
        var a = BlueprintPublicationId.Compute("register-one", Blueprint, Canonical);
        var b = BlueprintPublicationId.Compute("register-two", Blueprint, Canonical);

        a.Should().NotBe(b);
    }

    [Fact]
    public void Compute_SameContentDifferentBlueprint_ProducesTwoIds()
    {
        var a = BlueprintPublicationId.Compute(Register, "blueprint-one", Canonical);
        var b = BlueprintPublicationId.Compute(Register, "blueprint-two", Canonical);

        a.Should().NotBe(b);
    }

    [Fact]
    public void Compute_DifferentContent_ProducesTwoIds()
    {
        var a = BlueprintPublicationId.Compute(Register, Blueprint, """{"a":1}""");
        var b = BlueprintPublicationId.Compute(Register, Blueprint, """{"a":2}""");

        a.Should().NotBe(b);
    }

    /// <summary>
    /// The domain tag must be in the preimage. <c>InstanceIdentity.Derive</c> is already
    /// <c>SHA-256(registerId ␟ blueprintId ␟ startingActionTxHash)</c> with the same 0x1F separator —
    /// so an untagged publication id would be the <b>same preimage construction sharing its first two
    /// fields</b>, and the two kinds of identity would be indistinguishable by shape.
    /// </summary>
    [Fact]
    public void Compute_IsDomainSeparatedFromTheInstanceIdentityConstruction()
    {
        // The untagged three-field construction, exactly as InstanceIdentity.Derive builds it.
        var untagged = UntaggedThreeField(Register, Blueprint, Canonical);

        BlueprintPublicationId.Compute(Register, Blueprint, Canonical)
            .Should().NotBe(untagged,
                "without the domain tag a publication id and an instance id are the same function of " +
                "the same first two fields");
    }

    /// <summary>
    /// The separator must be a real separator. Without one, <c>("ab","c")</c> and <c>("a","bc")</c>
    /// concatenate identically and two different publications collide.
    /// </summary>
    [Fact]
    public void Compute_FieldBoundariesAreUnambiguous()
    {
        var a = BlueprintPublicationId.Compute("ab", "c", Canonical);
        var b = BlueprintPublicationId.Compute("a", "bc", Canonical);

        a.Should().NotBe(b);
    }

    [Fact]
    public void Compute_RejectsMissingInputs()
    {
        FluentActions.Invoking(() => BlueprintPublicationId.Compute("", Blueprint, Canonical))
            .Should().Throw<ArgumentException>();
        FluentActions.Invoking(() => BlueprintPublicationId.Compute(Register, " ", Canonical))
            .Should().Throw<ArgumentException>();
        FluentActions.Invoking(() => BlueprintPublicationId.Compute(Register, Blueprint, null!))
            .Should().Throw<ArgumentException>();
    }

    /// <summary>
    /// Pins the exact preimage, so a change to the construction is a change to this test rather than
    /// a silent re-identification of every definition on every register.
    /// </summary>
    [Fact]
    public void Compute_MatchesTheDocumentedPreimage()
    {
        const byte sep = 0x1F;
        using var buffer = new MemoryStream();
        Write(buffer, "sorcha:blueprint-publication:v1");
        buffer.WriteByte(sep);
        Write(buffer, Register);
        buffer.WriteByte(sep);
        Write(buffer, Blueprint);
        buffer.WriteByte(sep);
        Write(buffer, Canonical);

        var expected = Convert.ToHexStringLower(SHA256.HashData(buffer.ToArray()));

        BlueprintPublicationId.Compute(Register, Blueprint, Canonical).Should().Be(expected);
    }

    private static string UntaggedThreeField(string a, string b, string c)
    {
        const byte sep = 0x1F;
        using var buffer = new MemoryStream();
        Write(buffer, a);
        buffer.WriteByte(sep);
        Write(buffer, b);
        buffer.WriteByte(sep);
        Write(buffer, c);
        return Convert.ToHexStringLower(SHA256.HashData(buffer.ToArray()));
    }

    private static void Write(Stream target, string value)
    {
        var bytes = Encoding.UTF8.GetBytes(value);
        target.Write(bytes, 0, bytes.Length);
    }
}
