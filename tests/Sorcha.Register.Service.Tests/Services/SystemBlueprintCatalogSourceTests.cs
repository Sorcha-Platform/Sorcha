// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

using System.Text.Json;
using FluentAssertions;
using Microsoft.Extensions.Logging;
using Moq;
using Sorcha.Cryptography.Enums;
using Sorcha.Cryptography.Interfaces;
using Sorcha.Register.Core.Events;
using Sorcha.Register.Core.Managers;
using Sorcha.Register.Core.Storage;
using Sorcha.Register.Service.Services;
using Sorcha.ServiceClients.SystemWallet;
using Sorcha.ServiceClients.Validator;
using Xunit;

namespace Sorcha.Register.Service.Tests.Services;

/// <summary>
/// Feature 197 T003 — the publication id the catalogue source computes for a shipped template must be
/// the id a publish of that template actually submits. Two constructions of one identity drift silently.
/// </summary>
public class SystemBlueprintCatalogSourceTests : IDisposable
{
    // Deliberately NOT key-sorted and with escapable characters, so a non-canonical computation differs.
    private const string TemplateJson =
        "{\"title\":\"T003 <catalog> source\",\"participants\":[{\"id\":\"b\",\"name\":\"B\"}]," +
        "\"actions\":[{\"title\":\"z\",\"id\":0}],\"description\":\"a & b\"}";

    private readonly string _id = $"t003-source-{Guid.NewGuid():N}-v1";
    private readonly string _path;

    public SystemBlueprintCatalogSourceTests()
    {
        var dir = Path.Combine(AppContext.BaseDirectory, "blueprints", "templates");
        Directory.CreateDirectory(dir);
        _path = Path.Combine(dir, $"{_id}.json");
        File.WriteAllText(_path, TemplateJson);
    }

    public void Dispose()
    {
        if (File.Exists(_path)) File.Delete(_path);
    }

    [Fact]
    public async Task TryComputePublicationId_ShippedTemplate_EqualsTransactionIdOfAPublish()
    {
        var source = new SystemBlueprintCatalogSource();
        var computed = source.TryComputePublicationId(_id);

        var submitted = new List<TransactionSubmission>();
        var validator = new Mock<IValidatorServiceClient>();
        validator
            .Setup(v => v.SubmitTransactionAsync(It.IsAny<TransactionSubmission>(), It.IsAny<CancellationToken>()))
            .Callback<TransactionSubmission, CancellationToken>((s, _) => submitted.Add(s))
            .ReturnsAsync(new TransactionSubmissionResult { Success = true, TransactionId = "x", RegisterId = "y" });

        var signing = new Mock<ISystemWalletSigningService>();
        signing
            .Setup(s => s.SignAsync(
                It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(),
                It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new SystemSignResult
            {
                Signature = new byte[64], PublicKey = new byte[32], Algorithm = "ED25519", WalletAddress = "sys"
            });

        var hash = new Mock<IHashProvider>();
        hash.Setup(h => h.ComputeHash(It.IsAny<byte[]>(), It.IsAny<HashType>())).Returns(new byte[32]);

        var repo = new Mock<IRegisterRepository>();
        RegisterMockHelpers.StubTransactionsByTypeReadThrough(repo);
        var events = new Mock<IEventPublisher>();
        var service = new SystemRegisterService(
            new Mock<ILogger<SystemRegisterService>>().Object,
            new RegisterManager(repo.Object, events.Object),
            new TransactionManager(repo.Object, events.Object),
            validator.Object,
            signing.Object,
            hash.Object);

        var template = source.TryLoad(_id)!.Value;
        await service.PublishBlueprintAsync(_id, template, "test");

        submitted.Should().ContainSingle();
        computed.Should().NotBeNull();
        computed.Should().Be(submitted[0].TransactionId);
    }

    [Fact]
    public void TryLoad_UnknownId_ReturnsNull()
    {
        var source = new SystemBlueprintCatalogSource();
        source.TryLoad("no-such-blueprint-v9").Should().BeNull();
        source.TryComputePublicationId("no-such-blueprint-v9").Should().BeNull();
    }
}
