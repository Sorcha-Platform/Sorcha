// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

using System.Reflection;

using FluentAssertions;

using Sorcha.Blueprint.Models;

namespace Sorcha.Blueprint.Models.Tests;

/// <summary>
/// Pins the wire values of the cross-boundary validation codes, and the invariants that keep the
/// two shared classes coherent.
/// </summary>
/// <remarks>
/// <para>
/// These strings are an external contract: they appear in operator-facing logs, in
/// <c>docs/</c>, in the blueprint-builder skill, and — critically — in a cross-service string
/// comparison (see <see cref="ValidationErrorCodes.ChainFork"/>). Asserting the literal here is
/// what makes the constant safe to reference everywhere else; the
/// <c>check-error-code-contract.ps1</c> gate exempts tests for exactly this reason.
/// </para>
/// </remarks>
public sealed class ValidationCodeContractTests
{
    [Theory]
    [InlineData(nameof(ValidationErrorCodes.OpenParticipantPrebound), "VAL_BP_010")]
    [InlineData(nameof(ValidationErrorCodes.SorchaLocalWalletRecipientUnknown), "VAL_BP_CRED_001")]
    [InlineData(nameof(ValidationErrorCodes.SorchaLocalWalletRejectNotTerminal), "VAL_BP_CRED_003")]
    [InlineData(nameof(ValidationErrorCodes.ChainFork), "VAL_CHAIN_FORK")]
    [InlineData(nameof(ValidationErrorCodes.RevocationInvalid), "VAL_REV_001")]
    public void ErrorCode_HasPinnedWireValue(string name, string expected)
    {
        ConstantValue(typeof(ValidationErrorCodes), name).Should().Be(expected);
    }

    [Theory]
    [InlineData(nameof(ValidationWarningCodes.ReviewLayoutUnknown), "WARN_BP_REVIEW_001")]
    [InlineData(nameof(ValidationWarningCodes.CredentialPortraitOversize), "WARN_CRED_PORTRAIT_OVERSIZE_001")]
    [InlineData(nameof(ValidationWarningCodes.SorchaLocalWalletImplicitDisclosure), "WARN_BP_CRED_002")]
    [InlineData(nameof(ValidationWarningCodes.UnconditionalIssuanceOnDecision), "WARN_BP_CRED_005")]
    public void WarningCode_HasPinnedWireValue(string name, string expected)
    {
        ConstantValue(typeof(ValidationWarningCodes), name).Should().Be(expected);
    }

    [Fact]
    public void ErrorCodes_AreAllVal_AndWarningCodes_AreAllWarn()
    {
        // The two classes are a taxonomy, not just two buckets: an operator filtering logs on the
        // WARN_ prefix must not miss a blocking error, and vice versa. WARN_BP_CRED_002 previously
        // sat in an *error* class purely because it shared a feature with its neighbours.
        Constants(typeof(ValidationErrorCodes)).Should()
            .OnlyContain(c => c.Value.StartsWith("VAL_", StringComparison.Ordinal),
                "ValidationErrorCodes holds blocking errors only");

        Constants(typeof(ValidationWarningCodes)).Should()
            .OnlyContain(c => c.Value.StartsWith("WARN_", StringComparison.Ordinal),
                "ValidationWarningCodes holds non-blocking warnings only");
    }

    [Fact]
    public void SharedCodes_AreUniqueAcrossBothClasses()
    {
        // One code, one meaning. A value declared twice under different names would let two call
        // sites diverge while both look correct.
        var all = Constants(typeof(ValidationErrorCodes))
            .Concat(Constants(typeof(ValidationWarningCodes)))
            .ToList();

        all.Select(c => c.Value).Should().OnlyHaveUniqueItems();
        all.Should().HaveCountGreaterThanOrEqualTo(8, "the gate guards whatever is declared here");
    }

    private static string ConstantValue(Type type, string name) =>
        (string)type.GetField(name, BindingFlags.Public | BindingFlags.Static)!.GetValue(null)!;

    private static List<(string Name, string Value)> Constants(Type type) =>
        type.GetFields(BindingFlags.Public | BindingFlags.Static)
            .Where(f => f.IsLiteral && f.FieldType == typeof(string))
            .Select(f => (f.Name, Value: (string)f.GetValue(null)!))
            .ToList();
}
