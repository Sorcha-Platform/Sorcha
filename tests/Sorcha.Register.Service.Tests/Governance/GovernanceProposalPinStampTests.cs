// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using FluentAssertions;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Moq;
using Sorcha.Register.Core.Services;
using Sorcha.Register.Core.Storage;
using Sorcha.Register.Models;
using Sorcha.Register.Service.Services;
using Sorcha.Register.Service.Tests.Helpers;
using Sorcha.ServiceClients.Validator;
using Sorcha.Validator.Core.Validators;
using Xunit;

namespace Sorcha.Register.Service.Tests.Governance;

/// <summary>
/// Feature 197 T013 — every governance proposal is pinned, at the moment it is raised, to the
/// current publication id of the governance blueprint, on BOTH construction paths (a pending
/// proposal and an Owner-override propose-and-enact). With no readable definition nothing is
/// signed or submitted.
/// </summary>
[Collection("RegisterWebApp")]
public class GovernanceProposalPinStampTests : IClassFixture<GovernanceProposalPinStampTests.PinStampFactory>
{
    private const string CurrentPin = "pin-current-publication-tx-id";

    private readonly PinStampFactory _factory;
    private readonly HttpClient _client;
    private readonly string _registerId;

    public GovernanceProposalPinStampTests(PinStampFactory factory)
    {
        _factory = factory;
        _factory.Reset();
        _client = factory.CreateClient();
        _registerId = factory.CreateTestRegisterAsync("Pin Stamp Register", "pin-stamp-tenant").Result.Id;
    }

    [Fact]
    public async Task Propose_PendingPath_StampsCurrentGovernanceDefinitionPin()
    {
        _factory.QuorumMet = false;
        _factory.CurrentPin = CurrentPin;

        var response = await Propose();

        response.StatusCode.Should().Be(HttpStatusCode.Accepted);
        var payload = SingleSubmittedPayload();
        payload.Roster.Should().BeNull("a pending proposal carries no roster");
        payload.GovernanceDefinitionTxId.Should().Be(CurrentPin);
    }

    [Fact]
    public async Task Propose_OwnerOverridePath_StampsCurrentGovernanceDefinitionPin()
    {
        _factory.QuorumMet = true;
        _factory.CurrentPin = CurrentPin;

        var response = await Propose();

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var payload = SingleSubmittedPayload();
        payload.Roster.Should().NotBeNull("the override enacts in the same transaction");
        payload.GovernanceDefinitionTxId.Should().Be(CurrentPin);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Propose_PinSourceUnavailable_Returns503AndSubmitsNothing(bool quorumMet)
    {
        _factory.QuorumMet = quorumMet;
        _factory.CurrentPin = null;

        var response = await Propose();

        response.StatusCode.Should().Be(HttpStatusCode.ServiceUnavailable);
        _factory.Submissions.Should().BeEmpty("nothing may be signed or submitted without a pin");
        _factory.SigningCalls.Should().Be(0);
    }

    [Fact]
    public void BuildEnactmentPayload_CarriesNoPin()
    {
        // The enacting transaction is a different shape: it names the proposal it enacts, and the
        // proposal carries the pin. Stamping the enactment too would give one decision two pins.
        var rosterService = new Mock<IGovernanceRosterService>();
        rosterService
            .Setup(r => r.ApplyOperation(
                It.IsAny<RegisterControlRecord>(), It.IsAny<GovernanceOperation>(), It.IsAny<RegisterAttestation?>()))
            .Returns(new RegisterControlRecord());
        var sut = new GovernanceEnactmentService(
            Mock.Of<IGovernanceProposalReader>(), rosterService.Object, Mock.Of<IReadOnlyRegisterRepository>(),
            Mock.Of<IGovernanceSigningService>(), Mock.Of<IValidatorServiceClient>(),
            Mock.Of<ILogger<GovernanceEnactmentService>>());

        var payload = sut.BuildEnactmentPayload(
            new AdminRoster { ControlRecord = new RegisterControlRecord() },
            new GovernanceOperation
            {
                OperationType = GovernanceOperationType.Remove,
                TargetDid = "did:sorcha:w:target",
                ProposerDid = "did:sorcha:w:proposer"
            },
            "proposal-tx-id");

        payload.GovernanceDefinitionTxId.Should().BeNull();
        payload.EnactsProposalId.Should().Be("proposal-tx-id");
    }

    private Task<HttpResponseMessage> Propose() =>
        _client.PostAsJsonAsync($"/api/registers/{_registerId}/governance/propose", new
        {
            operationType = GovernanceOperationType.Remove.ToString(),
            proposerDid = "did:sorcha:w:proposer",
            targetDid = "did:sorcha:w:target",
        });

    private ControlTransactionPayload SingleSubmittedPayload()
    {
        var submission = _factory.Submissions.Should().ContainSingle().Subject;
        return submission.Payload.Deserialize<ControlTransactionPayload>(ControlTransactionPayload.CanonicalJsonOptions)!;
    }

    /// <summary>Host with the governance collaborators faked so the propose handler runs end to end.</summary>
    public class PinStampFactory : RegisterServiceWebApplicationFactory
    {
        private readonly List<TransactionSubmission> _submissions = new();

        public string? CurrentPin { get; set; }
        public bool QuorumMet { get; set; }
        public int SigningCalls { get; private set; }

        public IReadOnlyList<TransactionSubmission> Submissions
        {
            get { lock (_submissions) { return _submissions.ToList(); } }
        }

        public void Reset()
        {
            lock (_submissions) { _submissions.Clear(); }
            SigningCalls = 0;
            CurrentPin = null;
            QuorumMet = false;
        }

        protected override void ConfigureWebHost(IWebHostBuilder builder)
        {
            base.ConfigureWebHost(builder);

            builder.ConfigureServices(services =>
            {
                var pin = new Mock<IGovernanceDefinitionPinSource>();
                pin.Setup(p => p.GetCurrentAsync(It.IsAny<CancellationToken>()))
                    .ReturnsAsync(() => CurrentPin);
                services.AddSingleton(pin.Object);

                var roster = new Mock<IGovernanceRosterService>();
                roster.Setup(r => r.GetCurrentRosterAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
                    .ReturnsAsync(new AdminRoster
                    {
                        ControlRecord = new RegisterControlRecord(),
                        LastControlTxId = "prev-control-tx"
                    });
                roster.Setup(r => r.ValidateProposal(It.IsAny<AdminRoster>(), It.IsAny<GovernanceOperation>()))
                    .Returns(GovernanceValidationResult.Success());
                roster.Setup(r => r.ValidateQuorumAsync(
                        It.IsAny<string>(), It.IsAny<GovernanceOperation>(),
                        It.IsAny<List<ApprovalSignature>>(), It.IsAny<CancellationToken>()))
                    .ReturnsAsync(() => new QuorumResult { IsQuorumMet = QuorumMet, IsOwnerOverride = QuorumMet });
                roster.Setup(r => r.ApplyOperation(
                        It.IsAny<RegisterControlRecord>(), It.IsAny<GovernanceOperation>(),
                        It.IsAny<RegisterAttestation?>()))
                    .Returns(new RegisterControlRecord());
                services.AddSingleton(roster.Object);

                var seat = new Mock<ISeatAcceptanceVerifier>();
                seat.Setup(s => s.VerifyAsync(
                        It.IsAny<string>(), It.IsAny<GovernanceOperation>(), It.IsAny<CancellationToken>()))
                    .ReturnsAsync(SeatAcceptanceResult.Ok());
                services.AddSingleton(seat.Object);

                var signing = new Mock<IGovernanceSigningService>();
                signing.Setup(s => s.SignAsync(
                        It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(),
                        It.IsAny<string?>(), It.IsAny<CancellationToken>()))
                    .ReturnsAsync(() =>
                    {
                        SigningCalls++;
                        return new GovernanceSignResult
                        {
                            Signature = new byte[64],
                            PublicKey = new byte[32],
                            Algorithm = "ED25519",
                            WalletAddress = "ws1test",
                            Subject = "did:sorcha:w:proposer"
                        };
                    });
                services.AddSingleton(signing.Object);

                var validator = new Mock<IValidatorServiceClient>();
                validator.Setup(v => v.SubmitTransactionAsync(
                        It.IsAny<TransactionSubmission>(), It.IsAny<CancellationToken>()))
                    .ReturnsAsync((TransactionSubmission s, CancellationToken _) =>
                    {
                        lock (_submissions) { _submissions.Add(s); }
                        return new TransactionSubmissionResult
                        {
                            Success = true,
                            TransactionId = s.TransactionId,
                            RegisterId = s.RegisterId,
                            AddedAt = DateTimeOffset.UtcNow
                        };
                    });
                services.AddSingleton(validator.Object);
            });
        }
    }
}
