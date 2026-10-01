// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

using System.Text.Json;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Sorcha.Cryptography.Interfaces;
using Sorcha.Register.Core.Events;
using Sorcha.Register.Core.Managers;
using Sorcha.Register.Models;
using Sorcha.Register.Service.Services;
using Sorcha.Register.Storage.InMemory;
using Sorcha.ServiceClients.SystemWallet;
using Sorcha.ServiceClients.Validator;
using Xunit;

namespace Sorcha.Register.Service.Tests.Services;

/// <summary>Feature 197: drift between the image's system blueprints and the system register.</summary>
public class SystemBlueprintDriftReporterTests
{
    private static readonly DateTimeOffset Now = new(2026, 10, 1, 0, 0, 0, TimeSpan.Zero);

    private static TransactionModel Tx(string id) => new() { TxId = id };

    [Fact]
    public void Classify_ImageIsCurrent_InSync()
    {
        var e = SystemBlueprintDriftReporter.Classify("bp", "b", [Tx("a"), Tx("b")], Now);

        e.State.Should().Be(SystemBlueprintDriftState.InSync);
        e.CurrentVersion.Should().Be(2);
        e.CurrentPublicationTxId.Should().Be("b");
        e.ImageMatchesVersion.Should().BeNull();
    }

    [Fact]
    public void Classify_ImageMatchesOlderPublication_ImageBehindWithThatVersion()
    {
        var e = SystemBlueprintDriftReporter.Classify("bp", "b", [Tx("a"), Tx("b"), Tx("c")], Now);

        e.State.Should().Be(SystemBlueprintDriftState.ImageBehind);
        e.ImageMatchesVersion.Should().Be(2);
        e.CurrentVersion.Should().Be(3);
        e.CurrentPublicationTxId.Should().Be("c");
    }

    [Fact]
    public void Classify_ImageMatchesNoPublication_ImageAhead()
    {
        var e = SystemBlueprintDriftReporter.Classify("bp", "z", [Tx("a"), Tx("b")], Now);

        e.State.Should().Be(SystemBlueprintDriftState.ImageAhead);
        e.ImageMatchesVersion.Should().BeNull();
        e.ImagePublicationTxId.Should().Be("z");
    }

    [Fact]
    public void Classify_NoPublicationsAndNoImage_Missing()
    {
        var e = SystemBlueprintDriftReporter.Classify("bp", null, [], Now);

        e.State.Should().Be(SystemBlueprintDriftState.Missing);
        e.CurrentPublicationTxId.Should().BeNull();
        e.CurrentVersion.Should().BeNull();
    }

    [Fact]
    public void Classify_PublicationsButImageHasNoTemplate_UnknownWithNullImageId()
    {
        var e = SystemBlueprintDriftReporter.Classify("bp", null, [Tx("a")], Now);

        e.State.Should().Be(SystemBlueprintDriftState.Unknown);
        e.ImagePublicationTxId.Should().BeNull();
        e.CurrentPublicationTxId.Should().Be("a");
    }

    [Fact]
    public async Task ComputeAsync_ReadThrows_UnknownForThatEntryOnly()
    {
        var failing = SystemBlueprintCatalog.Ids[1];
        var reporter = BuildReporter(
            id => id == failing ? throw new InvalidOperationException("ssr unreadable") : [Tx("img-" + id)]);

        var entries = await reporter.ComputeAsync();

        entries.Select(x => x.BlueprintId).Should().Equal(SystemBlueprintCatalog.Ids);
        entries.Single(x => x.BlueprintId == failing).State.Should().Be(SystemBlueprintDriftState.Unknown);
        entries.Where(x => x.BlueprintId != failing)
            .Should().OnlyContain(x => x.State == SystemBlueprintDriftState.InSync);
        entries.Should().OnlyContain(x => x.CheckedAt == Now);
    }

    [Fact]
    public void State_SerialisesAsKebabCaseName_UnderSorchaJson()
    {
        var entry = SystemBlueprintDriftReporter.Classify("bp", "b", [Tx("a"), Tx("b"), Tx("c")], Now);

        JsonSerializer.Serialize(entry, Sorcha.Serialization.SorchaJson.Options).Should().Contain("\"state\":\"image-behind\"");
    }

    private static SystemBlueprintDriftReporter BuildReporter(Func<string, IReadOnlyList<TransactionModel>> publications)
    {
        var service = new Mock<SystemRegisterService>(
            NullLogger<SystemRegisterService>.Instance,
            new RegisterManager(new InMemoryRegisterRepository(), new Mock<IEventPublisher>().Object),
            new TransactionManager(new InMemoryRegisterRepository(), new Mock<IEventPublisher>().Object),
            new Mock<IValidatorServiceClient>().Object,
            new Mock<ISystemWalletSigningService>().Object,
            new Mock<IHashProvider>().Object);
        service.Setup(s => s.GetPublicationsAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((string id, CancellationToken _) => publications(id));

        var catalog = new Mock<ISystemBlueprintCatalogSource>();
        catalog.Setup(c => c.TryComputePublicationId(It.IsAny<string>())).Returns((string id) => "img-" + id);

        var services = new ServiceCollection().AddScoped(_ => service.Object).BuildServiceProvider();
        var time = new Mock<TimeProvider>();
        time.Setup(t => t.GetUtcNow()).Returns(Now);

        return new SystemBlueprintDriftReporter(
            catalog.Object,
            services.GetRequiredService<IServiceScopeFactory>(),
            time.Object,
            NullLogger<SystemBlueprintDriftReporter>.Instance);
    }
}
