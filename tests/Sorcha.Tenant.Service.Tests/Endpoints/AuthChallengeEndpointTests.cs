// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using FluentAssertions;
using Sorcha.Tenant.Service.Models;
using Sorcha.Tenant.Service.Models.Requests;
using Sorcha.Tenant.Service.Tests.Infrastructure;

namespace Sorcha.Tenant.Service.Tests.Endpoints;

/// <summary>
/// Integration tests for the auth-challenge endpoints (Feature 116).
/// Drives the initiate → verify cycle through the real HTTP pipeline using
/// <see cref="TenantServiceWebApplicationFactory"/>. Exercises the
/// ChallengeContext resolution path (sub claim → IIdentityRepository
/// fallback to PlatformUserId) since the test auth handler does not emit
/// a custom platform_user_id claim.
/// </summary>
public class AuthChallengeEndpointTests : IClassFixture<TenantServiceWebApplicationFactory>
{
    private readonly TenantServiceWebApplicationFactory _factory;

    public AuthChallengeEndpointTests(TenantServiceWebApplicationFactory factory)
    {
        _factory = factory;
    }

    [Fact]
    public async Task Initiate_AdminWithPasswordOnly_ReturnsPasswordMethod()
    {
        await _factory.SeedTestDataAsync();
        using var client = _factory.CreateAdminClient();

        var response = await client.PostAsJsonAsync("/api/auth/challenge/initiate",
            new ChallengeInitiateRequest(ScopedOperation.ChangePassword, PreferredMethod: null));

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var payload = await response.Content.ReadFromJsonAsync<ChallengeInitiateResponse>();
        payload.Should().NotBeNull();
        payload!.Method.Should().Be(ChallengeMethod.Password,
            "admin seed user has only a password set; ladder picks Password");
    }

    [Fact]
    public async Task Verify_CorrectPassword_ReturnsTokenWith300SecondTtl()
    {
        await _factory.SeedTestDataAsync();
        using var client = _factory.CreateAdminClient();

        var verifyResponse = await client.PostAsJsonAsync("/api/auth/challenge/verify",
            new ChallengeVerifyRequest(
                Method: ChallengeMethod.Password,
                ScopedOperation: ScopedOperation.ChangePassword,
                Proof: JsonDocument.Parse($$"""{"password":"{{TestDataSeeder.DefaultTestPassword}}"}""").RootElement));

        verifyResponse.StatusCode.Should().Be(HttpStatusCode.OK);
        var payload = await verifyResponse.Content.ReadFromJsonAsync<ChallengeVerifyResponse>();
        payload.Should().NotBeNull();
        payload!.Token.Should().StartWith("ch_");
        payload.ExpiresIn.Should().BeInRange(290, 300);
    }

    [Fact]
    public async Task Verify_WrongPassword_Returns401()
    {
        await _factory.SeedTestDataAsync();
        using var client = _factory.CreateAdminClient();

        var verifyResponse = await client.PostAsJsonAsync("/api/auth/challenge/verify",
            new ChallengeVerifyRequest(
                Method: ChallengeMethod.Password,
                ScopedOperation: ScopedOperation.ChangePassword,
                Proof: JsonDocument.Parse("""{"password":"WrongPassword!"}""").RootElement));

        verifyResponse.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task Verify_RequestedMethodNotEnrolled_Returns401()
    {
        await _factory.SeedTestDataAsync();
        using var client = _factory.CreateAdminClient();

        // Admin has no TOTP enrolled; asking to verify a TOTP code must fail.
        var response = await client.PostAsJsonAsync("/api/auth/challenge/verify",
            new ChallengeVerifyRequest(
                Method: ChallengeMethod.Totp,
                ScopedOperation: ScopedOperation.ChangePassword,
                Proof: JsonDocument.Parse("""{"code":"123456"}""").RootElement));

        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task Initiate_Anonymous_Returns401()
    {
        await _factory.SeedTestDataAsync();
        using var client = _factory.CreateUnauthenticatedClient();

        var response = await client.PostAsJsonAsync("/api/auth/challenge/initiate",
            new ChallengeInitiateRequest(ScopedOperation.ChangePassword, null));

        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task Verify_Anonymous_Returns401()
    {
        await _factory.SeedTestDataAsync();
        using var client = _factory.CreateUnauthenticatedClient();

        var response = await client.PostAsJsonAsync("/api/auth/challenge/verify",
            new ChallengeVerifyRequest(
                ChallengeMethod.Password,
                ScopedOperation.ChangePassword,
                JsonDocument.Parse("""{"password":"x"}""").RootElement));

        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }
}
