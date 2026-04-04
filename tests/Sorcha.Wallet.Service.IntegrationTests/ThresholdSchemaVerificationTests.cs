// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

using System.Linq;
using Microsoft.EntityFrameworkCore;
using Sorcha.Wallet.Core.Domain.Entities;
using Sorcha.Wallet.Core.Domain.Enums;

namespace Sorcha.Wallet.Service.IntegrationTests;

/// <summary>
/// Verifies that the threshold signing database schema (ThresholdKeyGroup,
/// SigningKeyShare, SigningSession) is correctly configured and that these
/// entities remain schema-only with no service/endpoint references.
/// </summary>
public class ThresholdSchemaVerificationTests : IDisposable
{
    private readonly ThresholdTestDbContext _context;

    public ThresholdSchemaVerificationTests()
    {
        var options = new DbContextOptionsBuilder<ThresholdTestDbContext>()
            .UseInMemoryDatabase(databaseName: $"ThresholdSchemaTests_{Guid.NewGuid()}")
            .Options;

        _context = new ThresholdTestDbContext(options);
        _context.Database.EnsureCreated();
    }

    [Fact]
    public async Task ThresholdKeyGroup_Table_HasCorrectSchema()
    {
        // Arrange
        var group = new ThresholdKeyGroup
        {
            Id = Guid.NewGuid(),
            GroupPublicKey = "base64-encoded-group-public-key",
            Threshold = 2,
            TotalShares = 3,
            Algorithm = "FROST-ED25519",
            OrganizationId = Guid.NewGuid().ToString(),
            Status = ThresholdKeyGroupStatus.Pending,
            DkgSessionId = "dkg-session-001",
            CreatedAt = DateTime.UtcNow
        };

        // Act
        _context.ThresholdKeyGroups.Add(group);
        await _context.SaveChangesAsync();

        // Assert - round-trip all fields
        var saved = await _context.ThresholdKeyGroups
            .FirstOrDefaultAsync(g => g.Id == group.Id);

        saved.Should().NotBeNull();
        saved!.GroupPublicKey.Should().Be("base64-encoded-group-public-key");
        saved.Threshold.Should().Be(2);
        saved.TotalShares.Should().Be(3);
        saved.Algorithm.Should().Be("FROST-ED25519");
        saved.OrganizationId.Should().NotBeNullOrEmpty();
        saved.Status.Should().Be(ThresholdKeyGroupStatus.Pending);
        saved.DkgSessionId.Should().Be("dkg-session-001");
        saved.CreatedAt.Should().BeCloseTo(DateTime.UtcNow, TimeSpan.FromSeconds(5));
    }

    [Fact]
    public async Task SigningKeyShare_Table_HasCorrectSchema()
    {
        // Arrange - parent group required for FK
        var group = new ThresholdKeyGroup
        {
            Id = Guid.NewGuid(),
            GroupPublicKey = "group-pub-key",
            Threshold = 2,
            TotalShares = 3,
            Algorithm = "FROST-ED25519",
            OrganizationId = Guid.NewGuid().ToString()
        };

        _context.ThresholdKeyGroups.Add(group);
        await _context.SaveChangesAsync();

        var share = new SigningKeyShare
        {
            Id = Guid.NewGuid(),
            ThresholdKeyGroupId = group.Id,
            ParticipantId = "participant-alice",
            ShareIndex = 1,
            EncryptedShareData = new byte[] { 0x01, 0x02, 0x03, 0x04 },
            ProtectionKeyId = "kms-key-001",
            Status = ThresholdKeyGroupStatus.Active,
            CreatedAt = DateTime.UtcNow
        };

        // Act
        _context.SigningKeyShares.Add(share);
        await _context.SaveChangesAsync();

        // Assert - round-trip all fields including FK navigation
        var saved = await _context.SigningKeyShares
            .Include(s => s.ThresholdKeyGroup)
            .FirstOrDefaultAsync(s => s.Id == share.Id);

        saved.Should().NotBeNull();
        saved!.ThresholdKeyGroupId.Should().Be(group.Id);
        saved.ParticipantId.Should().Be("participant-alice");
        saved.ShareIndex.Should().Be(1);
        saved.EncryptedShareData.Should().BeEquivalentTo(new byte[] { 0x01, 0x02, 0x03, 0x04 });
        saved.ProtectionKeyId.Should().Be("kms-key-001");
        saved.Status.Should().Be(ThresholdKeyGroupStatus.Active);
        saved.ThresholdKeyGroup.Should().NotBeNull();
        saved.ThresholdKeyGroup.Id.Should().Be(group.Id);
    }

    [Fact]
    public async Task SigningSession_Table_HasCorrectSchema()
    {
        // Arrange - parent group required for FK
        var group = new ThresholdKeyGroup
        {
            Id = Guid.NewGuid(),
            GroupPublicKey = "group-pub-key",
            Threshold = 3,
            TotalShares = 5,
            Algorithm = "FROST-ED25519",
            OrganizationId = Guid.NewGuid().ToString()
        };

        _context.ThresholdKeyGroups.Add(group);
        await _context.SaveChangesAsync();

        var expiresAt = DateTime.UtcNow.AddMinutes(10);
        var session = new SigningSession
        {
            Id = Guid.NewGuid(),
            ThresholdKeyGroupId = group.Id,
            TransactionId = "tx-abc-123",
            State = SigningSessionState.Initializing,
            RequiredSigners = 3,
            CollectedPartials = 0,
            ExpiresAt = expiresAt,
            CreatedAt = DateTime.UtcNow,
            CompletedAt = null
        };

        // Act
        _context.SigningSessions.Add(session);
        await _context.SaveChangesAsync();

        // Assert - round-trip all fields including FK navigation
        var saved = await _context.SigningSessions
            .Include(s => s.ThresholdKeyGroup)
            .FirstOrDefaultAsync(s => s.Id == session.Id);

        saved.Should().NotBeNull();
        saved!.ThresholdKeyGroupId.Should().Be(group.Id);
        saved.State.Should().Be(SigningSessionState.Initializing);
        saved.RequiredSigners.Should().Be(3);
        saved.CollectedPartials.Should().Be(0);
        saved.ExpiresAt.Should().BeCloseTo(expiresAt, TimeSpan.FromSeconds(1));
        saved.CompletedAt.Should().BeNull();
        saved.TransactionId.Should().Be("tx-abc-123");
        saved.ThresholdKeyGroup.Should().NotBeNull();
        saved.ThresholdKeyGroup.TotalShares.Should().Be(5);
    }

    [Fact]
    public void ThresholdTables_NoServiceCodeReferences()
    {
        // Verify that threshold entity types are not referenced by any service or
        // endpoint classes in the Wallet Service assembly. These entities are
        // schema-only - no service code should depend on them yet.
        var walletServiceAssembly = typeof(Sorcha.Wallet.Service.Extensions.WalletServiceExtensions).Assembly;

        var thresholdTypes = new[]
        {
            typeof(ThresholdKeyGroup),
            typeof(SigningKeyShare),
            typeof(SigningSession)
        };

        // Get all non-abstract types that live in Services/ or Endpoints/ namespaces
        var serviceAndEndpointTypes = Enumerable.Where(
            walletServiceAssembly.GetTypes(),
            t => t.Namespace is not null
                && !t.IsAbstract
                && (t.Namespace.Contains(".Services", StringComparison.Ordinal)
                    || t.Namespace.Contains(".Endpoints", StringComparison.Ordinal)))
            .ToList();

        serviceAndEndpointTypes.Should().NotBeEmpty(
            "the Wallet Service assembly should have service/endpoint types to verify against");

        foreach (var serviceType in serviceAndEndpointTypes)
        {
            // Check constructor parameters for threshold type references
            var constructors = serviceType.GetConstructors();
            foreach (var ctor in constructors)
            {
                foreach (var param in ctor.GetParameters())
                {
                    foreach (var thresholdType in thresholdTypes)
                    {
                        param.ParameterType.FullName.Should().NotContain(
                            thresholdType.Name,
                            $"service/endpoint type '{serviceType.Name}' should not reference " +
                            $"schema-only entity '{thresholdType.Name}' in constructor parameters");
                    }
                }
            }

            // Check public method return types and parameters
            var methods = serviceType.GetMethods(
                System.Reflection.BindingFlags.Public
                | System.Reflection.BindingFlags.Instance
                | System.Reflection.BindingFlags.DeclaredOnly);

            foreach (var method in methods)
            {
                foreach (var thresholdType in thresholdTypes)
                {
                    method.ReturnType.FullName?.Should().NotContain(
                        thresholdType.Name,
                        $"method '{serviceType.Name}.{method.Name}' should not return " +
                        $"schema-only entity '{thresholdType.Name}'");

                    foreach (var param in method.GetParameters())
                    {
                        param.ParameterType.FullName.Should().NotContain(
                            thresholdType.Name,
                            $"method '{serviceType.Name}.{method.Name}' should not accept " +
                            $"schema-only entity '{thresholdType.Name}' as parameter");
                    }
                }
            }
        }
    }

    public void Dispose()
    {
        _context.Dispose();
    }
}

/// <summary>
/// Standalone test DbContext for threshold signing schema verification.
/// Does NOT extend WalletDbContext to avoid InMemory provider issues with
/// PostgreSQL-specific column types (jsonb, gen_random_uuid). Configures only
/// the threshold entities needed for schema round-trip testing.
/// </summary>
internal class ThresholdTestDbContext : DbContext
{
    public DbSet<ThresholdKeyGroup> ThresholdKeyGroups => Set<ThresholdKeyGroup>();
    public DbSet<SigningKeyShare> SigningKeyShares => Set<SigningKeyShare>();
    public DbSet<SigningSession> SigningSessions => Set<SigningSession>();

    public ThresholdTestDbContext(DbContextOptions<ThresholdTestDbContext> options)
        : base(options)
    {
    }

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);

        modelBuilder.Entity<ThresholdKeyGroup>(entity =>
        {
            entity.HasKey(e => e.Id);
            entity.Property(e => e.Status).HasConversion<string>();
        });

        modelBuilder.Entity<SigningKeyShare>(entity =>
        {
            entity.HasKey(e => e.Id);
            entity.Property(e => e.Status).HasConversion<string>();

            entity.HasOne(e => e.ThresholdKeyGroup)
                .WithMany(g => g.Shares)
                .HasForeignKey(e => e.ThresholdKeyGroupId);
        });

        modelBuilder.Entity<SigningSession>(entity =>
        {
            entity.HasKey(e => e.Id);
            entity.Property(e => e.State).HasConversion<string>();

            entity.HasOne(e => e.ThresholdKeyGroup)
                .WithMany(g => g.Sessions)
                .HasForeignKey(e => e.ThresholdKeyGroupId);
        });
    }
}
