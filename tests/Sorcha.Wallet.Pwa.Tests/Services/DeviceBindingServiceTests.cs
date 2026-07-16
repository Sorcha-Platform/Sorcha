// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

using System;
using System.Buffers.Text;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Sorcha.CitizenWallet.Abstractions.Constants;
using Sorcha.CitizenWallet.Abstractions.Models;
using Sorcha.ServiceClients.CitizenWallet;
using Sorcha.UI.Core.Models.Presentation;
using Sorcha.UI.Core.Services.HolderKeys;
using Sorcha.Verifier.Engine.Dcql;
using Sorcha.Wallet.Pwa.Services;
using Sorcha.Wallet.Pwa.Services.Applications;
using Sorcha.Wallet.Pwa.Services.Catalogue;
using Sorcha.Wallet.Pwa.Services.Presentation;
using Xunit;
using BlueprintAction = Sorcha.Blueprint.Models.Action;

namespace Sorcha.Wallet.Pwa.Tests.Services;

/// <summary>
/// Tests for <see cref="DeviceBindingService"/> (#1195 Phase 2, Task 6 — the PWA
/// "Bind to device" flow). CanBind is the root/copy discriminator (holder-cnf AIAS
/// root, no live device copy on this device); BindToThisDeviceAsync orchestrates
/// capture → submit (gated starting action) → present (server-custody root) →
/// bounded wait for the device-cnf copy via sync. Failures must be loud and
/// distinguishable — never silent, never cached.
/// </summary>
public sealed class DeviceBindingServiceTests
{
    private const string AiasVct = "https://sorcha.dev/vc/assured-identity/v1";
    private const string BlueprintId = "aias-device-registration-20260716120000";
    private const string RegisterId = "reg-1";

    // ── fixed keys ────────────────────────────────────────────────────────────

    /// <summary>This device's P-256 public JWK (what IDeviceKeyService reports).</summary>
    private static readonly JsonElement DeviceJwk = JsonSerializer.Deserialize<JsonElement>(
        """{"kty":"EC","crv":"P-256","x":"devX","y":"devY"}""");

    /// <summary>The citizen's server-custodied holder JWK (Ed25519 — the root's cnf key).</summary>
    private static readonly JsonElement HolderJwk = JsonSerializer.Deserialize<JsonElement>(
        """{"kty":"OKP","crv":"Ed25519","x":"holderX"}""");

    private static string Rfc7638(JsonElement jwk)
    {
        var kty = jwk.GetProperty("kty").GetString();
        var crv = jwk.GetProperty("crv").GetString();
        var x = jwk.GetProperty("x").GetString();
        var canonical = kty == "OKP"
            ? $"{{\"crv\":\"{crv}\",\"kty\":\"OKP\",\"x\":\"{x}\"}}"
            : $"{{\"crv\":\"{crv}\",\"kty\":\"EC\",\"x\":\"{x}\",\"y\":\"{jwk.GetProperty("y").GetString()}\"}}";
        return Base64Url.EncodeToString(SHA256.HashData(Encoding.UTF8.GetBytes(canonical)));
    }

    private static string DeviceThumbprint => Rfc7638(DeviceJwk);

    // ── credential builders ───────────────────────────────────────────────────

    /// <summary>An SD-JWT whose payload carries cnf.jwk = the given key.</summary>
    private static string SdJwtWithCnf(JsonElement cnfJwk)
    {
        var payload = Base64Url.EncodeToString(JsonSerializer.SerializeToUtf8Bytes(new
        {
            vct = AiasVct,
            cnf = new { jwk = cnfJwk },
        }));
        return $"eyJhbGciOiJFZERTQSJ9.{payload}.sig~";
    }

    private static CachedCredential Root(Guid? id = null) => new()
    {
        Id = id ?? Guid.NewGuid(),
        Vct = AiasVct,
        RawSdJwt = SdJwtWithCnf(HolderJwk),
        AvailableClaimNames = ["givenName", "familyName", "dateOfBirth", "email"],
    };

    private static CachedCredential DeviceCopy(Guid? id = null) => new()
    {
        Id = id ?? Guid.NewGuid(),
        Vct = AiasVct,
        RawSdJwt = SdJwtWithCnf(DeviceJwk),
        AvailableClaimNames = ["givenName", "familyName", "dateOfBirth"],
    };

    // ── mocks / harness ───────────────────────────────────────────────────────

    private readonly Mock<IDeviceKeyService> _deviceKeys = new();
    private readonly Mock<ICatalogueClient> _catalogue = new();
    private readonly Mock<IApplicationActionClient> _actions = new();
    private readonly Mock<IPresentationEngine> _engine = new();
    private readonly Mock<IHolderKeyClient> _holderKeys = new();
    private readonly Mock<ICitizenWalletClient> _walletClient = new();
    private readonly Mock<ICredentialCache> _cache = new();
    private readonly Mock<ISyncService> _sync = new();
    private readonly RecordingHttpHandler _http = new();
    private readonly List<string> _callLog = [];

    private DeviceBindingService CreateService(DeviceBindingOptions? options = null) => new(
        _deviceKeys.Object,
        _catalogue.Object,
        _actions.Object,
        _engine.Object,
        _holderKeys.Object,
        _walletClient.Object,
        _cache.Object,
        _sync.Object,
        new HttpClient(_http) { BaseAddress = new Uri("https://gateway.test/") },
        TimeProvider.System,
        NullLogger<DeviceBindingService>.Instance,
        options ?? new DeviceBindingOptions
        {
            PollInterval = TimeSpan.FromMilliseconds(1),
            DeliveryTimeout = TimeSpan.FromMilliseconds(250),
        });

    public DeviceBindingServiceTests()
    {
        _deviceKeys.Setup(d => d.GetPublicJwkAsync(It.IsAny<CancellationToken>()))
            .Callback(() => _callLog.Add("capture"))
            .ReturnsAsync(DeviceJwk);
        _deviceKeys.Setup(d => d.GetThumbprintAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(DeviceThumbprint);
    }

    private void SetupHappyPath(CachedCredential root, CachedCredential copy)
    {
        var instanceId = Guid.NewGuid();

        _catalogue.Setup(c => c.GetServicesAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync([new CatalogueItem(BlueprintId, "AIAS Bind Identity to Device", null, RegisterId)]);
        _catalogue.Setup(c => c.StartAsync(It.IsAny<CatalogueItem>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(instanceId.ToString("N"));

        _actions.Setup(a => a.LoadFormAsync(instanceId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(ApplicationFormLoadResult.Success(FormContext(instanceId)));
        _actions.Setup(a => a.SubmitAsync(
                It.IsAny<ApplicationFormContext>(),
                It.IsAny<IReadOnlyDictionary<string, object?>>(),
                It.IsAny<CancellationToken>()))
            .Callback(() => _callLog.Add("submit"))
            .ReturnsAsync(SubmitAccepted());

        _engine.Setup(e => e.ParseAsync(
                "openid4vp://authorize?client_id=x&request_uri=y",
                It.IsAny<Func<string, CancellationToken, Task<string>>>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(Request());
        _engine.Setup(e => e.Match(It.IsAny<ParsedPresentationRequest>(), It.IsAny<IReadOnlyList<CachedCredential>>()))
            .Returns([new CredentialMatch
            {
                Credential = root,
                SatisfiedRequired = ["givenName", "familyName", "dateOfBirth"],
                AvailableOptional = [],
            }]);
        _engine.Setup(e => e.BuildVpTokenAsync(
                It.IsAny<CredentialMatch>(),
                It.IsAny<IReadOnlyList<string>>(),
                It.IsAny<ParsedPresentationRequest>(),
                It.IsAny<JsonElement>(),
                It.IsAny<Func<byte[], CancellationToken, Task<byte[]>>>(),
                It.IsAny<CancellationToken>()))
            .Callback(() => _callLog.Add("present"))
            .ReturnsAsync("vp-token~kb");

        _holderKeys.Setup(h => h.GetHolderKeysAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(new HolderKeysView { HolderJwk = HolderJwk, WalletAddress = "ws1qcitizen" });

        _sync.Setup(s => s.SyncAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(new SyncOutcome(SyncMode.Delta, 1, 0, 0, []));

        // First list (initialize) has only the root; after the presentation the copy appears.
        _cache.SetupSequence(c => c.ListAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync([root])
            .ReturnsAsync([root, copy]);
    }

    private static ApplicationFormContext FormContext(Guid instanceId) => new(
        InstanceId: instanceId,
        Action: new BlueprintAction { Id = 1, Title = "Bind your identity to this device" },
        BlueprintId: BlueprintId,
        RegisterId: RegisterId,
        SenderWallet: "ws1qcitizen",
        ActionId: 1,
        Title: "AIAS Bind Identity to Device");

    private static ApplicationSubmissionResult SubmitAccepted() => new(
        ApplicationSubmissionStatus.Success,
        InstanceId: null,
        ErrorCode: null,
        ErrorDetail: null,
        AwaitingPresentation: true,
        PresentationRequestId: Guid.NewGuid(),
        PresentationRequestUri: "openid4vp://authorize?client_id=x&request_uri=y");

    private static ParsedPresentationRequest Request() => new()
    {
        ClientId = "did:sorcha:org:aias",
        ResponseUri = "https://gateway.test/api/presentations/callbacks/sorcha-wallet/00000000000000000000000000000001",
        Nonce = "nonce-1",
        State = "state-1",
        Query = new DcqlQuery
        {
            Credentials = [new DcqlCredentialQuery
            {
                Id = "credential",
                Format = DcqlFormats.SdJwtVc,
                Meta = new DcqlCredentialMeta { VctValues = [AiasVct] },
            }],
        },
        RequiredVct = AiasVct,
        RequiredClaims = ["givenName", "familyName", "dateOfBirth"],
        OptionalClaims = [],
    };

    // ── CanBind matrix ────────────────────────────────────────────────────────

    [Fact]
    public async Task CanBind_AiasHolderCnfRootWithNoDeviceCopy_ReturnsTrue()
    {
        var root = Root();
        _cache.Setup(c => c.ListAsync(It.IsAny<CancellationToken>())).ReturnsAsync([root]);

        var service = CreateService();
        await service.InitializeAsync();

        service.CanBind(root).Should().BeTrue();
    }

    [Fact]
    public async Task CanBind_DeviceBoundCopyOnThisDevice_ReturnsFalse()
    {
        // A copy whose cnf IS this device's key is not a root — nothing to bind.
        var copy = DeviceCopy();
        _cache.Setup(c => c.ListAsync(It.IsAny<CancellationToken>())).ReturnsAsync([copy]);

        var service = CreateService();
        await service.InitializeAsync();

        service.CanBind(copy).Should().BeFalse();
    }

    [Fact]
    public async Task CanBind_RootButDeviceCopyAlreadyHeld_ReturnsFalse()
    {
        var root = Root();
        var copy = DeviceCopy();
        _cache.Setup(c => c.ListAsync(It.IsAny<CancellationToken>())).ReturnsAsync([root, copy]);

        var service = CreateService();
        await service.InitializeAsync();

        service.CanBind(root).Should().BeFalse("this device already holds a live device-cnf copy");
    }

    [Fact]
    public async Task CanBind_NonAiasVct_ReturnsFalse()
    {
        var other = new CachedCredential
        {
            Id = Guid.NewGuid(),
            Vct = "https://sorcha.dev/vc/driving-licence/v1",
            RawSdJwt = SdJwtWithCnf(HolderJwk),
            AvailableClaimNames = ["licenceNumber"],
        };
        _cache.Setup(c => c.ListAsync(It.IsAny<CancellationToken>())).ReturnsAsync([other]);

        var service = CreateService();
        await service.InitializeAsync();

        service.CanBind(other).Should().BeFalse();
    }

    [Fact]
    public async Task CanBind_CaseVariantVct_ReturnsFalse()
    {
        // vct matching is case-sensitive Ordinal — a case-variant URI is a different type.
        var variant = new CachedCredential
        {
            Id = Guid.NewGuid(),
            Vct = AiasVct.ToUpperInvariant(),
            RawSdJwt = SdJwtWithCnf(HolderJwk),
            AvailableClaimNames = ["givenName"],
        };
        _cache.Setup(c => c.ListAsync(It.IsAny<CancellationToken>())).ReturnsAsync([variant]);

        var service = CreateService();
        await service.InitializeAsync();

        service.CanBind(variant).Should().BeFalse();
    }

    [Fact]
    public async Task CanBind_CredentialWithoutCnf_ReturnsFalse()
    {
        var noCnf = new CachedCredential
        {
            Id = Guid.NewGuid(),
            Vct = AiasVct,
            RawSdJwt = "eyJhbGciOiJFZERTQSJ9." +
                Base64Url.EncodeToString(JsonSerializer.SerializeToUtf8Bytes(new { vct = AiasVct })) + ".sig~",
            AvailableClaimNames = ["givenName"],
        };
        _cache.Setup(c => c.ListAsync(It.IsAny<CancellationToken>())).ReturnsAsync([noCnf]);

        var service = CreateService();
        await service.InitializeAsync();

        service.CanBind(noCnf).Should().BeFalse();
    }

    [Fact]
    public void CanBind_BeforeInitialize_ReturnsFalse()
    {
        var service = CreateService();
        service.CanBind(Root()).Should().BeFalse("the device thumbprint is unknown until initialized");
    }

    [Fact]
    public async Task CanBind_DeviceKeyUnavailable_ReturnsFalse()
    {
        // Non-PWA host / bridge failure: device binding is unavailable, never a crash.
        _deviceKeys.Setup(d => d.GetThumbprintAsync(It.IsAny<CancellationToken>()))
            .ThrowsAsync(new InvalidOperationException("no webcrypto bridge"));
        _cache.Setup(c => c.ListAsync(It.IsAny<CancellationToken>())).ReturnsAsync([Root()]);

        var service = CreateService();
        await service.InitializeAsync();

        service.CanBind(Root()).Should().BeFalse();
    }

    // ── BindToThisDeviceAsync ─────────────────────────────────────────────────

    [Fact]
    public async Task BindToThisDeviceAsync_HappyPath_RunsLegsInOrder_AndReturnsTheCachedDeviceCopy()
    {
        var root = Root();
        var copy = DeviceCopy();
        SetupHappyPath(root, copy);

        var service = CreateService();
        var result = await service.BindToThisDeviceAsync(root, CancellationToken.None);

        result.Should().BeSameAs(copy, "the device-cnf copy delivered via sync is the result");

        // The F127 gate defines the order: the device key is captured first, the gated
        // starting action is SUBMITTED (that mints the presentation request), and only
        // then is the root PRESENTED. (Design §4.2 lists present-then-submit, but the
        // shipped F127 machinery mints the request AT submission — submit precedes present.)
        _callLog.Should().ContainInOrder("capture", "submit", "present");

        // The direct_post left the building: exactly one POST to the request's response_uri
        // carrying the vp token in the documented {vpToken} JSON shape.
        _http.Requests.Should().ContainSingle();
        _http.Requests[0].Method.Should().Be(HttpMethod.Post);
        _http.Requests[0].RequestUri!.ToString().Should().Contain("/api/presentations/callbacks/sorcha-wallet/");
        _http.RequestBodies[0].Should().Contain("\"vpToken\"").And.Contain("vp-token~kb");
    }

    [Fact]
    public async Task BindToThisDeviceAsync_SubmitsDeviceJwkAtTheBlueprintSlot()
    {
        var root = Root();
        SetupHappyPath(root, DeviceCopy());
        IReadOnlyDictionary<string, object?>? submitted = null;
        _actions.Setup(a => a.SubmitAsync(
                It.IsAny<ApplicationFormContext>(),
                It.IsAny<IReadOnlyDictionary<string, object?>>(),
                It.IsAny<CancellationToken>()))
            .Callback((ApplicationFormContext _, IReadOnlyDictionary<string, object?> data, CancellationToken _) =>
                submitted = data)
            .ReturnsAsync(SubmitAccepted());

        var service = CreateService();
        await service.BindToThisDeviceAsync(root, CancellationToken.None);

        submitted.Should().NotBeNull();
        submitted!.Keys.Should().Contain("/deviceKey/holderJwk",
            "the blueprint's issuance action reads the device JWK from /deviceKey/holderJwk");
    }

    [Fact]
    public async Task BindToThisDeviceAsync_HolderSignerDelegate_RoundTripsTheSignKbEndpoint()
    {
        var root = Root();
        SetupHappyPath(root, DeviceCopy());

        Func<byte[], CancellationToken, Task<byte[]>>? capturedSigner = null;
        _engine.Setup(e => e.BuildVpTokenAsync(
                It.IsAny<CredentialMatch>(),
                It.IsAny<IReadOnlyList<string>>(),
                It.IsAny<ParsedPresentationRequest>(),
                It.IsAny<JsonElement>(),
                It.IsAny<Func<byte[], CancellationToken, Task<byte[]>>>(),
                It.IsAny<CancellationToken>()))
            .Callback((CredentialMatch _, IReadOnlyList<string> _, ParsedPresentationRequest _,
                       JsonElement jwk, Func<byte[], CancellationToken, Task<byte[]>> signer, CancellationToken _) =>
            {
                jwk.GetProperty("kty").GetString().Should().Be("OKP", "the ROOT presents under the holder JWK, not the device key");
                capturedSigner = signer;
            })
            .ReturnsAsync("vp-token~kb");

        var signatureBytes = new byte[] { 7, 7, 7 };
        _walletClient.Setup(w => w.SignKbJwtAsync(
                It.Is<KbJwtSignRequest>(r => r.SigningInput == "hdr.payload"),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new KbJwtSignResponse
            {
                Signature = Base64Url.EncodeToString(signatureBytes),
                Algorithm = "EdDSA",
            });

        var service = CreateService();
        await service.BindToThisDeviceAsync(root, CancellationToken.None);

        capturedSigner.Should().NotBeNull("the engine must receive the server-custody signer");
        var produced = await capturedSigner!(Encoding.ASCII.GetBytes("hdr.payload"), CancellationToken.None);
        produced.Should().Equal(signatureBytes, "the delegate signs via POST /api/v1/wallet/presentations/sign-kb");
    }

    [Fact]
    public async Task BindToThisDeviceAsync_PresentationLegFails_ThrowsDistinguishableError_AndCachesNothing()
    {
        var root = Root();
        SetupHappyPath(root, DeviceCopy());
        _engine.Setup(e => e.ParseAsync(
                It.IsAny<string>(),
                It.IsAny<Func<string, CancellationToken, Task<string>>>(),
                It.IsAny<CancellationToken>()))
            .ThrowsAsync(new FormatException("bad request object"));

        var service = CreateService();
        var act = () => service.BindToThisDeviceAsync(root, CancellationToken.None);

        var ex = (await act.Should().ThrowAsync<DeviceBindingException>()).Which;
        ex.Failure.Should().Be(DeviceBindingFailure.PresentationRequestInvalid);
        ex.Message.Should().Contain("identity check", "the presentation-leg failure must be nameable by the citizen");

        _sync.Verify(s => s.SyncAsync(It.IsAny<CancellationToken>()), Times.Never,
            "a failed presentation must not trigger any delivery wait");
        _cache.Verify(c => c.UpsertAsync(It.IsAny<CachedCredential>(), It.IsAny<CancellationToken>()), Times.Never,
            "nothing may be cached on a failed bind");
    }

    [Fact]
    public async Task BindToThisDeviceAsync_DirectPostRejected_ThrowsWithStatus_AndCachesNothing()
    {
        var root = Root();
        SetupHappyPath(root, DeviceCopy());
        _http.RespondWith = _ => new HttpResponseMessage(HttpStatusCode.Forbidden);

        var service = CreateService();
        var act = () => service.BindToThisDeviceAsync(root, CancellationToken.None);

        var ex = (await act.Should().ThrowAsync<DeviceBindingException>()).Which;
        ex.Failure.Should().Be(DeviceBindingFailure.PresentationSubmitRejected);
        ex.Message.Should().Contain("403", "the submit-leg failure must carry the refusing status");
        ex.Retryable.Should().BeFalse();
        _sync.Verify(s => s.SyncAsync(It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task BindToThisDeviceAsync_DirectPost503_IsRetryable()
    {
        var root = Root();
        SetupHappyPath(root, DeviceCopy());
        _http.RespondWith = _ => new HttpResponseMessage(HttpStatusCode.ServiceUnavailable);

        var service = CreateService();
        var act = () => service.BindToThisDeviceAsync(root, CancellationToken.None);

        var ex = (await act.Should().ThrowAsync<DeviceBindingException>()).Which;
        ex.Failure.Should().Be(DeviceBindingFailure.PresentationSubmitRejected);
        ex.Retryable.Should().BeTrue("503 is a transient infrastructure fault");
    }

    [Fact]
    public async Task BindToThisDeviceAsync_Submit409_IsPolicyRefusal_NotRetryable()
    {
        var root = Root();
        SetupHappyPath(root, DeviceCopy());
        _actions.Setup(a => a.SubmitAsync(
                It.IsAny<ApplicationFormContext>(),
                It.IsAny<IReadOnlyDictionary<string, object?>>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ApplicationSubmissionResult(
                ApplicationSubmissionStatus.ValidationFailed, null, "HTTP_409", "conflict"));

        var service = CreateService();
        var act = () => service.BindToThisDeviceAsync(root, CancellationToken.None);

        var ex = (await act.Should().ThrowAsync<DeviceBindingException>()).Which;
        ex.Failure.Should().Be(DeviceBindingFailure.PolicyRefused);
        ex.Retryable.Should().BeFalse("the policy ran and said no — retrying cannot change the answer");
    }

    [Fact]
    public async Task BindToThisDeviceAsync_SubmitAcceptedWithoutPresentation_ThrowsNamedServerGap()
    {
        // Never-silent: if the server accepts the action but doesn't start the identity
        // check (SorchaWallet initiation not wired on /execute), the citizen must see a
        // named failure — not a success, not an eternal spinner.
        var root = Root();
        SetupHappyPath(root, DeviceCopy());
        _actions.Setup(a => a.SubmitAsync(
                It.IsAny<ApplicationFormContext>(),
                It.IsAny<IReadOnlyDictionary<string, object?>>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ApplicationSubmissionResult(
                ApplicationSubmissionStatus.Success, null, null, null));

        var service = CreateService();
        var act = () => service.BindToThisDeviceAsync(root, CancellationToken.None);

        var ex = (await act.Should().ThrowAsync<DeviceBindingException>()).Which;
        ex.Failure.Should().Be(DeviceBindingFailure.PresentationNotInitiated);
        _engine.Verify(e => e.ParseAsync(It.IsAny<string>(),
            It.IsAny<Func<string, CancellationToken, Task<string>>>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task BindToThisDeviceAsync_CopyNeverArrives_ThrowsDeliveryTimeout_AsExplicitPendingOutcome()
    {
        var root = Root();
        SetupHappyPath(root, DeviceCopy());
        // Sync runs, but the copy never lands in the cache.
        _cache.Setup(c => c.ListAsync(It.IsAny<CancellationToken>())).ReturnsAsync([root]);

        var service = CreateService();
        var act = () => service.BindToThisDeviceAsync(root, CancellationToken.None);

        var ex = (await act.Should().ThrowAsync<DeviceBindingException>()).Which;
        ex.Failure.Should().Be(DeviceBindingFailure.DeliveryTimeout);
        ex.Message.Should().Contain("still", "a timeout is a PENDING outcome, and must read as one — not as a generic error");
        _sync.Verify(s => s.SyncAsync(It.IsAny<CancellationToken>()), Times.AtLeastOnce);
    }

    [Fact]
    public async Task BindToThisDeviceAsync_BlueprintNotPublished_ThrowsBlueprintNotFound()
    {
        var root = Root();
        _catalogue.Setup(c => c.GetServicesAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync([new CatalogueItem("some-other-service", "Fishing Licence", null, RegisterId)]);

        var service = CreateService();
        var act = () => service.BindToThisDeviceAsync(root, CancellationToken.None);

        var ex = (await act.Should().ThrowAsync<DeviceBindingException>()).Which;
        ex.Failure.Should().Be(DeviceBindingFailure.BlueprintNotFound);
    }

    // ── HTTP stub ─────────────────────────────────────────────────────────────

    /// <summary>Records outbound requests and answers with a configurable response (200 OK default).</summary>
    private sealed class RecordingHttpHandler : HttpMessageHandler
    {
        public List<HttpRequestMessage> Requests { get; } = [];
        public List<string> RequestBodies { get; } = [];
        public Func<HttpRequestMessage, HttpResponseMessage> RespondWith { get; set; } =
            _ => new HttpResponseMessage(HttpStatusCode.OK);

        protected override async Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Requests.Add(request);
            RequestBodies.Add(request.Content is null
                ? string.Empty
                : await request.Content.ReadAsStringAsync(cancellationToken));
            return RespondWith(request);
        }
    }
}
