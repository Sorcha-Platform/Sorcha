// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

using FluentAssertions;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Moq;
using Sorcha.Haip.Service.Models;
using Sorcha.Haip.Service.Services;
using Xunit;

namespace Sorcha.Haip.Service.Tests.Services;

/// <summary>
/// Tests for PresentationRequestStore — in-memory fallback.
/// </summary>
public class PresentationRequestStoreTests
{
    private readonly PresentationRequestStore _store;

    public PresentationRequestStoreTests()
    {
        var config = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Haip:PresentationRequestTtlSeconds"] = "600"
            })
            .Build();

        _store = new PresentationRequestStore(
            Mock.Of<ILogger<PresentationRequestStore>>(),
            config);
    }

    [Fact]
    public async Task CreateAsync_ReturnsRequestWithNonce()
    {
        var request = await _store.CreateAsync(
            "https://test.example/haip",
            "LicenseCredential",
            requiredClaims: ["licenseNumber", "locality"],
            acceptedIssuers: null,
            baseUrl: "https://test.example/haip");

        request.Should().NotBeNull();
        request.Nonce.Should().NotBeNullOrWhiteSpace();
        request.CredentialType.Should().Be("LicenseCredential");
        request.RequiredClaims.Should().HaveCount(2);
        request.State.Should().Be(PresentationRequestState.Pending);
        request.ResponseUri.Should().Contain(request.Id.ToString());
    }

    [Fact]
    public async Task GetAsync_ExistingRequest_ReturnsIt()
    {
        var created = await _store.CreateAsync(
            "https://test.example/haip", "TestCredential",
            null, null, "https://test.example/haip");

        var retrieved = await _store.GetAsync(created.Id);

        retrieved.Should().NotBeNull();
        retrieved!.Id.Should().Be(created.Id);
        retrieved.Nonce.Should().Be(created.Nonce);
    }

    [Fact]
    public async Task GetAsync_NonExistent_ReturnsNull()
    {
        var result = await _store.GetAsync(Guid.NewGuid());
        result.Should().BeNull();
    }

    [Fact]
    public async Task MarkCompletedAsync_UpdatesStateAndResult()
    {
        var created = await _store.CreateAsync(
            "https://test.example/haip", "TestCredential",
            null, null, "https://test.example/haip");

        var result = new VerificationResult
        {
            IsValid = true,
            VerifiedClaims = new() { ["name"] = "Alice" },
            HolderKeyVerified = true
        };

        await _store.MarkCompletedAsync(created.Id, result);

        var updated = await _store.GetAsync(created.Id);
        updated.Should().NotBeNull();
        updated!.State.Should().Be(PresentationRequestState.Verified);
        updated.Result.Should().NotBeNull();
        updated.Result!.IsValid.Should().BeTrue();
        updated.Result.VerifiedClaims.Should().ContainKey("name");
    }

    [Fact]
    public async Task MarkCompletedAsync_FailedResult_SetsFailedState()
    {
        var created = await _store.CreateAsync(
            "https://test.example/haip", "TestCredential",
            null, null, "https://test.example/haip");

        var result = new VerificationResult
        {
            IsValid = false,
            Errors = { "KB-JWT signature invalid" }
        };

        await _store.MarkCompletedAsync(created.Id, result);

        var updated = await _store.GetAsync(created.Id);
        updated!.State.Should().Be(PresentationRequestState.Denied);
        updated.Result!.Errors.Should().Contain("KB-JWT signature invalid");
    }
}
