// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

using System;
using System.Text;
using FluentAssertions;
using Sorcha.Wallet.Pwa.Services;
using Xunit;

namespace Sorcha.Wallet.Pwa.Tests.Services;

public sealed class JwtIdentityTests
{
    private static string B64Url(string s)
        => Convert.ToBase64String(Encoding.UTF8.GetBytes(s)).TrimEnd('=').Replace('+', '-').Replace('/', '_');

    private static string Jwt(string payloadJson)
        => $"{B64Url("{\"alg\":\"none\"}")}.{B64Url(payloadJson)}.sig";

    [Fact]
    public void ExtractSubject_ReturnsSub()
        => JwtIdentity.ExtractSubject(Jwt("{\"sub\":\"user-123\",\"aud\":\"sorcha:consumer\"}"))
            .Should().Be("user-123");

    [Fact]
    public void ExtractSubject_NoSub_ReturnsNull()
        => JwtIdentity.ExtractSubject(Jwt("{\"aud\":\"sorcha:consumer\"}")).Should().BeNull();

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("not-a-jwt")]
    [InlineData("only.two")]                 // 2 segments but middle isn't valid base64url JSON
    [InlineData("aaa.bbb.ccc")]              // garbage payload
    public void ExtractSubject_Malformed_ReturnsNull(string? jwt)
        => JwtIdentity.ExtractSubject(jwt).Should().BeNull();

    [Fact]
    public void ExtractSubject_SubNotAString_ReturnsNull()
        => JwtIdentity.ExtractSubject(Jwt("{\"sub\":12345}")).Should().BeNull();
}
