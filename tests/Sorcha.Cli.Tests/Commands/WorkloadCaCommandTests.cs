// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

using System.CommandLine;
using System.Security.Cryptography.X509Certificates;
using FluentAssertions;
using Sorcha.Cli.Commands.WorkloadCa;
using Sorcha.WorkloadIdentity;

namespace Sorcha.Cli.Tests.Commands;

/// <summary>
/// F191 US2 — `sorcha workload-ca` lifecycle round-trips in a temp directory:
/// init (idempotent) → status (exit-code contract) → renew (threshold + --all) →
/// rotate-ca (overlap) → rotate-ca --complete.
/// </summary>
public sealed class WorkloadCaCommandTests : IDisposable
{
    private readonly string _dir;

    public WorkloadCaCommandTests()
    {
        _dir = Path.Combine(Path.GetTempPath(), $"workload-ca-{Guid.NewGuid():N}");
    }

    public void Dispose()
    {
        if (Directory.Exists(_dir))
            Directory.Delete(_dir, recursive: true);
    }

    private static Task<int> RunAsync(string args)
    {
        var command = new WorkloadCaCommand();
        var root = new RootCommand { command };
        return root.Parse($"workload-ca {args}").InvokeAsync();
    }

    private Task<int> InitAsync(string extra = "") =>
        RunAsync($"init --dir \"{_dir}\" --installation sorcha --password pw {extra}");

    private X509Certificate2 LoadPfx(string relativePath) =>
        X509CertificateLoader.LoadPkcs12FromFile(Path.Combine(_dir, relativePath), "pw",
            X509KeyStorageFlags.Exportable);

    private WorkloadTrustBundle LoadBundle() =>
        WorkloadTrustBundle.LoadFromFile(Path.Combine(_dir, "ca", "bundle.pem"));

    [Fact]
    public async Task Init_CreatesCaBundleLeavesAndServerCert()
    {
        var exit = await InitAsync();

        exit.Should().Be(0);
        File.Exists(Path.Combine(_dir, "ca", "ca.pfx")).Should().BeTrue();
        File.Exists(Path.Combine(_dir, "ca", "bundle.pem")).Should().BeTrue();
        File.Exists(Path.Combine(_dir, "server", "tenant-service.pfx")).Should().BeTrue();

        foreach (var clientId in WorkloadIdentityConfig.DefaultServiceDnsMap.Keys)
        {
            File.Exists(Path.Combine(_dir, "services", $"{clientId}.pfx"))
                .Should().BeTrue($"a leaf must exist for {clientId}");
        }

        // Leaves carry the right identity and chain to the bundle.
        using var blueprintLeaf = LoadPfx(Path.Combine("services", "service-blueprint.pfx"));
        WorkloadCertificateAuthority.TryGetSpiffeId(blueprintLeaf, out var id).Should().BeTrue();
        id!.ToString().Should().Be("spiffe://sorcha/service/service-blueprint");
        LoadBundle().Validate(blueprintLeaf, out var reason).Should().BeTrue(reason);

        // The bundle is public-only material.
        LoadBundle().Roots.Should().OnlyContain(r => !r.HasPrivateKey);
    }

    [Fact]
    public async Task Init_IsIdempotent_ValidMaterialIsUntouched()
    {
        (await InitAsync()).Should().Be(0);
        using var firstCa = LoadPfx(Path.Combine("ca", "ca.pfx"));
        using var firstLeaf = LoadPfx(Path.Combine("services", "service-wallet.pfx"));

        (await InitAsync()).Should().Be(0);

        using var secondCa = LoadPfx(Path.Combine("ca", "ca.pfx"));
        using var secondLeaf = LoadPfx(Path.Combine("services", "service-wallet.pfx"));
        secondCa.Thumbprint.Should().Be(firstCa.Thumbprint, "a valid CA must never be re-issued by init");
        secondLeaf.Thumbprint.Should().Be(firstLeaf.Thumbprint, "valid leaves must never be re-issued by init");
    }

    [Fact]
    public async Task Init_ReissuesOnlyMissingPieces()
    {
        (await InitAsync()).Should().Be(0);
        using var firstCa = LoadPfx(Path.Combine("ca", "ca.pfx"));
        File.Delete(Path.Combine(_dir, "services", "service-peer.pfx"));

        (await InitAsync()).Should().Be(0);

        using var secondCa = LoadPfx(Path.Combine("ca", "ca.pfx"));
        secondCa.Thumbprint.Should().Be(firstCa.Thumbprint);
        File.Exists(Path.Combine(_dir, "services", "service-peer.pfx")).Should().BeTrue();
        using var reissued = LoadPfx(Path.Combine("services", "service-peer.pfx"));
        LoadBundle().Validate(reissued, out var reason).Should().BeTrue(reason);
    }

    [Fact]
    public async Task Init_ServicesOverride_LimitsTheIssuanceUniverse()
    {
        var exit = await InitAsync("--services service-blueprint=blueprint-service");

        exit.Should().Be(0);
        File.Exists(Path.Combine(_dir, "services", "service-blueprint.pfx")).Should().BeTrue();
        Directory.GetFiles(Path.Combine(_dir, "services")).Should().HaveCount(1);
    }

    [Fact]
    public async Task Status_HealthyMaterial_ExitsZero()
    {
        (await InitAsync()).Should().Be(0);

        var exit = await RunAsync($"status --dir \"{_dir}\" --password pw");

        exit.Should().Be(0);
    }

    [Fact]
    public async Task Status_InsideThreshold_ExitsTwo()
    {
        (await InitAsync()).Should().Be(0);

        // Leaves are ~2 years (≈730 days); an 800-day threshold puts everything inside it.
        var exit = await RunAsync($"status --dir \"{_dir}\" --password pw --threshold-days 800");

        exit.Should().Be(2);
    }

    [Fact]
    public async Task Status_MissingDirectory_ExitsOne()
    {
        var exit = await RunAsync($"status --dir \"{_dir}\" --password pw");

        exit.Should().Be(1);
    }

    [Fact]
    public async Task Renew_OutsideThreshold_ReissuesNothing()
    {
        (await InitAsync()).Should().Be(0);
        using var before = LoadPfx(Path.Combine("services", "service-wallet.pfx"));

        (await RunAsync($"renew --dir \"{_dir}\" --password pw --threshold-days 30")).Should().Be(0);

        using var after = LoadPfx(Path.Combine("services", "service-wallet.pfx"));
        after.Thumbprint.Should().Be(before.Thumbprint);
    }

    [Fact]
    public async Task Renew_InsideThreshold_ReissuesWithFreshKeypairUnderSameCa()
    {
        (await InitAsync()).Should().Be(0);
        using var caBefore = LoadPfx(Path.Combine("ca", "ca.pfx"));
        using var before = LoadPfx(Path.Combine("services", "service-wallet.pfx"));

        (await RunAsync($"renew --dir \"{_dir}\" --password pw --threshold-days 800")).Should().Be(0);

        using var caAfter = LoadPfx(Path.Combine("ca", "ca.pfx"));
        using var after = LoadPfx(Path.Combine("services", "service-wallet.pfx"));
        caAfter.Thumbprint.Should().Be(caBefore.Thumbprint, "renew never touches the CA");
        after.SerialNumber.Should().NotBe(before.SerialNumber);
        after.GetPublicKeyString().Should().NotBe(before.GetPublicKeyString(), "renew issues a fresh keypair");
        LoadBundle().Validate(after, out var reason).Should().BeTrue(reason);
    }

    [Fact]
    public async Task Renew_All_ReissuesEverythingRegardlessOfThreshold()
    {
        (await InitAsync()).Should().Be(0);
        using var before = LoadPfx(Path.Combine("server", "tenant-service.pfx"));

        (await RunAsync($"renew --dir \"{_dir}\" --password pw --all")).Should().Be(0);

        using var after = LoadPfx(Path.Combine("server", "tenant-service.pfx"));
        after.SerialNumber.Should().NotBe(before.SerialNumber);
    }

    [Fact]
    public async Task RotateCa_CreatesOverlapBundleAndReissuesAllLeaves()
    {
        (await InitAsync()).Should().Be(0);
        using var oldCa = LoadPfx(Path.Combine("ca", "ca.pfx"));
        using var oldLeaf = LoadPfx(Path.Combine("services", "service-blueprint.pfx"));

        (await RunAsync($"rotate-ca --dir \"{_dir}\" --installation sorcha --password pw")).Should().Be(0);

        var bundle = LoadBundle();
        bundle.Roots.Should().HaveCount(2, "rotation overlap carries both roots");
        File.Exists(Path.Combine(_dir, "ca", "ca.previous.pfx")).Should().BeTrue();

        using var newCa = LoadPfx(Path.Combine("ca", "ca.pfx"));
        newCa.Thumbprint.Should().NotBe(oldCa.Thumbprint);

        // New leaves chain to the new root; a still-deployed OLD-root leaf also validates (overlap).
        using var newLeaf = LoadPfx(Path.Combine("services", "service-blueprint.pfx"));
        newLeaf.Thumbprint.Should().NotBe(oldLeaf.Thumbprint);
        bundle.Validate(newLeaf, out var newReason).Should().BeTrue(newReason);
        bundle.Validate(oldLeaf, out var oldReason).Should().BeTrue(oldReason);
    }

    [Fact]
    public async Task RotateCaComplete_DropsOldRoot()
    {
        (await InitAsync()).Should().Be(0);
        using var oldLeaf = LoadPfx(Path.Combine("services", "service-blueprint.pfx"));
        (await RunAsync($"rotate-ca --dir \"{_dir}\" --installation sorcha --password pw")).Should().Be(0);

        (await RunAsync($"rotate-ca --dir \"{_dir}\" --password pw --complete")).Should().Be(0);

        var bundle = LoadBundle();
        bundle.Roots.Should().HaveCount(1);
        File.Exists(Path.Combine(_dir, "ca", "ca.previous.pfx")).Should().BeFalse();
        bundle.Validate(oldLeaf, out _).Should().BeFalse("old-root leaves must be refused after completion");
        using var newLeaf = LoadPfx(Path.Combine("services", "service-blueprint.pfx"));
        bundle.Validate(newLeaf, out var reason).Should().BeTrue(reason);
    }

    [Fact]
    public async Task RotateCaComplete_WithoutPendingRotation_ExitsOne()
    {
        (await InitAsync()).Should().Be(0);

        var exit = await RunAsync($"rotate-ca --dir \"{_dir}\" --password pw --complete");

        exit.Should().Be(1, "there is no overlap to complete on a single-root bundle");
    }
}
