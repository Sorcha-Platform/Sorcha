# Performance Benchmark - Quick Start

This walkthrough provides comprehensive performance testing for the Sorcha Register Service.

---

## Prerequisites

1. **Docker Services Running**
   ```bash
   docker-compose up -d
   # Wait 30 seconds for services to be healthy
   ```

2. **Bootstrap Performance Organization**
   ```powershell
   ./walkthroughs/PerformanceBenchmark/bootstrap-perf-org.ps1
   ```

   This creates:
   - Organization: "Performance Testing" (subdomain: perf)
   - Admin user: `admin@perf.local` / `PerfTest2026!`

---

## Running Tests

### Full Benchmark Suite (15-20 minutes)
```powershell
./walkthroughs/PerformanceBenchmark/test-performance.ps1
```

Tests performed:
- ✅ Payload sizes: 1KB, 10KB, 50KB, 100KB, 500KB, 1MB
- ✅ Throughput: 60-second sustained load
- ✅ Latency: Single-threaded, light, moderate, heavy load scenarios
- ✅ Concurrency: 1, 5, 10, 25, 50 parallel workers
- ✅ Docket building: 10, 50, 100, 500 transaction batches

### Quick Test (5 minutes)
```powershell
./walkthroughs/PerformanceBenchmark/test-performance.ps1 -QuickTest
```

Runs reduced test suite:
- Payload sizes: 1KB, 10KB, 50KB
- Throughput: 30-second duration
- Latency: 50 iterations per scenario
- Concurrency: 1, 5, 10 workers
- Docket building: 10, 50 transactions

### Custom Configuration
```powershell
./walkthroughs/PerformanceBenchmark/test-performance.ps1 `
  -MaxPayloadSize 512KB `
  -Iterations 50 `
  -Concurrency 10 `
  -Profile direct
```

---

## Profiles

| Profile | Description | Use When |
|---------|-------------|----------|
| `gateway` | All requests through API Gateway (port 80) | **Default** - Tests real-world routing overhead |
| `direct` | Direct service ports (bypasses API Gateway) | Isolating Register Service performance |
| `aspire` | .NET Aspire HTTPS ports (7xxx) | Local development with Aspire |

---

## Results

Test results are saved to `results/` directory:

```
results/
├── benchmark-2026-02-13_10-30-00.json    # Detailed metrics (JSON)
└── summary-2026-02-13_10-30-00.md        # Human-readable report
```

### Sample Output

```
================================================================================
  TEST 1: Payload Size Performance
================================================================================

[STEP] Testing payload size: 1KB
  ✓ Completed 1KB test
    Successful     : 100 / 100 txs
    Error Rate     : 0.00 %
    Mean Latency   : 45.23 ms
    Median Latency : 42.10 ms
    P95 Latency    : 78.50 ms
    P99 Latency    : 95.20 ms

[STEP] Testing payload size: 10KB
  ✓ Completed 10KB test
    Successful     : 100 / 100 txs
    Error Rate     : 0.00 %
    Mean Latency   : 52.34 ms
    ...
```

---

## Monitoring Resources

Run alongside performance tests to monitor Docker container resource usage:

```powershell
# In a separate terminal
./walkthroughs/PerformanceBenchmark/monitor-resources.ps1 -Duration 300 -Interval 5
```

Captures:
- CPU usage per container
- Memory usage and limits
- Network I/O
- Block I/O

Results saved to `resource-usage.csv` for analysis in Excel/Python.

---

## Interpreting Results

### Payload Size Performance
- **Expected:** Linear latency increase with payload size
- **Warning:** P95 >500ms for payloads <100KB indicates bottleneck
- **Action:** Check MongoDB write performance, connection pooling

### Throughput
- **Target:** 100+ TPS sustained (5KB payloads)
- **Warning:** <50 TPS or error rate >1%
- **Action:** Check Register Service CPU/memory, MongoDB connection limits

### Latency Benchmarks
- **Target:** P95 <200ms under normal load
- **Warning:** P99 >1000ms indicates outliers
- **Action:** Profile Register Service with dotnet-trace

### Concurrency
- **Expected:** Linear scaling up to 10-25 workers
- **Warning:** >50% degradation with 10 workers
- **Action:** Increase connection pools, check thread pool exhaustion

### Docket Building
- **Target:** 100 transactions in <2 seconds
- **Warning:** >5 seconds for 100 transactions
- **Action:** Profile validator service, check signature verification overhead

---

## Troubleshooting

### Services crash during testing
**Symptom:** Docker containers restart mid-test

**Cause:** Memory/CPU exhaustion

**Fix:**
```bash
# Check resource usage
docker stats

# Increase Docker resources (Settings → Resources)
# Or reduce test concurrency:
./test-performance.ps1 -Concurrency 10 -QuickTest
```

### High error rates
**Symptom:** >5% error rate in results

**Possible Causes:**
1. MongoDB connection pool exhaustion
2. Rate limiting (50 req/minute default)
3. Request timeouts

**Fix:**
```bash
# Check logs
docker logs sorcha-register-service-1

# Increase MongoDB connections (docker-compose.yml)
# Adjust rate limiting (Register Service appsettings.json)
```

### Inconsistent results
**Symptom:** Wide variance between test runs

**Cause:** Background processes, Docker resource contention

**Fix:**
- Close other applications
- Run tests multiple times and average
- Use `-Profile direct` to isolate API Gateway overhead

---

## Next Steps

1. **Establish Baseline**
   - Run full benchmark suite 3 times
   - Average results
   - Document as baseline in `PERFORMANCE-REPORT.md`

2. **Identify Bottlenecks**
   - Review P95/P99 latencies
   - Check resource usage patterns
   - Compare `gateway` vs `direct` profiles

3. **Apply Optimizations**
   - Add MongoDB indexes
   - Tune connection pools
   - Enable response compression
   - Implement request batching

4. **Validate Improvements**
   - Re-run affected test scenarios
   - Compare before/after metrics
   - Update `PERFORMANCE-REPORT.md`

5. **Capacity Planning**
   - Use TPS metrics for load estimation
   - Plan MongoDB scaling strategy
   - Set up monitoring alerts

---

## Known Issues

1. **Register Creation in Performance Script**
   - Status: 🚧 In Progress
   - Workaround: Create register manually, then test transactions against it
   - Fix: Update register creation flow to match latest API changes

2. **Docket Building Tests**
   - Requires validator service to be fully operational
   - May timeout for large docket sizes (>500 txs)

3. **Concurrency >50 Workers**
   - May hit system limits (file descriptors, network connections)
   - Reduce if encountering "too many open files" errors

---

## Support

For issues or questions:
- Check `PERFORMANCE-REPORT.md` for analysis framework
- Review `../README.md` for other walkthroughs
- See `../../docs/architecture.md` for system design

---

**Happy Benchmarking!** 🚀
