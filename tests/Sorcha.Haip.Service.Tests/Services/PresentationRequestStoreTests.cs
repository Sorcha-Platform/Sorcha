// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

using FluentAssertions;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Moq;
using Sorcha.AtomicCache;
using Sorcha.Haip.Service.Models;
using Sorcha.Haip.Service.Services;
using Xunit;

namespace Sorcha.Haip.Service.Tests.Services;

/// <summary>
/// Tests for PresentationRequestStore — backed by the in-memory IAtomicDistributedCache.
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
            config,
            new InMemoryAtomicDistributedCache());
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
        request.StateToken.Should().Be(request.Id.ToString());
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
    public async Task MarkCompletedAsync_DeniedResult_SetsDeniedState()
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

    [Fact]
    public async Task MarkCompletedAsync_PersistsRawVpTokenAndSubmission()
    {
        // PR B1: the raw submitted presentation must round-trip so a verifier client can
        // re-validate locally and build its own rich verdict.
        var created = await _store.CreateAsync(
            "https://test.example/haip", "TestCredential",
            null, null, "https://test.example/haip");

        const string vpToken = "eyJhbGciOiJFUzI1NiJ9.payload~disclosure~kbjwt";
        const string submission = "{\"id\":\"sub-1\",\"definition_id\":\"pd-1\"}";

        var result = new VerificationResult { IsValid = true };

        await _store.MarkCompletedAsync(created.Id, result, vpToken, submission);

        var updated = await _store.GetAsync(created.Id);
        updated.Should().NotBeNull();
        updated!.SubmittedVpToken.Should().Be(vpToken);
        updated.PresentationSubmission.Should().Be(submission);
        // Existing fields remain intact (additive change).
        updated.State.Should().Be(PresentationRequestState.Verified);
        updated.Result!.IsValid.Should().BeTrue();
    }

    [Fact]
    public async Task GetAsync_BeforeSubmission_HasNoRawVpToken()
    {
        // FR-003: the raw token is null before a holder has submitted.
        var created = await _store.CreateAsync(
            "https://test.example/haip", "TestCredential",
            null, null, "https://test.example/haip");

        var retrieved = await _store.GetAsync(created.Id);

        retrieved.Should().NotBeNull();
        retrieved!.SubmittedVpToken.Should().BeNull();
        retrieved.PresentationSubmission.Should().BeNull();
    }

    [Fact]
    public async Task MarkCompletedAsync_SecondCallAfterTerminal_IsIdempotentNoOp()
    {
        // Review M4: the CAS completion is first-writer-wins. A second callback for an
        // already-completed request must NOT overwrite the recorded outcome.
        var created = await _store.CreateAsync(
            "https://test.example/haip", "TestCredential",
            null, null, "https://test.example/haip");

        await _store.MarkCompletedAsync(created.Id, new VerificationResult
        {
            IsValid = true,
            VerifiedClaims = new() { ["name"] = "Alice" },
            HolderKeyVerified = true
        });

        // A racing/second callback arriving with a different (denied) outcome must be ignored.
        await _store.MarkCompletedAsync(created.Id, new VerificationResult
        {
            IsValid = false,
            Errors = { "late duplicate callback" }
        });

        var final = await _store.GetAsync(created.Id);
        final!.State.Should().Be(PresentationRequestState.Verified, "the first writer's terminal state wins");
        final.Result!.IsValid.Should().BeTrue();
        final.Result.VerifiedClaims.Should().ContainKey("name");
    }
}
