// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

using Microsoft.Extensions.Logging.Abstractions;
using ModelContextProtocol.Protocol;
using Sorcha.McpServer.Infrastructure;
using Sorcha.McpServer.Services;
using Sorcha.McpServer.Tests.Services;
using Sorcha.McpServer.Tools.Designer;
using Sorcha.Register.Models;
using Sorcha.Register.Models.Enums;
using Sorcha.ServiceClients.Register;
using Sorcha.ServiceClients.Tenant;
using Sorcha.ServiceClients.Wallet;
using Sorcha.Wallet.Contracts.Constants;

using SdkMcpServer = ModelContextProtocol.Server.McpServer;

namespace Sorcha.McpServer.Tests.Tools;

/// <summary>
/// <c>sorcha_register_create</c>. The four load-bearing cases pin a value or a refusal whose
/// failure mode is SILENT: a wrong derivation path derives a different valid key (leaving the
/// register ungovernable with nothing reporting it), an agent-supplied wallet records the wrong
/// governance key, and a client that cannot elicit must never reach a backend at all.
/// </summary>
public class RegisterCreateToolTests
{
    private const string OrgId = "00000000-0000-0000-0000-000000000042";

    [Fact]
    public async Task CreateRegisterAsync_ClientCannotElicit_RefusesBeforeAnyBackendCall()
    {
        var h = new Harness().WithApprovalOutcome(ApprovalOutcome.NotSupported, "no elicitation");

        var result = await h.Sut().CreateRegisterAsync(h.Server, "Acme Supply", "A register");

        result.Status.Should().Be("ApprovalRequired");
        h.Register.Verify(r => r.InitiateRegisterCreationAsync(
            It.IsAny<InitiateRegisterCreationRequest>(), It.IsAny<CancellationToken>()), Times.Never);
        h.Wallet.Verify(w => w.SignTransactionAsync(
            It.IsAny<string>(), It.IsAny<byte[]>(), It.IsAny<string?>(), It.IsAny<bool>(),
            It.IsAny<CancellationToken>()), Times.Never);
    }

    [Theory]
    [InlineData(ApprovalOutcome.Refused)]
    [InlineData(ApprovalOutcome.NotSupported)]
    public async Task CreateRegisterAsync_NotApproved_NeverInitiates(ApprovalOutcome outcome)
    {
        var h = new Harness().WithApprovalOutcome(outcome, "nope");

        await h.Sut().CreateRegisterAsync(h.Server, "Acme Supply", "A register");

        h.Register.Verify(r => r.InitiateRegisterCreationAsync(
            It.IsAny<InitiateRegisterCreationRequest>(), It.IsAny<CancellationToken>()), Times.Never);
        h.Register.Verify(r => r.FinalizeRegisterCreationAsync(
            It.IsAny<FinalizeRegisterCreationRequest>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task CreateRegisterAsync_Refused_ReportsRefusedNotApprovalRequired()
    {
        // The two refusal paths need different operator responses: "the person said no" versus
        // "your client cannot ask anybody". Collapsing them would tell an agent to retry a
        // deliberate decline, or to go find a person for a client that can never reach one.
        var h = new Harness().WithApprovalOutcome(ApprovalOutcome.Refused, "the user declined");

        var result = await h.Sut().CreateRegisterAsync(h.Server, "Acme Supply", "A register");

        result.Status.Should().Be("Refused");
        result.Message.Should().Contain("declined");
    }

    [Fact]
    public async Task CreateRegisterAsync_Approved_SignsWithTheRegisterAttestationContext()
    {
        // A wrong derivation path does not throw — it derives a DIFFERENT, perfectly valid key,
        // and the register is silently ungovernable from creation. Nothing else catches this.
        var h = new Harness().WithApproval().WithInitiate(walletId: "ws11qorg", dataToSignHex: "aabb");

        await h.Sut().CreateRegisterAsync(h.Server, "Acme Supply", "A register");

        h.Wallet.Verify(w => w.SignTransactionAsync(
            "ws11qorg",
            It.IsAny<byte[]>(),
            SorchaDerivationPaths.RegisterAttestation,
            true,
            It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task CreateRegisterAsync_Approved_SignsWithTheCallersOrgWalletNotAnAgentSuppliedOne()
    {
        var h = new Harness().WithApproval().WithInitiate(walletId: "ws11qorg", dataToSignHex: "aabb");

        await h.Sut().CreateRegisterAsync(h.Server, "Acme Supply", "A register");

        h.Tenant.Verify(t => t.GetOrganizationAsync(OrgId, It.IsAny<CancellationToken>()), Times.Once);
        h.CapturedInitiateRequest.Should().NotBeNull();
        h.CapturedInitiateRequest!.Owners.Should().ContainSingle()
            .Which.WalletId.Should().Be("ws11qorg");
    }

    [Fact]
    public async Task CreateRegisterAsync_Approved_TagsProvenanceInMetadata()
    {
        var h = new Harness().WithApproval().WithInitiate(walletId: "ws11qorg", dataToSignHex: "aabb");

        await h.Sut().CreateRegisterAsync(h.Server, "Acme Supply", "A register");

        h.CapturedInitiateRequest!.Metadata.Should().NotBeNull();
        h.CapturedInitiateRequest.Metadata!.Should().ContainKey("createdVia")
            .WhoseValue.Should().Be("mcp");
        h.CapturedInitiateRequest.Metadata.Should().ContainKey("mcpToolVersion");
    }

    [Fact]
    public async Task CreateRegisterAsync_Approved_FinalizesWithTheInitiationNonceAndReturnsGenesis()
    {
        var h = new Harness().WithApproval().WithInitiate(walletId: "ws11qorg", dataToSignHex: "aabb");

        var result = await h.Sut().CreateRegisterAsync(h.Server, "Acme Supply", "A register");

        result.Status.Should().Be("Success");
        result.RegisterId.Should().Be(Harness.RegisterId);
        result.GenesisTransactionId.Should().Be("genesis-tx-1");
        h.CapturedFinalizeRequest.Should().NotBeNull();
        h.CapturedFinalizeRequest!.Nonce.Should().Be("nonce-1");
        h.CapturedFinalizeRequest.RegisterId.Should().Be(Harness.RegisterId);
        h.CapturedFinalizeRequest.SignedAttestations.Should().ContainSingle();
    }

    [Fact]
    public async Task CreateRegisterAsync_Approved_SubmissionIsAsynchronous_MessageDoesNotClaimTheChainIsSettled()
    {
        // Feature 145: the genesis transaction is submitted, not confirmed. A message that reads
        // as "done" invites the agent to immediately query state that does not exist yet.
        var h = new Harness().WithApproval().WithInitiate(walletId: "ws11qorg", dataToSignHex: "aabb");

        var result = await h.Sut().CreateRegisterAsync(h.Server, "Acme Supply", "A register");

        result.Message.Should().NotContainEquivalentOf("confirmed");
    }

    [Fact]
    public async Task CreateRegisterAsync_OrgHasNoWallet_TellsTheAgentAnAdminMustCreateOne()
    {
        // #1525: an org without a wallet is a legitimate state, not a fault to repair. The
        // recovery phrase is shown once and belongs to the org's own admin, so no service —
        // and certainly no agent — may mint it on their behalf.
        var h = new Harness().WithApproval();
        h.Tenant.Setup(t => t.GetOrganizationAsync(OrgId, It.IsAny<CancellationToken>()))
            .ReturnsAsync($$"""{"id":"{{OrgId}}","name":"Acme","walletAddress":null}""");

        var result = await h.Sut().CreateRegisterAsync(h.Server, "Acme Supply", "A register");

        result.Status.Should().Be("Error");
        result.Message.Should().ContainEquivalentOf("administrator");
        h.Approval.Verify(a => a.RequestAsync(
            It.IsAny<SdkMcpServer>(), It.IsAny<HumanApprovalRequest>(), It.IsAny<CancellationToken>()),
            Times.Never);
        h.Register.Verify(r => r.InitiateRegisterCreationAsync(
            It.IsAny<InitiateRegisterCreationRequest>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task CreateRegisterAsync_NoOrgInToken_RefusesWithoutAskingAnybody()
    {
        var h = new Harness().WithApproval();
        h.Caller.SetupGet(c => c.OrganizationId).Returns((string?)null);

        var result = await h.Sut().CreateRegisterAsync(h.Server, "Acme Supply", "A register");

        result.Status.Should().Be("Error");
        result.Message.Should().ContainEquivalentOf("organisation");
        h.Approval.Verify(a => a.RequestAsync(
            It.IsAny<SdkMcpServer>(), It.IsAny<HumanApprovalRequest>(), It.IsAny<CancellationToken>()),
            Times.Never);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public async Task CreateRegisterAsync_MissingName_ReturnsValidationErrorWithoutAskingAnybody(string name)
    {
        var h = new Harness().WithApproval();

        var result = await h.Sut().CreateRegisterAsync(h.Server, name, "A register");

        result.Status.Should().Be("ValidationError");
        h.Approval.Verify(a => a.RequestAsync(
            It.IsAny<SdkMcpServer>(), It.IsAny<HumanApprovalRequest>(), It.IsAny<CancellationToken>()),
            Times.Never);
    }

    [Fact]
    public async Task CreateRegisterAsync_NameTooLong_NamesTheRealLimit()
    {
        // The limit is InitiateRegisterCreationRequest's own [StringLength(38, MinimumLength = 1)].
        // Stating it beats making the agent decode a 400.
        var h = new Harness().WithApproval();

        var result = await h.Sut().CreateRegisterAsync(h.Server, new string('x', 39), "A register");

        result.Status.Should().Be("ValidationError");
        result.ValidationErrors.Should().ContainMatch("*38*");
    }

    [Fact]
    public async Task CreateRegisterAsync_DescriptionTooLong_NamesTheRealLimit()
    {
        var h = new Harness().WithApproval();

        var result = await h.Sut().CreateRegisterAsync(h.Server, "Acme Supply", new string('x', 501));

        result.Status.Should().Be("ValidationError");
        result.ValidationErrors.Should().ContainMatch("*500*");
    }

    [Fact]
    public async Task CreateRegisterAsync_NotEntitled_RefusesWithoutAskingAnybody()
    {
        var h = new Harness().WithApproval();
        h.Auth.Setup(a => a.CanInvokeTool("sorcha_register_create")).Returns(false);

        var result = await h.Sut().CreateRegisterAsync(h.Server, "Acme Supply", "A register");

        result.Status.Should().Be("Unauthorized");
        h.Approval.Verify(a => a.RequestAsync(
            It.IsAny<SdkMcpServer>(), It.IsAny<HumanApprovalRequest>(), It.IsAny<CancellationToken>()),
            Times.Never);
    }

    [Fact]
    public async Task CreateRegisterAsync_RegisterServiceUnavailable_ReturnsUnavailableWithoutAskingAnybody()
    {
        var h = new Harness().WithApproval();
        h.Availability.Setup(a => a.IsServiceAvailable("Register")).Returns(false);

        var result = await h.Sut().CreateRegisterAsync(h.Server, "Acme Supply", "A register");

        result.Status.Should().Be("Unavailable");
        h.Approval.Verify(a => a.RequestAsync(
            It.IsAny<SdkMcpServer>(), It.IsAny<HumanApprovalRequest>(), It.IsAny<CancellationToken>()),
            Times.Never);
    }

    [Fact]
    public async Task CreateRegisterAsync_ApprovalPromptNamesTheOwningWalletAndTheStorageMode()
    {
        // The person is the entire gate. A prompt that omits what is actually being established
        // makes the confirmation ceremonial.
        var h = new Harness().WithApproval().WithInitiate(walletId: "ws11qorg", dataToSignHex: "aabb");

        await h.Sut().CreateRegisterAsync(h.Server, "Acme Supply", "A register", devMode: true);

        h.CapturedApprovalRequest.Should().NotBeNull();
        h.CapturedApprovalRequest!.Message.Should().Contain("Acme Supply");
        h.CapturedApprovalRequest.Message.Should().Contain("ws11qorg");
        h.CapturedApprovalRequest.Message.Should().ContainEquivalentOf("plaintext");
    }

    [Fact]
    public async Task CreateRegisterAsync_FinalizeFails_ReportsErrorRatherThanAFalseSuccess()
    {
        var h = new Harness().WithApproval().WithInitiate(walletId: "ws11qorg", dataToSignHex: "aabb");
        h.Register.Setup(r => r.FinalizeRegisterCreationAsync(
                It.IsAny<FinalizeRegisterCreationRequest>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new HttpRequestException("finalize: RequestTimeout"));

        var result = await h.Sut().CreateRegisterAsync(h.Server, "Acme Supply", "A register");

        result.Status.Should().Be("Error");
        result.RegisterId.Should().BeNull();
    }

    /// <summary>
    /// Pattern 23: a self-supplied label may be recorded as an audit fact, but the moment
    /// anything branches on it, it becomes an authority claim the agent controls.
    /// </summary>
    [Fact]
    public void CreatedViaProvenance_IsWrittenButNeverRead()
    {
        var sourceRoot = FindRepositoryRoot();
        var srcDir = Path.Combine(sourceRoot, "src");
        Directory.Exists(srcDir).Should().BeTrue(
            "the scan must actually reach the source tree, or this test is vacuous");

        var scanned = 0;
        var readSites = new List<string>();

        foreach (var file in Directory.EnumerateFiles(srcDir, "*.cs", SearchOption.AllDirectories))
        {
            if (file.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}", StringComparison.Ordinal)
                || file.Contains($"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}", StringComparison.Ordinal))
            {
                continue;
            }

            scanned++;

            // The ONE write site is exempt. Everything else naming the key is a read.
            if (file.EndsWith("RegisterCreateTool.cs", StringComparison.Ordinal))
            {
                continue;
            }

            if (File.ReadAllText(file).Contains("createdVia", StringComparison.Ordinal))
            {
                readSites.Add(file);
            }
        }

        scanned.Should().BeGreaterThan(500,
            "the scan must cover the whole src tree; a near-empty enumeration would pass vacuously");

        readSites.Should().BeEmpty(
            "createdVia is an audit fact; branching on it would make it an authority claim the " +
            "agent supplies about itself (CLAUDE.md pattern 23)");
    }

    /// <summary>Walks up from the test binary to the directory holding <c>Sorcha.sln</c>.</summary>
    private static string FindRepositoryRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "Sorcha.sln")))
        {
            dir = dir.Parent;
        }

        dir.Should().NotBeNull("the test must be able to locate the repository root (Sorcha.sln)");
        return dir!.FullName;
    }

    private sealed class Harness
    {
        public const string RegisterId = "0123456789abcdef0123456789abcdef";

        public Mock<IMcpAuthorizationService> Auth { get; } = new();
        public Mock<IServiceAvailabilityTracker> Availability { get; } = new();
        public Mock<ICallerContext> Caller { get; } = new();
        public Mock<ITenantServiceClient> Tenant { get; } = new();
        public Mock<IRegisterServiceClient> Register { get; } = new();
        public Mock<IWalletServiceClient> Wallet { get; } = new();
        public Mock<IHumanApproval> Approval { get; } = new();

        public InitiateRegisterCreationRequest? CapturedInitiateRequest { get; private set; }
        public FinalizeRegisterCreationRequest? CapturedFinalizeRequest { get; private set; }
        public HumanApprovalRequest? CapturedApprovalRequest { get; private set; }

        /// <summary>
        /// A real <c>McpServer</c> stand-in rather than null: the tool hands this straight to
        /// <see cref="IHumanApproval"/>, and the SDK type is non-mockable.
        /// </summary>
        public SdkMcpServer Server { get; } = new FakeMcpServer(new ClientCapabilities());

        public Harness()
        {
            Auth.Setup(a => a.CanInvokeTool("sorcha_register_create")).Returns(true);
            Availability.Setup(a => a.IsServiceAvailable(It.IsAny<string>())).Returns(true);
            Caller.SetupGet(c => c.OrganizationId).Returns(OrgId);

            Tenant.Setup(t => t.GetOrganizationAsync(OrgId, It.IsAny<CancellationToken>()))
                .ReturnsAsync($$"""{"id":"{{OrgId}}","name":"Acme","walletAddress":"ws11qorg"}""");

            Approval.Setup(a => a.RequestAsync(
                    It.IsAny<SdkMcpServer>(), It.IsAny<HumanApprovalRequest>(), It.IsAny<CancellationToken>()))
                .Callback<SdkMcpServer, HumanApprovalRequest, CancellationToken>(
                    (_, r, _) => CapturedApprovalRequest = r)
                .ReturnsAsync(new ApprovalResult(ApprovalOutcome.Refused, "default: not approved"));
        }

        public Harness WithApproval()
        {
            Approval.Setup(a => a.RequestAsync(
                    It.IsAny<SdkMcpServer>(), It.IsAny<HumanApprovalRequest>(), It.IsAny<CancellationToken>()))
                .Callback<SdkMcpServer, HumanApprovalRequest, CancellationToken>(
                    (_, r, _) => CapturedApprovalRequest = r)
                .ReturnsAsync(new ApprovalResult(ApprovalOutcome.Approved, "Approved by the user."));
            return this;
        }

        public Harness WithApprovalOutcome(ApprovalOutcome outcome, string detail)
        {
            Approval.Setup(a => a.RequestAsync(
                    It.IsAny<SdkMcpServer>(), It.IsAny<HumanApprovalRequest>(), It.IsAny<CancellationToken>()))
                .Callback<SdkMcpServer, HumanApprovalRequest, CancellationToken>(
                    (_, r, _) => CapturedApprovalRequest = r)
                .ReturnsAsync(new ApprovalResult(outcome, detail));
            return this;
        }

        public Harness WithInitiate(string walletId, string dataToSignHex)
        {
            Register.Setup(r => r.InitiateRegisterCreationAsync(
                    It.IsAny<InitiateRegisterCreationRequest>(), It.IsAny<CancellationToken>()))
                .Callback<InitiateRegisterCreationRequest, CancellationToken>(
                    (req, _) => CapturedInitiateRequest = req)
                .ReturnsAsync(new InitiateRegisterCreationResponse
                {
                    RegisterId = RegisterId,
                    Nonce = "nonce-1",
                    ExpiresAt = DateTimeOffset.UtcNow.AddMinutes(5),
                    AttestationsToSign =
                    [
                        new AttestationToSign
                        {
                            UserId = OrgId,
                            WalletId = walletId,
                            Role = RegisterRole.Owner,
                            DataToSign = dataToSignHex,
                            AttestationData = new AttestationSigningData
                            {
                                Role = RegisterRole.Owner,
                                Subject = walletId,
                                RegisterId = RegisterId,
                                RegisterName = "Acme Supply",
                                GrantedAt = DateTimeOffset.UtcNow
                            }
                        }
                    ]
                });

            Wallet.Setup(w => w.SignTransactionAsync(
                    It.IsAny<string>(), It.IsAny<byte[]>(), It.IsAny<string?>(), It.IsAny<bool>(),
                    It.IsAny<CancellationToken>()))
                .ReturnsAsync(new WalletSignResult
                {
                    Signature = [1, 2, 3],
                    PublicKey = [4, 5, 6],
                    SignedBy = walletId,
                    Algorithm = "ED25519"
                });

            Register.Setup(r => r.FinalizeRegisterCreationAsync(
                    It.IsAny<FinalizeRegisterCreationRequest>(), It.IsAny<CancellationToken>()))
                .Callback<FinalizeRegisterCreationRequest, CancellationToken>(
                    (req, _) => CapturedFinalizeRequest = req)
                .ReturnsAsync(new FinalizeRegisterCreationResponse
                {
                    RegisterId = RegisterId,
                    Status = "created",
                    GenesisTransactionId = "genesis-tx-1",
                    GenesisDocketId = "0",
                    CreatedAt = DateTimeOffset.UtcNow
                });

            return this;
        }

        public RegisterCreateTool Sut() => new(
            Auth.Object,
            Availability.Object,
            Caller.Object,
            Tenant.Object,
            Register.Object,
            Wallet.Object,
            Approval.Object,
            NullLogger<RegisterCreateTool>.Instance);
    }
}
