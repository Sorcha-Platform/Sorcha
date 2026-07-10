// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

using Sorcha.Cryptography.Secp256k1.Siwe;

namespace Sorcha.Cryptography.Secp256k1.Tests;

public class SiweFormatterTests
{
    // The EIP-4361 specification's example message (verbatim), LF-joined.
    private static readonly string SpecExample = string.Join("\n",
    [
        "example.com wants you to sign in with your Ethereum account:",
        "0xC02aaA39b223FE8D0A0e5C4F27eAD9083C756Cc2",
        "",
        "I accept the ExampleOrg Terms of Service: https://example.com/tos",
        "",
        "URI: https://example.com/login",
        "Version: 1",
        "Chain ID: 1",
        "Nonce: 32891756",
        "Issued At: 2021-09-30T16:25:24Z",
        "Resources:",
        "- ipfs://bafybeiemxf5abjwjbikoz4mc3a3dla6ual3jsgpdr4cjr3oz3evfyavhwq/",
        "- https://example.com/my-web2-claim.json",
    ]);

    [Fact]
    public void TryParse_SpecExample_ParsesAllFields()
    {
        SiweFormatter.TryParse(SpecExample, out var msg).Should().BeTrue();

        msg.Domain.Should().Be("example.com");
        msg.Address.Should().Be("0xC02aaA39b223FE8D0A0e5C4F27eAD9083C756Cc2");
        msg.Statement.Should().Be("I accept the ExampleOrg Terms of Service: https://example.com/tos");
        msg.Uri.Should().Be("https://example.com/login");
        msg.Version.Should().Be("1");
        msg.ChainId.Should().Be(1);
        msg.Nonce.Should().Be("32891756");
        msg.IssuedAt.Should().Be("2021-09-30T16:25:24Z");
        msg.Resources.Should().Equal(
            "ipfs://bafybeiemxf5abjwjbikoz4mc3a3dla6ual3jsgpdr4cjr3oz3evfyavhwq/",
            "https://example.com/my-web2-claim.json");
    }

    [Fact]
    public void Format_SpecExample_ByteIdenticalRoundTrip()
    {
        SiweFormatter.TryParse(SpecExample, out var msg).Should().BeTrue();
        SiweFormatter.Format(msg).Should().Be(SpecExample);
    }

    [Fact]
    public void FormatParse_MinimalMessage_NoStatementNoOptionals_RoundTrips()
    {
        var msg = new SiweMessage
        {
            Domain = "app.test",
            Address = "0xC02aaA39b223FE8D0A0e5C4F27eAD9083C756Cc2",
            Uri = "https://app.test/login",
            Version = "1",
            ChainId = 1,
            Nonce = "deadbeef",
            IssuedAt = "2026-07-10T00:00:00Z"
        };

        var text = SiweFormatter.Format(msg);
        SiweFormatter.TryParse(text, out var back).Should().BeTrue();

        back.Domain.Should().Be(msg.Domain);
        back.Address.Should().Be(msg.Address);
        back.Statement.Should().BeNull();
        back.Uri.Should().Be(msg.Uri);
        back.ChainId.Should().Be(1);
        back.Nonce.Should().Be("deadbeef");
        back.IssuedAt.Should().Be(msg.IssuedAt);
        SiweFormatter.Format(back).Should().Be(text);
    }

    [Fact]
    public void FormatParse_AllOptionalFields_RoundTrips()
    {
        var msg = new SiweMessage
        {
            Domain = "app.test",
            Address = "0xC02aaA39b223FE8D0A0e5C4F27eAD9083C756Cc2",
            Statement = "Sign in to app.test",
            Uri = "https://app.test/login",
            Version = "1",
            ChainId = 137,
            Nonce = "abc123",
            IssuedAt = "2026-07-10T00:00:00Z",
            ExpirationTime = "2026-07-10T01:00:00Z",
            NotBefore = "2026-07-10T00:00:00Z",
            RequestId = "req-1",
            Resources = ["https://app.test/scope"]
        };

        var text = SiweFormatter.Format(msg);
        SiweFormatter.TryParse(text, out var back).Should().BeTrue();
        SiweFormatter.Format(back).Should().Be(text);
        back.ExpirationTime.Should().Be("2026-07-10T01:00:00Z");
        back.NotBefore.Should().Be("2026-07-10T00:00:00Z");
        back.RequestId.Should().Be("req-1");
    }

    [Theory]
    [InlineData("not a siwe message")]
    [InlineData("example.com wants you to sign in with your Ethereum account:")] // truncated
    public void TryParse_Malformed_ReturnsFalse(string text)
    {
        SiweFormatter.TryParse(text, out _).Should().BeFalse();
    }
}
