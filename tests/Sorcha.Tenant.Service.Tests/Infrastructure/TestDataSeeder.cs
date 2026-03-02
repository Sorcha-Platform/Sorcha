// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

using System.Security.Cryptography;
using System.Text;

using Microsoft.EntityFrameworkCore;

using Org.BouncyCastle.Crypto.Generators;
using Org.BouncyCastle.Crypto.Parameters;

using Sorcha.Tenant.Service.Data;
using Sorcha.Tenant.Service.Models;

namespace Sorcha.Tenant.Service.Tests.Infrastructure;

/// <summary>
/// Seeds test data for integration tests.
/// Provides well-known IDs for consistent test assertions.
/// </summary>
public static class TestDataSeeder
{
    // Well-known test organization ID
    public static readonly Guid TestOrganizationId = new("00000000-0000-0000-0000-000000000001");

    // Well-known test user IDs
    public static readonly Guid AdminUserId = new("00000000-0000-0000-0001-000000000001");
    public static readonly Guid MemberUserId = new("00000000-0000-0000-0001-000000000002");
    public static readonly Guid AuditorUserId = new("00000000-0000-0000-0001-000000000003");

    /// <summary>
    /// Seeds test data into the database context (idempotent - safe to call multiple times).
    /// </summary>
    public static async Task SeedAsync(TenantDbContext context)
    {
        // Check if data already exists (idempotent seeding)
        if (await context.Organizations.AnyAsync(o => o.Id == TestOrganizationId))
        {
            return; // Data already seeded
        }

        // Create test organization
        var testOrg = new Organization
        {
            Id = TestOrganizationId,
            Name = "Test Organization",
            Subdomain = "test-org",
            Status = OrganizationStatus.Active,
            CreatedAt = DateTimeOffset.UtcNow
        };

        context.Organizations.Add(testOrg);

        // Create admin user
        var adminUser = new UserIdentity
        {
            Id = AdminUserId,
            Email = "admin@test-org.sorcha.io",
            DisplayName = "Admin User",
            PasswordHash = BCrypt.Net.BCrypt.HashPassword("TestPassword123!"),
            Status = IdentityStatus.Active,
            Roles = new[] { UserRole.Administrator },
            OrganizationId = TestOrganizationId,
            CreatedAt = DateTimeOffset.UtcNow
        };

        // Create member user
        var memberUser = new UserIdentity
        {
            Id = MemberUserId,
            Email = "member@test-org.sorcha.io",
            DisplayName = "Member User",
            PasswordHash = BCrypt.Net.BCrypt.HashPassword("TestPassword123!"),
            Status = IdentityStatus.Active,
            Roles = new[] { UserRole.Member },
            OrganizationId = TestOrganizationId,
            CreatedAt = DateTimeOffset.UtcNow
        };

        // Create auditor user
        var auditorUser = new UserIdentity
        {
            Id = AuditorUserId,
            Email = "auditor@test-org.sorcha.io",
            DisplayName = "Auditor User",
            PasswordHash = BCrypt.Net.BCrypt.HashPassword("TestPassword123!"),
            Status = IdentityStatus.Active,
            Roles = new[] { UserRole.Auditor },
            OrganizationId = TestOrganizationId,
            CreatedAt = DateTimeOffset.UtcNow
        };

        context.UserIdentities.AddRange(adminUser, memberUser, auditorUser);

        // Create test service principal with Argon2id-hashed secret
        // ServiceAuthService.VerifyClientSecret expects salt(16) + hash(32) = 48 bytes
        var servicePrincipal = new ServicePrincipal
        {
            Id = Guid.NewGuid(),
            ServiceName = "test-service",
            ClientId = "test-client-id",
            ClientSecretEncrypted = CreateArgon2idHash("test-client-secret"),
            Status = ServicePrincipalStatus.Active,
            Scopes = new[] { "blueprints:read", "wallets:write", "tenant:delegate" },
            CreatedAt = DateTimeOffset.UtcNow
        };

        context.ServicePrincipals.Add(servicePrincipal);

        await context.SaveChangesAsync();
    }

    /// <summary>
    /// Creates an Argon2id hash matching the format used by ServiceAuthService.EncryptClientSecret.
    /// Returns salt(16) + hash(32) = 48 bytes.
    /// </summary>
    private static byte[] CreateArgon2idHash(string secret)
    {
        var salt = new byte[16];
        RandomNumberGenerator.Fill(salt);

        var parameters = new Argon2Parameters.Builder(Argon2Parameters.Argon2id)
            .WithSalt(salt)
            .WithMemoryAsKB(65536)  // 64MB - matches ServiceAuthService
            .WithIterations(3)
            .WithParallelism(4)
            .Build();

        var generator = new Argon2BytesGenerator();
        generator.Init(parameters);

        var hash = new byte[32];
        generator.GenerateBytes(Encoding.UTF8.GetBytes(secret), hash);

        // Store as: salt(16) + hash(32) = 48 bytes
        var result = new byte[48];
        Buffer.BlockCopy(salt, 0, result, 0, 16);
        Buffer.BlockCopy(hash, 0, result, 16, 32);
        return result;
    }
}
