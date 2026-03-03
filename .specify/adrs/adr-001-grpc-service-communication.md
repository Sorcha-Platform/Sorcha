# ADR-001: Adopt gRPC for Internal Service Communication

**Status:** Accepted
**Date:** 2025-12-13
**Deciders:** Sorcha Architecture Team
**Related ADRs:** None (foundational decision)
**Related Issues:** AUTH-002 (Service Authentication Integration)

## Context

The Sorcha platform currently uses HTTP REST APIs with JWT authentication for all service-to-service communication. As documented in the development status report, the platform is 98% complete with:

- Blueprint Service (100% complete, 123 tests)
- Wallet Service (95% complete, 67 tests)
- Register Service (100% complete, 112 tests)
- Tenant Service (85% complete, 67 integration tests)

All services currently communicate via HTTP REST with:
- JWT Bearer authentication
- Delegation tokens via custom `X-Delegation-Token` HTTP headers
- OAuth2 client credentials for service-to-service auth
- .NET Aspire orchestration

### Problem Statement

While REST/HTTP works, it has limitations for internal service communication:

1. **Performance Overhead:** JSON serialization/deserialization and HTTP/1.1 overhead
2. **Type Safety:** No compile-time contract validation between services
3. **Streaming Limitations:** HTTP REST not optimized for bidirectional streaming
4. **Coupling:** Changes to REST contracts not validated until runtime

The requirement has been identified to use gRPC for internal service-to-service communication with:
- Mutual TLS (mTLS) for authentication
- Delegation token support for user context propagation
- Strong typing via Protocol Buffers

### Constraints

- **Technical:** Must maintain existing JWT authentication infrastructure
- **Technical:** Must support delegation tokens for user context
- **Business:** Cannot disrupt existing services during migration
- **Time:** Platform is 98% complete - minimize rework
- **Resource:** 369 existing integration tests need updating

### Assumptions

- Services will continue to expose REST APIs for external clients (via API Gateway)
- mTLS certificate infrastructure can be established (Azure Key Vault)
- Development team has capacity for 144-204 hour migration effort
- .NET Aspire supports gRPC service discovery

## Decision Drivers

1. **Performance:** Binary Protocol Buffers vs JSON serialization
2. **Type Safety:** Compile-time contract validation via .proto files
3. **Industry Standard:** gRPC is the de-facto standard for microservices
4. **Streaming:** Native support for server/client/bidirectional streaming
5. **Security:** Built-in mTLS support for service-to-service authentication
6. **Tooling:** Excellent .NET support via Grpc.Net.Client and Grpc.AspNetCore
7. **HTTP/2:** Multiplexing, header compression, server push

## Considered Options

### Option 1: Keep HTTP REST for All Communication

**Description:**
Continue using HTTP REST APIs for both external and internal service communication. Maintain current JWT Bearer authentication with delegation tokens via custom headers.

**Pros:**
- Zero migration effort required
- All existing tests continue to work (369 tests)
- Team already familiar with REST
- OpenAPI documentation already in place
- Works well for request/response patterns

**Cons:**
- JSON serialization overhead (5-10x slower than Protocol Buffers)
- No compile-time contract validation
- HTTP/1.1 performance limitations (unless upgraded to HTTP/2)
- No native streaming support
- Custom header handling for delegation tokens
- Not industry standard for internal microservices

**Effort:** 0 hours

---

### Option 2: Adopt gRPC for Internal Services Only (Hybrid)

**Description:**
Use gRPC for internal service-to-service communication while maintaining REST for external client-facing APIs. API Gateway translates REST → gRPC for external requests.

**Architecture:**
```
External Clients → [REST] → API Gateway → [gRPC] → Internal Services
                                                      (Blueprint, Wallet, Register)
```

**Pros:**
- Best of both worlds (REST for clients, gRPC for services)
- 5-10x performance improvement for internal calls
- Strong typing via Protocol Buffers
- Built-in mTLS support
- HTTP/2 multiplexing
- Industry standard pattern (used by Google, Netflix, etc.)
- Delegation tokens via gRPC metadata
- Compile-time contract validation
- Easier debugging during migration (dual-stack possible)

**Cons:**
- Migration effort: 120-160 hours
- 369 integration tests need rewriting
- Team learning curve for gRPC
- API Gateway complexity increases
- Need certificate infrastructure (mTLS)
- Dual-stack maintenance during migration

**Effort:** 120-160 hours

---

### Option 3: Full gRPC Adoption (Including External APIs)

**Description:**
Use gRPC for all communication - both internal and external. Require clients to use gRPC clients or gRPC-Web for browser clients.

**Pros:**
- Maximum performance benefits
- Single technology stack
- No REST/gRPC translation layer
- Consistent patterns across all services

**Cons:**
- Forces external clients to adopt gRPC (breaking change)
- Browser clients need gRPC-Web (additional complexity)
- Higher migration effort: 224-324 hours
- Loses OpenAPI documentation benefits
- Not ideal for public APIs (REST preferred)
- Mobile app integration more complex

**Effort:** 224-324 hours

## Decision

**Selected Option:** Option 2 - Adopt gRPC for Internal Services Only (Hybrid Approach)

**Rationale:**

1. **Performance:** Internal service calls will see 5-10x performance improvement
   - Token introspection: 8ms → 2ms
   - Wallet signing: 50ms → 10ms
   - Register queries: 20ms → 5ms

2. **Industry Standard:** Aligns with microservices best practices (Google, Netflix, Uber all use this pattern)

3. **Type Safety:** Protocol Buffers provide compile-time contract validation, reducing runtime errors

4. **Security:** Built-in mTLS is more robust than custom HTTP header authentication

5. **Backwards Compatibility:** External clients continue using familiar REST APIs

6. **Migration Risk:** Phased approach allows gradual migration with dual-stack support

7. **Future-Proof:** Enables streaming scenarios (e.g., real-time transaction notifications)

## Consequences

### Positive

- **Performance:** 5-10x faster internal service communication
- **Type Safety:** .proto files validated at compile time
- **Security:** Mutual TLS provides stronger authentication than custom headers
- **Streaming:** Enables future real-time features (e.g., transaction streaming)
- **Tooling:** gRPC reflection, grpcurl, Postman support
- **Observability:** Better tracing with gRPC interceptors
- **Industry Alignment:** Using proven patterns from industry leaders

### Negative

- **Migration Effort:** 120-160 hours of development time
- **Test Rewrite:** 369 integration tests need updating
- **Learning Curve:** Team needs to learn Protocol Buffers and gRPC patterns
- **Infrastructure:** Requires mTLS certificate management
- **Complexity:** Dual protocol support during migration
- **Debugging:** Binary protocol harder to inspect than JSON (mitigated by tooling)

### Neutral

- **API Gateway:** Needs to handle REST → gRPC translation (standard pattern)
- **Documentation:** Replace OpenAPI for internal services with .proto documentation
- **Dual-Stack:** Services will run both REST and gRPC during migration

### Risks

| Risk | Impact | Probability | Mitigation |
|------|--------|-------------|------------|
| Migration breaks existing services | HIGH | MEDIUM | Phased migration with dual-stack support |
| Team struggles with gRPC | MEDIUM | LOW | Training, pair programming, comprehensive docs |
| Certificate management complexity | MEDIUM | MEDIUM | Use Azure Key Vault, automate rotation |
| Performance doesn't meet expectations | LOW | LOW | Benchmark early, validate assumptions |
| External clients confused by hybrid | LOW | LOW | Clear API documentation, API Gateway abstraction |

## Implementation

### Migration Strategy

**Phase 1: Infrastructure Setup (Week 1-2)**
- Set up mTLS certificate infrastructure (Azure Key Vault)
- Create .proto file definitions for all services
- Add gRPC NuGet packages to all services
- Configure .NET Aspire for gRPC service discovery

**Phase 2: Dual-Stack Implementation (Week 3-4)**
- Implement gRPC services alongside existing REST endpoints
- Add gRPC interceptors for authentication and delegation tokens
- Maintain both REST and gRPC endpoints during transition

**Phase 3: Service Migration (Week 5-7)**
- Migrate Blueprint → Wallet calls to gRPC
- Migrate Blueprint → Register calls to gRPC
- Migrate Tenant Service → All services to gRPC
- Update integration tests incrementally

**Phase 4: Validation and Cleanup (Week 8)**
- Performance benchmarking
- Remove dual-stack REST endpoints from internal services
- Update documentation
- Final integration testing

### Timeline

- **Week 1-2:** Infrastructure and .proto definitions (24 hours)
- **Week 3-4:** Dual-stack implementation (40 hours)
- **Week 5-7:** Service migration and testing (64 hours)
- **Week 8:** Validation, cleanup, documentation (16 hours)
- **Total:** 144 hours (~18 days)

### Dependencies

1. **Certificate Infrastructure**
   - Azure Key Vault for certificate storage
   - Certificate rotation automation
   - mTLS configuration for all services

2. **Service Updates**
   - Wallet Service: WalletService.proto with Sign, Encrypt, Decrypt RPCs
   - Register Service: RegisterService.proto with Submit, Query RPCs
   - Tenant Service: TenantService.proto with Introspect, Token RPCs
   - Blueprint Service: gRPC clients for Wallet and Register

3. **Testing Infrastructure**
   - gRPC test server setup
   - Mock gRPC services for integration tests
   - Performance benchmarking tools

4. **.NET Aspire Updates**
   - gRPC service discovery configuration
   - Health checks via gRPC health protocol
   - Distributed tracing for gRPC calls

### Success Criteria

1. **Performance:** Internal service calls 4x faster than REST baseline
2. **Security:** All internal calls use mTLS with valid certificates
3. **Delegation:** User context properly propagated via gRPC metadata
4. **Testing:** 100% of existing integration tests migrated and passing
5. **Documentation:** All .proto files documented with inline comments
6. **Observability:** gRPC calls visible in distributed tracing (Zipkin)
7. **Stability:** Zero regression in existing functionality
8. **External APIs:** No breaking changes for external clients

## Validation

This decision will be validated by:

1. **Performance Benchmarks:** Measure actual latency improvements (target: 4-5x)
2. **Integration Testing:** All 369+ tests passing with gRPC
3. **Security Audit:** Verify mTLS implementation and certificate management
4. **User Acceptance:** Pilot with internal development team
5. **Production Metrics:** Monitor latency, error rates, throughput after deployment

**Re-evaluation Triggers:**
- Performance improvements < 2x (not worth the effort)
- Major security vulnerabilities discovered in gRPC stack
- Certificate management proves unmanageable
- External clients demand native gRPC access

## References

- [gRPC Official Documentation](https://grpc.io/docs/)
- [gRPC for .NET](https://learn.microsoft.com/en-us/aspnet/core/grpc/)
- [Protocol Buffers v3](https://protobuf.dev/)
- [Mutual TLS in gRPC](https://grpc.io/docs/guides/auth/)
- [gRPC Performance Best Practices](https://grpc.io/docs/guides/performance/)
- [Sorcha Constitution v1.3](../constitution.md)
- [Sorcha Development Status](../../docs/reference/development-status.md)
- [AUTH-002 Service Authentication Integration](../../docs/guides/AUTHENTICATION-SETUP.md)

## Revision History

| Date | Version | Changes | Author |
|------|---------|---------|--------|
| 2025-12-13 | 1.0 | Initial ADR for gRPC adoption | Sorcha Architecture Team |
