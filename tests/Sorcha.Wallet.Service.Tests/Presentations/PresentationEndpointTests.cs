// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

using System.Text.Json;
using Microsoft.AspNetCore.Http;
using Sorcha.Wallet.Service.Credentials;
using Sorcha.Wallet.Service.Endpoints;
using Sorcha.Wallet.Service.Models;
using Sorcha.Wallet.Service.Services;

namespace Sorcha.Wallet.Service.Tests.Presentations;

public class PresentationEndpointTests
{
    private readonly Mock<IPresentationRequestService> _serviceMock = new();
    // Phase 2b — SubmitPresentation now takes ICredentialStore + IWalletInboxWriter
    // so the reflection-based InvokeSubmit must pass these as well. Both default
    // mocks let calls fall through without affecting the existing assertions
    // (the inbox writer is fail-safe; the credential lookup returns null which
    // short-circuits the inbox-write path).
    private readonly Mock<Sorcha.Wallet.Service.Credentials.ICredentialStore> _credentialStoreMock = new();
    private readonly Mock<Sorcha.Wallet.Service.Services.Implementation.IWalletInboxWriter> _inboxWriterMock = new();

    [Fact]
    public async Task CreateRequest_ValidBody_Returns201WithRequestId()
    {
        var expectedRequest = CreateTestRequest();
        _serviceMock
            .Setup(s => s.CreateRequestAsync(It.IsAny<CreatePresentationRequestDto>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(expectedRequest);

        var body = new CreatePresentationRequestBody
        {
            CredentialType = "ChemicalHandlingLicense",
            CallbackUrl = "https://verifier.example/callback"
        };

        var result = await InvokeCreateRequest(body);

        result.GetType().Name.Should().Contain("Created");
    }

    [Fact]
    public async Task CreateRequest_MissingCredentialType_ReturnsBadRequest()
    {
        var body = new CreatePresentationRequestBody
        {
            CredentialType = "",
            CallbackUrl = "https://verifier.example/callback"
        };

        var result = await InvokeCreateRequest(body);

        result.GetType().Name.Should().Contain("BadRequest");
    }

    [Fact]
    public async Task CreateRequest_InvalidCallbackUrl_ReturnsBadRequest()
    {
        var body = new CreatePresentationRequestBody
        {
            CredentialType = "ChemicalHandlingLicense",
            CallbackUrl = "http://not-https.example/callback"
        };

        var result = await InvokeCreateRequest(body);

        result.GetType().Name.Should().Contain("BadRequest");
    }

    [Fact]
    public async Task GetRequest_ExistingRequest_Returns200()
    {
        var request = CreateTestRequest();
        _serviceMock
            .Setup(s => s.GetRequestAsync("req-1", It.IsAny<CancellationToken>()))
            .ReturnsAsync(request);
        _serviceMock
            .Setup(s => s.FindMatchingCredentialsAsync(request, "wallet-1", It.IsAny<CancellationToken>()))
            .ReturnsAsync(Array.Empty<MatchedCredentialInfo>());

        var result = await InvokeGetRequest("req-1", "wallet-1");

        result.GetType().Name.Should().Contain("Ok");
    }

    [Fact]
    public async Task GetRequest_NotFound_Returns404()
    {
        _serviceMock
            .Setup(s => s.GetRequestAsync("nonexistent", It.IsAny<CancellationToken>()))
            .ReturnsAsync((PresentationRequest?)null);

        var result = await InvokeGetRequest("nonexistent", null);

        result.GetType().Name.Should().Contain("NotFound");
    }

    [Fact]
    public async Task GetRequest_Expired_Returns410()
    {
        var request = CreateTestRequest();
        request.Status = PresentationStatus.Expired;

        _serviceMock
            .Setup(s => s.GetRequestAsync("expired", It.IsAny<CancellationToken>()))
            .ReturnsAsync(request);

        var result = await InvokeGetRequest("expired", null);

        result.GetType().Name.Should().Contain("JsonHttpResult");
    }

    [Fact]
    public async Task SubmitPresentation_ValidSubmission_Returns200()
    {
        var request = CreateTestRequest();
        request.Status = PresentationStatus.Verified;
        request.VerificationResult = JsonSerializer.Serialize(new VerificationResult
        {
            IsValid = true,
            VerifiedClaims = new Dictionary<string, object> { ["class"] = "CategoryB" },
            CredentialType = "ChemicalHandlingLicense",
            IssuerDid = "did:sorcha:w:issuer-1",
            StatusListCheck = "Active"
        });

        _serviceMock
            .Setup(s => s.SubmitPresentationAsync("req-1", "cred-1", It.IsAny<string[]>(), "vp-token", It.IsAny<CancellationToken>()))
            .ReturnsAsync(request);

        var body = new SubmitPresentationBody
        {
            CredentialId = "cred-1",
            VpToken = "vp-token"
        };

        var result = await InvokeSubmit("req-1", body);

        result.GetType().Name.Should().Contain("Ok");
    }

    [Fact]
    public async Task SubmitPresentation_MissingVpToken_ReturnsBadRequest()
    {
        var body = new SubmitPresentationBody
        {
            CredentialId = "cred-1",
            VpToken = ""
        };

        var result = await InvokeSubmit("req-1", body);

        result.GetType().Name.Should().Contain("BadRequest");
    }

    [Fact]
    public async Task SubmitPresentation_NotFound_Returns404()
    {
        _serviceMock
            .Setup(s => s.SubmitPresentationAsync("missing", It.IsAny<string>(), It.IsAny<string[]>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new KeyNotFoundException("Presentation request 'missing' not found"));

        var body = new SubmitPresentationBody
        {
            CredentialId = "cred-1",
            VpToken = "token"
        };

        var result = await InvokeSubmit("missing", body);

        result.GetType().Name.Should().Contain("NotFound");
    }

    [Fact]
    public async Task SubmitPresentation_Expired_Returns410()
    {
        _serviceMock
            .Setup(s => s.SubmitPresentationAsync("expired", It.IsAny<string>(), It.IsAny<string[]>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new InvalidOperationException("Presentation request has expired"));

        var body = new SubmitPresentationBody
        {
            CredentialId = "cred-1",
            VpToken = "token"
        };

        var result = await InvokeSubmit("expired", body);

        result.GetType().Name.Should().Contain("JsonHttpResult");
    }

    [Fact]
    public async Task DenyRequest_Existing_ReturnsDeniedStatus()
    {
        var request = CreateTestRequest();
        request.Status = PresentationStatus.Denied;

        _serviceMock
            .Setup(s => s.DenyRequestAsync("req-1", It.IsAny<CancellationToken>()))
            .ReturnsAsync(request);

        var result = await InvokeDeny("req-1");

        result.GetType().Name.Should().Contain("Ok");
    }

    [Fact]
    public async Task DenyRequest_NotFound_Returns404()
    {
        _serviceMock
            .Setup(s => s.DenyRequestAsync("missing", It.IsAny<CancellationToken>()))
            .ReturnsAsync((PresentationRequest?)null);

        var result = await InvokeDeny("missing");

        result.GetType().Name.Should().Contain("NotFound");
    }

    [Fact]
    public async Task GetResult_Pending_Returns202()
    {
        var request = CreateTestRequest();
        request.Status = PresentationStatus.Pending;

        _serviceMock
            .Setup(s => s.GetRequestAsync("req-1", It.IsAny<CancellationToken>()))
            .ReturnsAsync(request);

        var result = await InvokeGetResult("req-1");

        result.GetType().Name.Should().Contain("JsonHttpResult");
    }

    [Fact]
    public async Task GetResult_Verified_Returns200()
    {
        var request = CreateTestRequest();
        request.Status = PresentationStatus.Verified;
        request.VerificationResult = JsonSerializer.Serialize(new VerificationResult
        {
            IsValid = true,
            VerifiedClaims = new Dictionary<string, object> { ["class"] = "CategoryB" }
        });

        _serviceMock
            .Setup(s => s.GetRequestAsync("req-1", It.IsAny<CancellationToken>()))
            .ReturnsAsync(request);

        var result = await InvokeGetResult("req-1");

        result.GetType().Name.Should().Contain("Ok");
    }

    [Fact]
    public async Task GetResult_Expired_Returns410()
    {
        var request = CreateTestRequest();
        request.Status = PresentationStatus.Expired;

        _serviceMock
            .Setup(s => s.GetRequestAsync("expired", It.IsAny<CancellationToken>()))
            .ReturnsAsync(request);

        var result = await InvokeGetResult("expired");

        result.GetType().Name.Should().Contain("JsonHttpResult");
    }

    [Fact]
    public async Task GetResult_NotFound_Returns404()
    {
        _serviceMock
            .Setup(s => s.GetRequestAsync("missing", It.IsAny<CancellationToken>()))
            .ReturnsAsync((PresentationRequest?)null);

        var result = await InvokeGetResult("missing");

        result.GetType().Name.Should().Contain("NotFound");
    }

    // --- Helper methods for reflection-based endpoint invocation ---

    private static PresentationRequest CreateTestRequest() => new()
    {
        Id = "req-1",
        VerifierIdentity = "Test Verifier",
        CredentialType = "ChemicalHandlingLicense",
        CallbackUrl = "https://verifier.example/callback",
        ExpiresAt = DateTimeOffset.UtcNow.AddMinutes(5)
    };

    private async Task<IResult> InvokeCreateRequest(CreatePresentationRequestBody body)
    {
        var method = typeof(PresentationEndpoints).GetMethod(
            "CreateRequest",
            System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Static);
        method.Should().NotBeNull("CreateRequest should exist");

        var httpContext = CreateMockHttpContext();
        var result = method!.Invoke(null, [body, _serviceMock.Object, httpContext, CancellationToken.None]);
        return await (Task<IResult>)result!;
    }

    private async Task<IResult> InvokeGetRequest(string requestId, string? walletAddress)
    {
        var method = typeof(PresentationEndpoints).GetMethod(
            "GetRequest",
            System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Static);
        method.Should().NotBeNull("GetRequest should exist");

        var result = method!.Invoke(null, [requestId, walletAddress, _serviceMock.Object, CancellationToken.None]);
        return await (Task<IResult>)result!;
    }

    private async Task<IResult> InvokeSubmit(string requestId, SubmitPresentationBody body)
    {
        var method = typeof(PresentationEndpoints).GetMethod(
            "SubmitPresentation",
            System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Static);
        method.Should().NotBeNull("SubmitPresentation should exist");

        var result = method!.Invoke(null, [
            requestId,
            body,
            _serviceMock.Object,
            _credentialStoreMock.Object,
            _inboxWriterMock.Object,
            CancellationToken.None]);
        return await (Task<IResult>)result!;
    }

    private async Task<IResult> InvokeDeny(string requestId)
    {
        var method = typeof(PresentationEndpoints).GetMethod(
            "DenyRequest",
            System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Static);
        method.Should().NotBeNull("DenyRequest should exist");

        var result = method!.Invoke(null, [requestId, _serviceMock.Object, CancellationToken.None]);
        return await (Task<IResult>)result!;
    }

    private async Task<IResult> InvokeGetResult(string requestId)
    {
        var method = typeof(PresentationEndpoints).GetMethod(
            "GetResult",
            System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Static);
        method.Should().NotBeNull("GetResult should exist");

        var result = method!.Invoke(null, [requestId, _serviceMock.Object, CancellationToken.None]);
        return await (Task<IResult>)result!;
    }

    private static HttpContext CreateMockHttpContext()
    {
        var context = new DefaultHttpContext();
        context.Request.Scheme = "https";
        context.Request.Host = new HostString("sorcha.example");
        return context;
    }
}
