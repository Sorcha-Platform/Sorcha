// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

using Sorcha.Tenant.Service.Data;
using Sorcha.Tenant.Service.Data.Repositories;
using Sorcha.Tenant.Service.Models;

namespace Sorcha.Tenant.Service.Services;

/// <summary>
/// Background service that periodically verifies CNAME records for all verified custom domains.
/// Runs daily and marks domains as Failed if their CNAME record is no longer valid.
/// </summary>
public class CustomDomainVerificationService : BackgroundService
{
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ILogger<CustomDomainVerificationService> _logger;
    private readonly TimeSpan _checkInterval = TimeSpan.FromHours(24);

    public CustomDomainVerificationService(
        IServiceScopeFactory scopeFactory,
        ILogger<CustomDomainVerificationService> logger)
    {
        _scopeFactory = scopeFactory;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _logger.LogInformation("Custom domain verification service started. Check interval: {Interval}", _checkInterval);

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await Task.Delay(_checkInterval, stoppingToken);
                await VerifyAllDomainsAsync(stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                // Graceful shutdown
                break;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error during custom domain verification cycle");
            }
        }

        _logger.LogInformation("Custom domain verification service stopped");
    }

    internal async Task VerifyAllDomainsAsync(CancellationToken cancellationToken)
    {
        using var scope = _scopeFactory.CreateScope();
        var domainRepository = scope.ServiceProvider.GetRequiredService<ICustomDomainRepository>();
        var orgRepository = scope.ServiceProvider.GetRequiredService<IOrganizationRepository>();
        var dnsResolver = scope.ServiceProvider.GetRequiredService<IDnsResolver>();
        var dbContext = scope.ServiceProvider.GetRequiredService<TenantDbContext>();

        var verifiedDomains = await domainRepository.GetVerifiedDomainsAsync(cancellationToken);
        _logger.LogInformation("Checking {Count} verified custom domains", verifiedDomains.Count);

        foreach (var mapping in verifiedDomains)
        {
            try
            {
                var org = await orgRepository.GetByIdAsync(mapping.OrganizationId, cancellationToken);
                if (org is null)
                {
                    _logger.LogWarning("Organization {OrgId} not found for domain {Domain}", mapping.OrganizationId, mapping.Domain);
                    continue;
                }

                var cnameTarget = $"{org.Subdomain}.sorcha.io";
                var verified = await dnsResolver.VerifyCnameAsync(mapping.Domain, cnameTarget, cancellationToken);

                mapping.LastCheckedAt = DateTimeOffset.UtcNow;

                if (!verified)
                {
                    _logger.LogWarning(
                        "Custom domain {Domain} for org {OrgId} failed re-verification — CNAME no longer points to {Target}",
                        mapping.Domain, mapping.OrganizationId, cnameTarget);

                    mapping.Status = CustomDomainStatus.Failed;
                    org.CustomDomainStatus = CustomDomainStatus.Failed;

                    dbContext.AuditLogEntries.Add(new AuditLogEntry
                    {
                        EventType = AuditEventType.CustomDomainFailed,
                        OrganizationId = mapping.OrganizationId,
                        Success = false,
                        Details = new Dictionary<string, object>
                        {
                            ["domain"] = mapping.Domain,
                            ["expectedCname"] = cnameTarget,
                            ["reason"] = "Periodic re-verification failed. CNAME record no longer resolves to expected target."
                        }
                    });
                }

                await domainRepository.UpdateAsync(mapping, cancellationToken);
                await dbContext.SaveChangesAsync(cancellationToken);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error verifying domain {Domain}", mapping.Domain);
            }
        }
    }
}
