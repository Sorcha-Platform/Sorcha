# Monitoring & Observability

Sorcha uses OpenTelemetry (OTEL) for distributed tracing, metrics, and structured logging, collected by the .NET Aspire Dashboard.

## Architecture

```
┌─────────────┐     OTLP/gRPC       ┌──────────────────┐
│  All Sorcha  │───────────────────>│  Aspire Dashboard  │
│  Services    │  (port 18889)      │  (traces, logs,    │
└─────────────┘                     │   metrics)         │
                                    └────────┬───────────┘
                                             │
                              ┌──────────────┼──────────────┐
                              v                             v
                     ┌────────────────┐           ┌─────────────────┐
                     │ :18888 direct  │           │ /admin/dashboard │
                     │ (dev only)     │           │ (SystemAdmin JWT)│
                     └────────────────┘           └─────────────────┘
```

All Sorcha services export telemetry data via OTLP gRPC to the Aspire Dashboard container. The dashboard provides a web UI for exploring traces, logs, and metrics.

## Accessing the Dashboard

### Direct Access (Development)

```
http://localhost:18888
```

The Aspire Dashboard is accessible without authentication in the default Docker configuration (`DOTNET_DASHBOARD_UNSECURED_ALLOW_ANONYMOUS=true`).

### Via API Gateway (Production)

```
http://localhost/admin/dashboard
```

Access through the API Gateway requires a JWT token with the `SystemAdmin` role. This is the recommended approach for production deployments.

### Securing the Dashboard

For production, disable anonymous access:

```yaml
aspire-dashboard:
  environment:
    - DOTNET_DASHBOARD_UNSECURED_ALLOW_ANONYMOUS=false
    - DASHBOARD__FRONTEND__AUTHMODE=BrowserToken
    - DASHBOARD__FRONTEND__BROWSERTOKEN__TOKEN=<secure-token>
```

## Health Check Endpoints

Every Sorcha service exposes a `/health` endpoint. Via the API Gateway:

| Endpoint | Service | Check Includes |
|----------|---------|----------------|
| `http://localhost/health` | API Gateway | Gateway process |
| `http://localhost/blueprint/health` | Blueprint | Redis, MongoDB, downstream services |
| `http://localhost/tenant/health` | Tenant | PostgreSQL, Redis |
| `http://localhost/wallet/health` | Wallet | PostgreSQL, Redis, encryption provider |
| `http://localhost/register/health` | Register | MongoDB, Redis |
| `http://localhost/validator/health` | Validator | Redis, MongoDB |
| `http://localhost/peer/health` | Peer | Redis, MongoDB |

### Direct Service Health (bypassing gateway)

| Endpoint | Service |
|----------|---------|
| `http://localhost:5000/health` | Blueprint |
| `http://localhost:5450/health` | Tenant |
| `http://localhost:5380/health` | Register |
| `http://localhost:5800/health` | Validator |

### Docker Health Checks

Docker Compose includes built-in health checks for all services. Monitor container health:

```bash
# Show all container statuses
docker-compose ps

# Show only unhealthy containers
docker ps --filter health=unhealthy

# Inspect a specific container's health check history
docker inspect --format='{{json .State.Health}}' sorcha-blueprint-service | jq
```

Health check configuration (per container):
- **Interval:** 10 seconds
- **Timeout:** 5 seconds
- **Retries:** 10
- **Start period:** 30 seconds

## Logging

### Log Levels

Configure log verbosity per service via `ASPNETCORE_ENVIRONMENT`:

| Environment | Default Level | SQL Queries | Request Details |
|-------------|---------------|-------------|-----------------|
| `Development` | Debug | Visible | Verbose |
| `Docker` | Information | Hidden | Standard |
| `Production` | Warning | Hidden | Minimal |

### Viewing Logs

**Docker Compose logs:**

```bash
# All services
docker-compose logs -f

# Specific service
docker-compose logs -f blueprint-service

# Last 100 lines
docker-compose logs --tail=100 tenant-service

# Since a specific time
docker-compose logs --since="2026-01-01T00:00:00" wallet-service
```

**Aspire Dashboard:** The Structured Logs tab in the Aspire Dashboard provides filtering, search, and correlation of log entries across all services.

### Structured Logging Format

All Sorcha services use Serilog for structured logging. Log entries include:

| Field | Description |
|-------|-------------|
| `Timestamp` | ISO 8601 timestamp |
| `Level` | Log level (Debug, Information, Warning, Error, Fatal) |
| `MessageTemplate` | Structured message with named placeholders |
| `Properties` | Key-value pairs (correlation ID, user ID, etc.) |
| `Exception` | Exception details (if applicable) |
| `SourceContext` | Originating class/namespace |
| `TraceId` | OpenTelemetry trace ID for correlation |
| `SpanId` | OpenTelemetry span ID |

### Log Output Configuration

By default, logs are written to stdout (captured by Docker). To add file-based logging or external sinks, configure Serilog in `appsettings.json` or via environment variables:

```bash
# Set minimum log level
Serilog__MinimumLevel__Default=Information

# Override for specific namespaces
Serilog__MinimumLevel__Override__Microsoft=Warning
Serilog__MinimumLevel__Override__System=Warning
```

## Distributed Tracing

### Viewing Traces

Open the Aspire Dashboard Traces tab to see:
- End-to-end request flows across services
- Latency breakdown per service hop
- Error traces highlighted in red
- Dependency calls (database queries, Redis operations, HTTP clients)

### Trace Correlation

All HTTP requests flowing through the API Gateway receive a trace ID that propagates to downstream services. This enables end-to-end visibility of a single user request across all services.

### Key Trace Attributes

| Attribute | Description |
|-----------|-------------|
| `service.name` | Service that generated the span |
| `deployment.environment` | `docker` or `production` |
| `http.method` | HTTP method (GET, POST, etc.) |
| `http.url` | Request URL |
| `http.status_code` | Response status code |
| `db.system` | Database type (postgresql, mongodb, redis) |
| `db.statement` | Database query (in Development mode) |

## Metrics

The Aspire Dashboard Metrics tab shows runtime metrics including:

- **ASP.NET Core:** Request rate, response time, active connections
- **Runtime:** GC collections, thread pool usage, memory
- **HTTP Client:** Outbound request rate and latency
- **Database:** Connection pool size, query duration

## External OTEL Integration

To send telemetry to external observability platforms instead of (or in addition to) the Aspire Dashboard, change the OTLP endpoint:

### Datadog

```yaml
environment:
  OTEL_EXPORTER_OTLP_ENDPOINT: http://datadog-agent:4317
  OTEL_EXPORTER_OTLP_PROTOCOL: grpc
```

### Grafana (via Grafana Agent / Alloy)

```yaml
environment:
  OTEL_EXPORTER_OTLP_ENDPOINT: http://grafana-agent:4317
  OTEL_EXPORTER_OTLP_PROTOCOL: grpc
```

### Azure Monitor (Application Insights)

```yaml
environment:
  APPLICATIONINSIGHTS_CONNECTION_STRING: InstrumentationKey=<key>;IngestionEndpoint=https://<region>.in.applicationinsights.azure.com/
```

Azure Monitor integration uses the Application Insights SDK rather than pure OTLP. Add the `Azure.Monitor.OpenTelemetry.AspNetCore` NuGet package for native support.

### Dual Export (Dashboard + External)

To keep the Aspire Dashboard while also exporting to an external system, use an OpenTelemetry Collector as an intermediary:

```
Services --> OTEL Collector --> Aspire Dashboard
                            --> External Platform
```

## Alerting Recommendations

For production deployments, configure alerts on:

| Metric | Threshold | Severity |
|--------|-----------|----------|
| Health check failure | Any service unhealthy > 2 min | Critical |
| Response time (p95) | > 2 seconds | Warning |
| Error rate (5xx) | > 1% of requests | Critical |
| Disk usage | > 80% on database volumes | Warning |
| Memory usage | > 90% per container | Warning |
| MongoDB oplog lag | > 10 seconds | Warning |
| PostgreSQL connection pool | > 80% utilized | Warning |
