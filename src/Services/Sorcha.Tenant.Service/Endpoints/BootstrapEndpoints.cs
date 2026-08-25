// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Sorcha.ServiceClients.Wallet;
using Sorcha.Tenant.Service.Data;
using Sorcha.Tenant.Service.Models;
using Sorcha.Tenant.Service.Models.Dtos;
using Sorcha.Tenant.Service.Services;
using WellKnownIds = Sorcha.Tenant.Service.Services.WellKnownIds;

namespace Sorcha.Tenant.Service.Endpoints;

/// <summary>
/// Bootstrap endpoints for initial Sorcha platform setup.
/// Provides atomic creation of organization, admin user, and optional service principal.
/// </summary>
public static class BootstrapEndpoints
{
    /// <summary>
    /// Maps bootstrap endpoints to the application.
    /// </summary>
    /// <param name="app">The endpoint route builder.</param>
    /// <returns>The endpoint route builder for chaining.</returns>
    public static IEndpointRouteBuilder MapBootstrapEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/tenants")
            .WithTags("Bootstrap")
            .AllowAnonymous(); // Bootstrap is public - no existing auth needed

        group.MapPost("/bootstrap", BootstrapPlatform)
            .WithName("BootstrapPlatform")
            .WithSummary("Bootstrap fresh Sorcha installation")
            .WithDescription("""
                Atomically creates initial organization, administrator user, and optional service principal.
                This endpoint is designed for first-time platform setup.

                **Security Notes:**
                - This endpoint should be protected in production (e.g., firewall rules, one-time tokens)
                - Consider disabling after initial setup via configuration
                - Password must meet complexity requirements

                **What gets created:**
                1. New organization with specified name and subdomain
                2. Administrator user with full permissions
                3. JWT access and refresh tokens for immediate use
                4. Optional service principal for automation

                **Response includes:**
                - Organization and user IDs
                - Admin JWT tokens (access + refresh)
                - Service principal credentials (if requested)
                """)
            .Produces<BootstrapResponse>(StatusCodes.Status201Created)
            .ProducesValidationProblem(StatusCodes.Status400BadRequest)
            .Produces<ProblemDetails>(StatusCodes.Status409Conflict)
            .Produces<ProblemDetails>(StatusCodes.Status500InternalServerError);

        return app;
    }

    /// <summary>
    /// Well-known System Register ID (SHA-256("sorcha-system-register")[0:32]).
    /// Duplicated from Sorcha.Register.Models.Constants.SystemRegisterConstants to avoid cross-domain dependency.
    /// </summary>
    private const string SystemRegisterId = "aebf26362e079087571ac0932d4db973";

    /// <summary>
    /// Well-known display name for the System Register.
    /// </summary>
    private const string SystemRegisterName = "Sorcha System Register";

    /// <summary>
    /// Bootstraps a fresh Sorcha installation with initial organization and admin user.
    /// </summary>
    private static async Task<Results<Created<BootstrapResponse>, ValidationProblem, Conflict<ProblemDetails>, ProblemHttpResult>> BootstrapPlatform(
        [FromBody] BootstrapRequest request,
        HttpContext httpContext,
        IConfiguration configuration,
        IOrganizationService organizationService,
        IServiceAuthService serviceAuthService,
        ITokenService tokenService,
        Data.Repositories.IIdentityRepository identityRepository,
        TenantDbContext dbContext,
        IWalletServiceClient walletClient,
        IRegisterSubscriptionService subscriptionService,
        ILogger<Program> logger)
    {
        // Bootstrap secret guard: if BOOTSTRAP_SECRET is configured, require matching X-Bootstrap-Secret header
        var bootstrapSecret = configuration["BOOTSTRAP_SECRET"];
        if (!string.IsNullOrEmpty(bootstrapSecret))
        {
            var providedSecret = httpContext.Request.Headers["X-Bootstrap-Secret"].FirstOrDefault();
            if (!string.Equals(providedSecret, bootstrapSecret, StringComparison.Ordinal))
            {
                logger.LogWarning("Bootstrap attempted without valid bootstrap secret");
                return TypedResults.Problem(
                    title: "Unauthorized",
                    detail: "A valid X-Bootstrap-Secret header is required when BOOTSTRAP_SECRET is configured.",
                    statusCode: StatusCodes.Status403Forbidden);
            }
        }

        // One-shot guard: prevent re-bootstrap
        var alreadyBootstrapped = await dbContext.SystemConfigurations
            .AnyAsync(c => c.Key == "BootstrapCompleted");
        if (alreadyBootstrapped)
        {
            logger.LogWarning("Bootstrap attempted but platform has already been bootstrapped");
            return TypedResults.Conflict(new ProblemDetails
            {
                Title = "Already Bootstrapped",
                Detail = "Platform has already been bootstrapped",
                Status = StatusCodes.Status409Conflict
            });
        }

        logger.LogInformation("Bootstrap request received for organization: {OrgName}", request.OrganizationName);

        // Validate request
        var validationErrors = ValidateBootstrapRequest(request);
        if (validationErrors.Count > 0)
        {
            logger.LogWarning("Bootstrap validation failed: {Errors}", string.Join(", ", validationErrors.Values));
            return TypedResults.ValidationProblem(validationErrors);
        }

        try
        {
            // Step 1: Create organization
            // Note: For bootstrap, we use a system user ID (all zeros) as the creator
            logger.LogInformation("Creating organization: {Name} ({Subdomain})",
                request.OrganizationName, request.OrganizationSubdomain);

            var createOrgRequest = new CreateOrganizationRequest
            {
                Name = request.OrganizationName,
                Subdomain = request.OrganizationSubdomain,
                Branding = request.OrganizationDescription != null ? new BrandingConfigurationDto
                {
                    CompanyTagline = request.OrganizationDescription,
                    PrimaryColor = "#0066CC",
                    SecondaryColor = "#FFFFFF"
                } : null
            };

            var bootstrapUserId = Guid.Empty; // System user for bootstrap
            var organization = await organizationService.CreateOrganizationAsync(createOrgRequest, bootstrapUserId);
            logger.LogInformation("Organization created: {OrgId}", organization.Id);

            // Step 1b: Provision organization wallet for signing operations
            try
            {
                var walletName = $"org-{request.OrganizationSubdomain.ToLowerInvariant()}-signing";
                var walletInfo = await walletClient.CreateWalletAsync(
                    walletName,
                    "ED25519",
                    organization.Id.ToString(),
                    organization.Id.ToString());

                var orgEntity = await dbContext.Organizations.FindAsync(organization.Id);
                if (orgEntity != null)
                {
                    orgEntity.WalletAddress = walletInfo.Address;
                    orgEntity.PublicKey = walletInfo.PublicKey;
                    orgEntity.SigningAlgorithm = walletInfo.Algorithm;
                    await dbContext.SaveChangesAsync();
                }

                logger.LogInformation(
                    "Organization wallet provisioned: {OrgId} -> {WalletAddress}",
                    organization.Id, walletInfo.Address);
            }
            catch (Exception walletEx)
            {
                logger.LogWarning(walletEx,
                    "Failed to provision wallet for organization {OrgId} during bootstrap. " +
                    "The org admin must create it via POST /api/organizations/{id}/wallet (#1525).",
                    organization.Id);
            }

            // Step 2: Create admin PlatformUser + UserIdentity
            logger.LogInformation("Creating administrator: {Email}", request.AdminEmail);

            var passwordHash = BCrypt.Net.BCrypt.HashPassword(request.AdminPassword);

            // Create platform-wide identity
            var platformUser = new PlatformUser
            {
                Email = request.AdminEmail,
                DisplayName = request.AdminName,
                PasswordHash = passwordHash,
                EmailVerified = true,
                EmailVerifiedAt = DateTimeOffset.UtcNow,
                Status = PlatformUserStatus.Active,
                CreatedAt = DateTimeOffset.UtcNow
            };
            dbContext.PlatformUsers.Add(platformUser);
            await dbContext.SaveChangesAsync();

            // Create per-org identity in the bootstrapped org
            var adminUser = new UserIdentity
            {
                OrganizationId = organization.Id,
                Email = request.AdminEmail,
                DisplayName = request.AdminName,
                PlatformUserId = platformUser.Id,
                Roles = new[] { UserRole.Administrator },
                Status = IdentityStatus.Active,
                CreatedAt = DateTimeOffset.UtcNow
            };

            var createdUser = await identityRepository.CreateUserAsync(adminUser);

            // Create org membership
            dbContext.PlatformUserOrgMemberships.Add(new PlatformUserOrgMembership
            {
                PlatformUserId = platformUser.Id,
                OrganizationId = organization.Id,
                Role = UserRole.Administrator.ToString(),
                JoinedAt = DateTimeOffset.UtcNow
            });
            await dbContext.SaveChangesAsync();

            logger.LogInformation("Administrator created: PlatformUser={PlatformUserId}, UserIdentity={UserId}",
                platformUser.Id, createdUser.Id);

            // Step 3: Generate JWT tokens for immediate use
            string adminAccessToken;
            string adminRefreshToken;
            try
            {
                logger.LogInformation("Generating admin tokens for user {UserId}", createdUser.Id);
                var orgEntity = new Organization
                {
                    Id = organization.Id,
                    Name = organization.Name,
                    Subdomain = organization.Subdomain
                };
                var tokenResponse = await tokenService.GenerateUserTokenAsync(createdUser, orgEntity, platformUser.Id, emailVerified: platformUser.EmailVerified);
                adminAccessToken = tokenResponse.AccessToken;
                adminRefreshToken = tokenResponse.RefreshToken;
            }
            catch (Exception tokenEx)
            {
                logger.LogWarning(tokenEx, "Token generation failed during bootstrap — use /api/auth/login instead");
                adminAccessToken = string.Empty;
                adminRefreshToken = string.Empty;
            }

            // Step 4: Optionally create service principal
            Guid? servicePrincipalId = null;
            string? servicePrincipalClientId = null;
            string? servicePrincipalSecret = null;

            if (request.CreateServicePrincipal)
            {
                var spName = request.ServicePrincipalName ?? "bootstrap-principal";
                logger.LogInformation("Creating service principal: {Name}", spName);

                var scopes = new[]
                {
                    "admin",
                    "platform:manage",
                    "organizations:manage",
                    "users:manage"
                };

                var servicePrincipal = await serviceAuthService.RegisterServicePrincipalAsync(spName, scopes);
                servicePrincipalId = servicePrincipal.Id;
                servicePrincipalClientId = servicePrincipal.ClientId;
                servicePrincipalSecret = servicePrincipal.ClientSecret;

                logger.LogInformation("Service principal created: {SpId}", servicePrincipalId);
            }

            // Build response
            var response = new BootstrapResponse
            {
                OrganizationId = organization.Id,
                OrganizationName = organization.Name,
                OrganizationSubdomain = organization.Subdomain,
                AdminUserId = createdUser.Id,
                AdminEmail = createdUser.Email,
                AdminAccessToken = adminAccessToken,
                AdminRefreshToken = adminRefreshToken,
                ServicePrincipalId = servicePrincipalId,
                ServicePrincipalClientId = servicePrincipalClientId,
                ServicePrincipalClientSecret = servicePrincipalSecret,
                SystemAdminOrgId = WellKnownIds.SystemAdminOrgId,
                PublicOrgId = WellKnownIds.PublicOrgId,
                PlatformUserId = platformUser.Id,
                CreatedAt = DateTime.UtcNow
            };

            // Mark bootstrap as completed (one-shot guard)
            dbContext.SystemConfigurations.Add(new SystemConfiguration
            {
                Key = "BootstrapCompleted",
                Value = DateTimeOffset.UtcNow.ToString("O"),
                CreatedAt = DateTimeOffset.UtcNow
            });

            try
            {
                await dbContext.SaveChangesAsync();
            }
            catch (DbUpdateException)
            {
                // Race condition: another concurrent request already wrote BootstrapCompleted
                logger.LogWarning("Bootstrap race condition detected — another request completed bootstrap first");
                return TypedResults.Conflict(new ProblemDetails
                {
                    Title = "Already Bootstrapped",
                    Detail = "Platform has already been bootstrapped by a concurrent request.",
                    Status = StatusCodes.Status409Conflict
                });
            }

            // Step 5: Auto-subscribe organization to the System Register (Owner subscription)
            try
            {
                await subscriptionService.CreateOwnerSubscriptionAsync(
                    organization.Id, SystemRegisterId, SystemRegisterName, bootstrapUserId, CancellationToken.None);

                logger.LogInformation(
                    "Auto-subscribed organization {OrgId} to System Register {RegisterId}",
                    organization.Id, SystemRegisterId);
            }
            catch (Exception subEx)
            {
                logger.LogWarning(subEx,
                    "Failed to auto-subscribe organization {OrgId} to System Register during bootstrap. " +
                    "Subscription can be created manually.",
                    organization.Id);
            }

            logger.LogInformation("Bootstrap completed successfully for organization: {OrgId}", organization.Id);

            return TypedResults.Created($"/api/organizations/{organization.Id}", response);
        }
        catch (ArgumentException ex) when (ex.Message.Contains("subdomain") || ex.Message.Contains("already taken"))
        {
            // Subdomain already exists
            logger.LogWarning("Bootstrap conflict: {Message}", ex.Message);
            return TypedResults.Conflict(new ProblemDetails
            {
                Title = "Bootstrap Conflict",
                Detail = ex.Message,
                Status = StatusCodes.Status409Conflict
            });
        }
        catch (InvalidOperationException ex) when (ex.Message.Contains("already exists"))
        {
            // User already exists
            logger.LogWarning("Bootstrap conflict: {Message}", ex.Message);
            return TypedResults.Conflict(new ProblemDetails
            {
                Title = "Bootstrap Conflict",
                Detail = ex.Message,
                Status = StatusCodes.Status409Conflict
            });
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Bootstrap failed: {Message}", ex.Message);
            return TypedResults.Problem(
                title: "Bootstrap Failed",
                detail: ex.Message,
                statusCode: StatusCodes.Status500InternalServerError);
        }
    }

    /// <summary>
    /// Validates the bootstrap request.
    /// </summary>
    private static Dictionary<string, string[]> ValidateBootstrapRequest(BootstrapRequest request)
    {
        var errors = new Dictionary<string, string[]>();

        // Organization name
        if (string.IsNullOrWhiteSpace(request.OrganizationName))
        {
            errors["organizationName"] = new[] { "Organization name is required" };
        }
        else if (request.OrganizationName.Length < 3 || request.OrganizationName.Length > 100)
        {
            errors["organizationName"] = new[] { "Organization name must be between 3 and 100 characters" };
        }

        // Organization subdomain
        if (string.IsNullOrWhiteSpace(request.OrganizationSubdomain))
        {
            errors["organizationSubdomain"] = new[] { "Organization subdomain is required" };
        }
        else if (request.OrganizationSubdomain.Length < 3 || request.OrganizationSubdomain.Length > 50)
        {
            errors["organizationSubdomain"] = new[] { "Organization subdomain must be between 3 and 50 characters" };
        }
        else if (!System.Text.RegularExpressions.Regex.IsMatch(request.OrganizationSubdomain, @"^[a-z0-9-]+$"))
        {
            errors["organizationSubdomain"] = new[] { "Organization subdomain can only contain lowercase letters, numbers, and hyphens" };
        }

        // Admin email
        if (string.IsNullOrWhiteSpace(request.AdminEmail))
        {
            errors["adminEmail"] = new[] { "Administrator email is required" };
        }
        else if (!System.Text.RegularExpressions.Regex.IsMatch(request.AdminEmail, @"^[^@\s]+@[^@\s]+\.[^@\s]+$"))
        {
            errors["adminEmail"] = new[] { "Administrator email is not valid" };
        }

        // Admin name
        if (string.IsNullOrWhiteSpace(request.AdminName))
        {
            errors["adminName"] = new[] { "Administrator name is required" };
        }

        // Admin password
        if (string.IsNullOrWhiteSpace(request.AdminPassword))
        {
            errors["adminPassword"] = new[] { "Administrator password is required" };
        }
        else if (request.AdminPassword.Length < 8)
        {
            errors["adminPassword"] = new[] { "Administrator password must be at least 8 characters" };
        }
        else if (!System.Text.RegularExpressions.Regex.IsMatch(request.AdminPassword, @"^(?=.*[a-z])(?=.*[A-Z])(?=.*\d)(?=.*[@$!%*?&])[A-Za-z\d@$!%*?&]"))
        {
            errors["adminPassword"] = new[] { "Administrator password must contain at least one uppercase letter, one lowercase letter, one number, and one special character" };
        }

        // Service principal name (if creating)
        if (request.CreateServicePrincipal && !string.IsNullOrWhiteSpace(request.ServicePrincipalName))
        {
            if (request.ServicePrincipalName.Length < 3 || request.ServicePrincipalName.Length > 50)
            {
                errors["servicePrincipalName"] = new[] { "Service principal name must be between 3 and 50 characters" };
            }
        }

        return errors;
    }
}
