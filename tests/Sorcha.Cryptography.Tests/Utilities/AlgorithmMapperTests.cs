// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

using FluentAssertions;

using Sorcha.Cryptography.Enums;
using Sorcha.Cryptography.Utilities;

using Xunit;

namespace Sorcha.Cryptography.Tests.Utilities;

/// <summary>
/// Tests for <see cref="AlgorithmMapper"/> alias handling. Covers the "ES256" alias — the
/// JOSE/COSE name for ECDSA-P256-SHA256 used by <c>OrgIssuerCertKeyService</c> (Feature 181
/// US4/US5 HAIP co-key derivation) — which was missing from the <see cref="WalletNetworks.NISTP256"/>
/// alias set even though the mapper's own XML doc claims to support "common aliases for each
/// algorithm".
/// </summary>
public class AlgorithmMapperTests
{
    [Theory]
    [InlineData("ES256")]
    [InlineData("es256")]
    [InlineData("Es256")]
    public void TryParseAlgorithm_Es256Alias_ParsesToNistP256(string algorithmName)
    {
        var parsed = AlgorithmMapper.TryParseAlgorithm(algorithmName, out var network);

        parsed.Should().BeTrue("ES256 is the JOSE/COSE name for ECDSA-P256-SHA256 and must be accepted");
        network.Should().Be(WalletNetworks.NISTP256);
    }

    [Theory]
    [InlineData("ES256")]
    [InlineData("es256")]
    public void ParseAlgorithm_Es256Alias_ReturnsNistP256(string algorithmName)
    {
        var network = AlgorithmMapper.ParseAlgorithm(algorithmName);

        network.Should().Be(WalletNetworks.NISTP256);
    }
}
