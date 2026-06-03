// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

using FluentAssertions;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.FileProviders;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Sorcha.Tenant.Service.Extensions;
using Sorcha.Tenant.Service.Services;

namespace Sorcha.Tenant.Service.Tests.Services;

/// <summary>
/// Tests for <see cref="TenantSecretKeyResolver"/>: HKDF derivation from the JWT signing key,
/// override precedence + validation, fail-closed behaviour, and the distinct login-token key.
/// </summary>
public class TenantSecretKeyResolverTests
{
    private const string SampleSigningKey = "super-secret-signing-key-value-1234567890";

    private static TenantSecretKeyResolver Create(
        string? signingKey,
        string? overrideKey = null,
        string environmentName = "Development")
    {
        var jwt = Options.Create(new JwtConfiguration { SigningKey = signingKey ?? string.Empty });

        var settings = new Dictionary<string, string?>();
        if (overrideKey is not null)
        {
            settings[TenantSecretKeyResolver.OverrideConfigPath] = overrideKey;
        }
        var config = new ConfigurationBuilder().AddInMemoryCollection(settings).Build();

        var environment = new FakeEnvironment { EnvironmentName = environmentName };

        return new TenantSecretKeyResolver(jwt, config, environment, NullLogger<TenantSecretKeyResolver>.Instance);
    }

    [Fact]
    public void ResolveProtectionKey_DerivesFromSigningKey_Deterministically()
    {
        var a = Create(SampleSigningKey).ResolveProtectionKey();
        var b = Create(SampleSigningKey).ResolveProtectionKey();

        a.KeyId.Should().Be(TenantSecretKeyResolver.DerivedKeyId);
        a.Key.Should().HaveCount(32);
        a.Key.Should().Equal(b.Key); // same root ⇒ same key (cross-replica / restart stable)
    }

    [Fact]
    public void ResolveProtectionKey_OverrideTakesPrecedenceOverDerivation()
    {
        var keyBytes = new byte[32];
        keyBytes[0] = 0x42;
        var b64 = Convert.ToBase64String(keyBytes);

        var result = Create(SampleSigningKey, overrideKey: b64).ResolveProtectionKey();

        result.KeyId.Should().Be(TenantSecretKeyResolver.ConfigKeyId);
        result.Key.Should().Equal(keyBytes);
    }

    [Fact]
    public void ResolveProtectionKey_OverrideWrongLength_Throws()
    {
        var b64 = Convert.ToBase64String(new byte[16]);

        var act = () => Create(SampleSigningKey, overrideKey: b64).ResolveProtectionKey();

        act.Should().Throw<InvalidOperationException>();
    }

    [Fact]
    public void ResolveProtectionKey_OverrideInvalidBase64_Throws()
    {
        var act = () => Create(SampleSigningKey, overrideKey: "!!! not base64 !!!").ResolveProtectionKey();

        act.Should().Throw<InvalidOperationException>();
    }

    [Fact]
    public void ResolveProtectionKey_NoKeyInProduction_FailsClosed()
    {
        var act = () => Create(signingKey: null, environmentName: "Production").ResolveProtectionKey();

        act.Should().Throw<InvalidOperationException>();
    }

    [Fact]
    public void ResolveLoginTokenSigningKey_DerivesDeterministically_AndDiffersFromProtectionKey()
    {
        var resolver = Create(SampleSigningKey);

        var login1 = resolver.ResolveLoginTokenSigningKey();
        var login2 = Create(SampleSigningKey).ResolveLoginTokenSigningKey();

        login1.Should().HaveCount(32);
        login1.Should().Equal(login2);                                  // stable across replicas/restarts
        login1.Should().NotEqual(resolver.ResolveProtectionKey().Key);  // domain-separated (distinct HKDF info)
    }

    [Fact]
    public void ResolveLoginTokenSigningKey_NoSigningKey_Throws()
    {
        var act = () => Create(signingKey: null).ResolveLoginTokenSigningKey();

        act.Should().Throw<InvalidOperationException>();
    }

    private sealed class FakeEnvironment : IHostEnvironment
    {
        public string EnvironmentName { get; set; } = "Development";
        public string ApplicationName { get; set; } = "Sorcha.Tenant.Service.Tests";
        public string ContentRootPath { get; set; } = AppContext.BaseDirectory;
        public IFileProvider ContentRootFileProvider { get; set; } = new NullFileProvider();
    }
}
