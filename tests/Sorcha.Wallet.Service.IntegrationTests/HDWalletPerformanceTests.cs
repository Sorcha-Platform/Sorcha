using System.Diagnostics;
using System.Linq;
using System.Net;
using System.Net.Http.Json;
using FluentAssertions;
using Sorcha.Wallet.Contracts.Models;
using Sorcha.Wallet.Service.IntegrationTests.Fixtures;
using Sorcha.Wallet.Service.Models;
using Xunit;

namespace Sorcha.Wallet.Service.IntegrationTests;

/// <summary>
/// Performance tests for HD wallet address management endpoints.
/// Measures throughput, latency, and scalability of the new HD wallet features.
/// Uses test authentication handler to bypass JWT validation.
/// </summary>
[Collection("WalletService")]
public class HDWalletPerformanceTests
{
    private readonly HttpClient _client;

    public HDWalletPerformanceTests(WalletServiceWebApplicationFactory factory)
    {
        _client = factory.CreateAuthenticatedClient();
    }

    [Fact]
    public async Task Performance_RegisterAddress_ShouldMeasureLatency()
    {
        // Arrange
        var wallet = await CreateTestWallet();
        var iterations = 100;
        var latencies = new List<double>();

        Console.WriteLine($"=== Address Registration Performance Test ===");
        Console.WriteLine($"Iterations: {iterations}");
        Console.WriteLine("");

        // Act - Measure individual address registration latency
        // Use multiple accounts to avoid BIP44 gap limit (max 20 per account)
        for (int i = 0; i < iterations; i++)
        {
            var account = i / 20; // Spread across accounts (max 20 per account)
            var index = i % 20;

            var request = new RegisterDerivedAddressRequest
            {
                DerivedPublicKey = Convert.ToBase64String(new byte[32]),
                DerivedAddress = $"ws1q{Guid.NewGuid().ToString("N")}",
                DerivationPath = $"m/44'/0'/{account}'/0/{index}",
                Label = $"Address {i}"
            };

            var sw = Stopwatch.StartNew();
            var response = await _client.PostAsJsonAsync(
                $"/api/v1/wallets/{wallet.Address}/addresses",
                request);
            sw.Stop();

            response.StatusCode.Should().Be(HttpStatusCode.Created);
            latencies.Add(sw.Elapsed.TotalMilliseconds);
        }

        // Calculate statistics
        var avgLatency = latencies.Average();
        var minLatency = latencies.Min();
        var maxLatency = latencies.Max();
        var p50 = CalculatePercentile(latencies, 50);
        var p95 = CalculatePercentile(latencies, 95);
        var p99 = CalculatePercentile(latencies, 99);
        var throughput = iterations / (latencies.Sum() / 1000.0); // ops/sec

        // Output results
        Console.WriteLine("=== Results ===");
        Console.WriteLine($"Total Time:        {latencies.Sum():F2} ms");
        Console.WriteLine($"Throughput:        {throughput:F2} ops/sec");
        Console.WriteLine("");
        Console.WriteLine("Latency Statistics:");
        Console.WriteLine($"  Min:             {minLatency:F2} ms");
        Console.WriteLine($"  Average:         {avgLatency:F2} ms");
        Console.WriteLine($"  Max:             {maxLatency:F2} ms");
        Console.WriteLine($"  P50 (median):    {p50:F2} ms");
        Console.WriteLine($"  P95:             {p95:F2} ms");
        Console.WriteLine($"  P99:             {p99:F2} ms");

        // Assert performance targets (reasonable for in-memory implementation)
        avgLatency.Should().BeLessThan(100, "average latency should be under 100ms");
        p95.Should().BeLessThan(150, "95th percentile should be under 150ms");
    }

    [Fact]
    public async Task Performance_ListAddresses_ShouldScaleWithAddressCount()
    {
        // Arrange
        var wallet = await CreateTestWallet();
        var addressCounts = new[] { 10, 50, 100, 200 };

        Console.WriteLine($"=== List Addresses Scalability Test ===");
        Console.WriteLine("");

        foreach (var count in addressCounts)
        {
            // Register addresses
            for (int i = 0; i < count; i++)
            {
                var request = new RegisterDerivedAddressRequest
                {
                    DerivedPublicKey = Convert.ToBase64String(new byte[32]),
                    DerivedAddress = $"ws1q{Guid.NewGuid().ToString("N")}",
                    DerivationPath = $"m/44'/0'/0'/0/{i}",
                    Label = $"Address {i}"
                };

                await _client.PostAsJsonAsync($"/api/v1/wallets/{wallet.Address}/addresses", request);
            }

            // Measure list operation
            var measurements = new List<double>();
            for (int i = 0; i < 10; i++)
            {
                var sw = Stopwatch.StartNew();
                var response = await _client.GetAsync($"/api/v1/wallets/{wallet.Address}/addresses");
                sw.Stop();

                response.StatusCode.Should().Be(HttpStatusCode.OK);
                measurements.Add(sw.Elapsed.TotalMilliseconds);
            }

            var avgLatency = measurements.Average();
            Console.WriteLine($"Address Count: {count:D3} | Avg Latency: {avgLatency:F2} ms");
        }

        Console.WriteLine("");
        Console.WriteLine("Note: Latency should scale sub-linearly with address count");
    }

    [Fact]
    public async Task Performance_GapStatusCalculation_ShouldBeEfficient()
    {
        // Arrange - Create wallet with many addresses across multiple accounts
        var wallet = await CreateTestWallet();
        var accountCount = 5;
        var addressesPerAccount = 50;

        Console.WriteLine($"=== Gap Status Calculation Performance Test ===");
        Console.WriteLine($"Accounts: {accountCount}");
        Console.WriteLine($"Addresses per account: {addressesPerAccount}");
        Console.WriteLine($"Total addresses: {accountCount * addressesPerAccount}");
        Console.WriteLine("");

        // Register addresses across multiple accounts
        var sw = Stopwatch.StartNew();
        for (uint account = 0; account < accountCount; account++)
        {
            for (int i = 0; i < addressesPerAccount; i++)
            {
                var request = new RegisterDerivedAddressRequest
                {
                    DerivedPublicKey = Convert.ToBase64String(new byte[32]),
                    DerivedAddress = $"ws1q{Guid.NewGuid().ToString("N")}",
                    DerivationPath = $"m/44'/0'/{account}'/0/{i}",
                    Label = $"Acct {account} Address {i}"
                };

                await _client.PostAsJsonAsync($"/api/v1/wallets/{wallet.Address}/addresses", request);
            }
        }
        sw.Stop();
        var registrationTime = sw.Elapsed.TotalMilliseconds;

        // Measure gap status calculation
        var measurements = new List<double>();
        for (int i = 0; i < 20; i++)
        {
            sw = Stopwatch.StartNew();
            var response = await _client.GetAsync($"/api/v1/wallets/{wallet.Address}/gap-status");
            sw.Stop();

            response.StatusCode.Should().Be(HttpStatusCode.OK);
            measurements.Add(sw.Elapsed.TotalMilliseconds);
        }

        var avgLatency = measurements.Average();
        var p95 = CalculatePercentile(measurements, 95);

        Console.WriteLine("=== Results ===");
        Console.WriteLine($"Registration Time: {registrationTime:F2} ms");
        Console.WriteLine($"Gap Status Avg:    {avgLatency:F2} ms");
        Console.WriteLine($"Gap Status P95:    {p95:F2} ms");
        Console.WriteLine("");

        // Assert performance target
        avgLatency.Should().BeLessThan(100, "gap status calculation should be fast even with many addresses");
    }

    [Fact]
    public async Task Performance_ConcurrentRegistration_ShouldHandleLoad()
    {
        // Arrange
        var wallet = await CreateTestWallet();
        var concurrentRequests = 20;
        var requestsPerThread = 10;

        Console.WriteLine($"=== Concurrent Address Registration Test ===");
        Console.WriteLine($"Concurrent threads: {concurrentRequests}");
        Console.WriteLine($"Requests per thread: {requestsPerThread}");
        Console.WriteLine($"Total requests: {concurrentRequests * requestsPerThread}");
        Console.WriteLine("");

        var tasks = new List<Task<List<double>>>();
        var sw = Stopwatch.StartNew();

        // Launch concurrent registration tasks
        for (int thread = 0; thread < concurrentRequests; thread++)
        {
            var threadId = thread;
            tasks.Add(Task.Run(async () =>
            {
                var latencies = new List<double>();
                for (int i = 0; i < requestsPerThread; i++)
                {
                    var request = new RegisterDerivedAddressRequest
                    {
                        DerivedPublicKey = Convert.ToBase64String(new byte[32]),
                        DerivedAddress = $"ws1q{Guid.NewGuid().ToString("N")}",
                        DerivationPath = $"m/44'/0'/{threadId}'/0/{i}",
                        Label = $"Thread {threadId} Addr {i}"
                    };

                    var localSw = Stopwatch.StartNew();
                    var response = await _client.PostAsJsonAsync(
                        $"/api/v1/wallets/{wallet.Address}/addresses",
                        request);
                    localSw.Stop();

                    if (response.StatusCode == HttpStatusCode.Created)
                    {
                        latencies.Add(localSw.Elapsed.TotalMilliseconds);
                    }
                }
                return latencies;
            }));
        }

        var results = await Task.WhenAll(tasks);
        sw.Stop();

        // Aggregate results
        var allLatencies = results.SelectMany(x => x).ToList();
        var totalTime = sw.Elapsed.TotalMilliseconds;
        var successCount = allLatencies.Count;
        var throughput = successCount / (totalTime / 1000.0);
        var avgLatency = allLatencies.Average();
        var p95 = CalculatePercentile(allLatencies, 95);
        var p99 = CalculatePercentile(allLatencies, 99);

        Console.WriteLine("=== Results ===");
        Console.WriteLine($"Total Time:        {totalTime:F2} ms");
        Console.WriteLine($"Successful:        {successCount} / {concurrentRequests * requestsPerThread}");
        Console.WriteLine($"Throughput:        {throughput:F2} ops/sec");
        Console.WriteLine($"Avg Latency:       {avgLatency:F2} ms");
        Console.WriteLine($"P95 Latency:       {p95:F2} ms");
        Console.WriteLine($"P99 Latency:       {p99:F2} ms");

        // Assert performance and correctness
        successCount.Should().Be(concurrentRequests * requestsPerThread, "all requests should succeed");
        throughput.Should().BeGreaterThan(10, "should handle at least 10 ops/sec under concurrent load");
    }

    [Fact]
    public async Task Performance_FilteredQueries_ShouldBeFast()
    {
        // Arrange - Create wallet with diverse addresses
        var wallet = await CreateTestWallet();
        var totalAddresses = 100;

        Console.WriteLine($"=== Filtered Query Performance Test ===");
        Console.WriteLine($"Total addresses: {totalAddresses}");
        Console.WriteLine("");

        // Register addresses with different types and accounts
        for (int i = 0; i < totalAddresses; i++)
        {
            var isChange = i % 3 == 0; // ~33% change addresses
            var account = (uint)(i % 5); // Spread across 5 accounts

            var request = new RegisterDerivedAddressRequest
            {
                DerivedPublicKey = Convert.ToBase64String(new byte[32]),
                DerivedAddress = $"ws1q{Guid.NewGuid().ToString("N")}",
                DerivationPath = $"m/44'/0'/{account}'/{(isChange ? 1 : 0)}/{i}",
                Label = $"Address {i}"
            };

            await _client.PostAsJsonAsync($"/api/v1/wallets/{wallet.Address}/addresses", request);
        }

        // Mark some as used
        var addresses = await GetAllAddresses(wallet.Address);
        for (int i = 0; i < 20; i++)
        {
            await _client.PostAsync(
                $"/api/v1/wallets/{wallet.Address}/addresses/{addresses[i].Id}/mark-used",
                null);
        }

        // Measure different query types
        var queries = new Dictionary<string, string>
        {
            ["All addresses"] = "",
            ["Receive only"] = "?type=receive",
            ["Change only"] = "?type=change",
            ["Account 0"] = "?account=0",
            ["Used"] = "?used=true",
            ["Unused"] = "?used=false",
            ["Complex filter"] = "?type=receive&account=0&used=false"
        };

        Console.WriteLine("Query Performance:");
        foreach (var query in queries)
        {
            var measurements = new List<double>();
            for (int i = 0; i < 10; i++)
            {
                var sw = Stopwatch.StartNew();
                var response = await _client.GetAsync(
                    $"/api/v1/wallets/{wallet.Address}/addresses{query.Value}");
                sw.Stop();

                response.StatusCode.Should().Be(HttpStatusCode.OK);
                measurements.Add(sw.Elapsed.TotalMilliseconds);
            }

            var avgLatency = measurements.Average();
            Console.WriteLine($"  {query.Key,-20} : {avgLatency:F2} ms");
        }

        Console.WriteLine("");
        Console.WriteLine("Note: All queries should have similar performance (in-memory filtering)");
    }

    [Fact]
    public async Task Performance_UpdateOperations_ShouldBeFast()
    {
        // Arrange
        var wallet = await CreateTestWallet();
        var addressCount = 50;

        Console.WriteLine($"=== Update Operations Performance Test ===");
        Console.WriteLine($"Addresses: {addressCount}");
        Console.WriteLine("");

        // Register addresses across multiple accounts to avoid BIP44 gap limit
        for (int i = 0; i < addressCount; i++)
        {
            var account = i / 20; // Spread across accounts (max 20 per account)
            var index = i % 20;

            var request = new RegisterDerivedAddressRequest
            {
                DerivedPublicKey = Convert.ToBase64String(new byte[32]),
                DerivedAddress = $"ws1q{Guid.NewGuid().ToString("N")}",
                DerivationPath = $"m/44'/0'/{account}'/0/{index}",
                Label = $"Address {i}"
            };

            await _client.PostAsJsonAsync($"/api/v1/wallets/{wallet.Address}/addresses", request);
        }

        var addresses = await GetAllAddresses(wallet.Address);

        // Measure update metadata performance
        var updateMeasurements = new List<double>();
        for (int i = 0; i < addressCount; i++)
        {
            var updateRequest = new UpdateAddressRequest
            {
                Label = $"Updated Address {i}",
                Notes = "Performance test note",
                Tags = "perf,test",
                Metadata = new Dictionary<string, string>
                {
                    ["updated"] = "true",
                    ["iteration"] = i.ToString()
                }
            };

            var sw = Stopwatch.StartNew();
            var response = await _client.PatchAsJsonAsync(
                $"/api/v1/wallets/{wallet.Address}/addresses/{addresses[i].Id}",
                updateRequest);
            sw.Stop();

            response.StatusCode.Should().Be(HttpStatusCode.OK);
            updateMeasurements.Add(sw.Elapsed.TotalMilliseconds);
        }

        // Measure mark-as-used performance
        var markUsedMeasurements = new List<double>();
        for (int i = 0; i < addressCount; i++)
        {
            var sw = Stopwatch.StartNew();
            var response = await _client.PostAsync(
                $"/api/v1/wallets/{wallet.Address}/addresses/{addresses[i].Id}/mark-used",
                null);
            sw.Stop();

            response.StatusCode.Should().Be(HttpStatusCode.OK);
            markUsedMeasurements.Add(sw.Elapsed.TotalMilliseconds);
        }

        var updateAvg = updateMeasurements.Average();
        var updateP95 = CalculatePercentile(updateMeasurements, 95);
        var markUsedAvg = markUsedMeasurements.Average();
        var markUsedP95 = CalculatePercentile(markUsedMeasurements, 95);

        Console.WriteLine("=== Results ===");
        Console.WriteLine($"Update Metadata:");
        Console.WriteLine($"  Avg:     {updateAvg:F2} ms");
        Console.WriteLine($"  P95:     {updateP95:F2} ms");
        Console.WriteLine("");
        Console.WriteLine($"Mark as Used:");
        Console.WriteLine($"  Avg:     {markUsedAvg:F2} ms");
        Console.WriteLine($"  P95:     {markUsedP95:F2} ms");

        // Assert performance targets
        updateAvg.Should().BeLessThan(100, "update should be fast");
        markUsedAvg.Should().BeLessThan(100, "mark-used should be fast");
    }

    #region Helper Methods

    private async Task<WalletDto> CreateTestWallet()
    {
        var request = new CreateWalletRequest
        {
            Name = "Performance Test Wallet",
            Algorithm = "ED25519",
            WordCount = 12
        };

        var response = await _client.PostAsJsonAsync("/api/v1/wallets", request);
        response.StatusCode.Should().Be(HttpStatusCode.Created);

        var result = await response.Content.ReadFromJsonAsync<CreateWalletResponse>();
        return result!.Wallet;
    }

    private async Task<List<WalletAddressDto>> GetAllAddresses(string walletAddress)
    {
        var response = await _client.GetAsync($"/api/v1/wallets/{walletAddress}/addresses?pageSize=1000");
        response.StatusCode.Should().Be(HttpStatusCode.OK);

        var result = await response.Content.ReadFromJsonAsync<AddressListResponse>();
        return result!.Addresses;
    }

    private static double CalculatePercentile(List<double> sortedData, int percentile)
    {
        var data = sortedData.OrderBy(x => x).ToList();
        var index = (int)Math.Ceiling((percentile / 100.0) * data.Count) - 1;
        return data[Math.Max(0, Math.Min(index, data.Count - 1))];
    }

    #endregion
}
