// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

using Microsoft.Extensions.Logging;

using Sorcha.Tenant.Service.Data;
using Sorcha.Tenant.Service.Data.Repositories;
using Sorcha.Tenant.Service.Models;
using Sorcha.Tenant.Service.Models.Dtos;

namespace Sorcha.Tenant.Service.Services;

/// <summary>
/// Manages custom domain configuration and DNS CNAME verification for organizations.
/// </summary>
public class CustomDomainService : ICustomDomainService
{
    private readonly ICustomDomainRepository _domainRepository;
    private readonly IOrganizationRepository _organizationRepository;
    private readonly TenantDbContext _dbContext;
    private readonly IDnsResolver _dnsResolver;
    private readonly ILogger<CustomDomainService> _logger;

    /// <summary>
    /// The base domain for CNAME targets (e.g., "sorcha.io").
    /// </summary>
    private const string BaseDomain = "sorcha.io";

    public CustomDomainService(
        ICustomDomainRepository domainRepository,
        IOrganizationRepository organizationRepository,
        TenantDbContext dbContext,
        IDnsResolver dnsResolver,
        ILogger<CustomDomainService> logger)
    {
        _domainRepository = domainRepository;
        _organizationRepository = organizationRepository;
        _dbContext = dbContext;
        _dnsResolver = dnsResolver;
        _logger = logger;
    }

    public async Task<CustomDomainResponse> GetCustomDomainAsync(Guid organizationId, CancellationToken cancellationToken = default)
    {
        var mapping = await _domainRepository.GetByOrganizationIdAsync(organizationId, cancellationToken);
        var org = await _organizationRepository.GetByIdAsync(organizationId, cancellationToken);

        if (mapping is null)
        {
            return new CustomDomainResponse
            {
                Status = "None",
                CnameTarget = org is not null ? $"{org.Subdomain}.{BaseDomain}" : null
            };
        }

        return new CustomDomainResponse
        {
            Domain = mapping.Domain,
            Status = mapping.Status.ToString(),
            VerifiedAt = mapping.VerifiedAt,
            CnameTarget = org is not null ? $"{org.Subdomain}.{BaseDomain}" : null
        };
    }

    public async Task<CnameInstructionsResponse> ConfigureCustomDomainAsync(
        Guid organizationId,
        ConfigureCustomDomainRequest request,
        Guid adminUserId,
        CancellationToken cancellationToken = default)
    {
        var org = await _organizationRepository.GetByIdAsync(organizationId, cancellationToken)
            ?? throw new InvalidOperationException("Organization not found.");

        var domain = request.Domain.ToLowerInvariant().Trim();

        // Check if domain is already used by another organization
        var existing = await _domainRepository.GetByDomainAsync(domain, cancellationToken);
        if (existing is not null && existing.OrganizationId != organizationId)
        {
            throw new InvalidOperationException($"Domain '{domain}' is already configured for another organization.");
        }

        var cnameTarget = $"{org.Subdomain}.{BaseDomain}";

        // Update or create mapping
        var mapping = await _domainRepository.GetByOrganizationIdAsync(organizationId, cancellationToken);
        if (mapping is not null)
        {
            mapping.Domain = domain;
            mapping.Status = CustomDomainStatus.Pending;
            mapping.VerifiedAt = null;
            mapping.LastCheckedAt = null;
            await _domainRepository.UpdateAsync(mapping, cancellationToken);
        }
        else
        {
            mapping = new CustomDomainMapping
            {
                OrganizationId = organizationId,
                Domain = domain,
                Status = CustomDomainStatus.Pending
            };
            await _domainRepository.CreateAsync(mapping, cancellationToken);
        }

        // Update the Organization model
        org.CustomDomain = domain;
        org.CustomDomainStatus = CustomDomainStatus.Pending;
        await _dbContext.SaveChangesAsync(cancellationToken);

        // Write audit event
        _dbContext.AuditLogEntries.Add(new AuditLogEntry
        {
            EventType = AuditEventType.CustomDomainConfigured,
            IdentityId = adminUserId,
            OrganizationId = organizationId,
            Success = true,
            Details = new Dictionary<string, object>
            {
                ["domain"] = domain,
                ["cnameTarget"] = cnameTarget
            }
        });
        await _dbContext.SaveChangesAsync(cancellationToken);

        _logger.LogInformation("Custom domain {Domain} configured for organization {OrgId}", domain, organizationId);

        return new CnameInstructionsResponse
        {
            Domain = domain,
            CnameTarget = cnameTarget,
            Instructions = $"Create a CNAME record pointing '{domain}' to '{cnameTarget}'. Then verify the configuration."
        };
    }

    public async Task<DomainVerificationResponse> VerifyCustomDomainAsync(
        Guid organizationId,
        Guid adminUserId,
        CancellationToken cancellationToken = default)
    {
        var mapping = await _domainRepository.GetByOrganizationIdAsync(organizationId, cancellationToken)
            ?? throw new InvalidOperationException("No custom domain configured.");

        var org = await _organizationRepository.GetByIdAsync(organizationId, cancellationToken)
            ?? throw new InvalidOperationException("Organization not found.");

        var cnameTarget = $"{org.Subdomain}.{BaseDomain}";

        var verified = await _dnsResolver.VerifyCnameAsync(mapping.Domain, cnameTarget, cancellationToken);

        mapping.LastCheckedAt = DateTimeOffset.UtcNow;

        if (verified)
        {
            mapping.Status = CustomDomainStatus.Verified;
            mapping.VerifiedAt = DateTimeOffset.UtcNow;
            org.CustomDomainStatus = CustomDomainStatus.Verified;

            _dbContext.AuditLogEntries.Add(new AuditLogEntry
            {
                EventType = AuditEventType.CustomDomainVerified,
                IdentityId = adminUserId,
                OrganizationId = organizationId,
                Success = true,
                Details = new Dictionary<string, object>
                {
                    ["domain"] = mapping.Domain,
                    ["cnameTarget"] = cnameTarget
                }
            });

            _logger.LogInformation("Custom domain {Domain} verified for organization {OrgId}", mapping.Domain, organizationId);
        }
        else
        {
            mapping.Status = CustomDomainStatus.Failed;
            org.CustomDomainStatus = CustomDomainStatus.Failed;

            _dbContext.AuditLogEntries.Add(new AuditLogEntry
            {
                EventType = AuditEventType.CustomDomainFailed,
                IdentityId = adminUserId,
                OrganizationId = organizationId,
                Success = false,
                Details = new Dictionary<string, object>
                {
                    ["domain"] = mapping.Domain,
                    ["expectedCname"] = cnameTarget,
                    ["reason"] = "CNAME record not found or does not match expected target."
                }
            });

            _logger.LogWarning("Custom domain {Domain} verification failed for organization {OrgId}", mapping.Domain, organizationId);
        }

        await _domainRepository.UpdateAsync(mapping, cancellationToken);
        await _dbContext.SaveChangesAsync(cancellationToken);

        return new DomainVerificationResponse
        {
            Verified = verified,
            Message = verified
                ? $"Domain '{mapping.Domain}' verified successfully. CNAME points to '{cnameTarget}'."
                : $"Verification failed. Ensure a CNAME record for '{mapping.Domain}' points to '{cnameTarget}'."
        };
    }

    public async Task RemoveCustomDomainAsync(
        Guid organizationId,
        Guid adminUserId,
        CancellationToken cancellationToken = default)
    {
        var mapping = await _domainRepository.GetByOrganizationIdAsync(organizationId, cancellationToken);
        if (mapping is null) return;

        var org = await _organizationRepository.GetByIdAsync(organizationId, cancellationToken);
        if (org is not null)
        {
            org.CustomDomain = null;
            org.CustomDomainStatus = CustomDomainStatus.None;
        }

        await _domainRepository.DeleteAsync(mapping, cancellationToken);
        await _dbContext.SaveChangesAsync(cancellationToken);

        _logger.LogInformation("Custom domain {Domain} removed for organization {OrgId}", mapping.Domain, organizationId);
    }
}
