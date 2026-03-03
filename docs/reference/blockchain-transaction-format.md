# Blockchain Transaction Format - JSON-LD Specification

**Version:** 1.0.0
**Last Updated:** 2025-11-13
**Status:** Architectural Requirement
**Authority:** Sorcha Architecture Team

## Overview

This document defines the universal JSON-LD format for representing blockchain transactions and transaction references within the Sorcha platform. This format ensures interoperability, semantic consistency, and alignment with W3C standards.

## Constitutional Alignment

This specification aligns with the following constitutional principles:

- **Microservices-First Architecture** - Provides a standard interchange format between services
- **Security Principles** - Enables cryptographic verification and audit trails
- **Development Standards** - Follows W3C standards and semantic web best practices
- **Data Management** - Defines canonical representation for blockchain data

## Purpose

To establish a universal, machine-readable, and semantically rich format for:

1. **Transaction References** - Unique identifiers for blockchain transactions
2. **Transaction Metadata** - Complete transaction information with context
3. **Cross-Service Communication** - Standard format for API responses
4. **Audit and Compliance** - Traceable references to immutable ledger data

## Design Principles

### 1. W3C Standards Compliance

- **JSON-LD 1.1** - Linked Data format for semantic web integration
- **Decentralized Identifiers (DIDs)** - W3C DID specification for universal identifiers
- **Verifiable Credentials** - Support for blockchain-anchored credentials

### 2. Universal Addressability

All transactions MUST be addressable via a universal DID-based URI:

```
did:sorcha:register:{registerId}/tx/{txId}
```

This format provides:
- **Unique Identification** - No collision across registers
- **Resolvability** - Can be dereferenced to retrieve full transaction
- **Self-Describing** - Contains namespace, register, and transaction identifiers

### 3. Semantic Consistency

Use standardized vocabularies:
- `https://sorcha.dev/blockchain/v1#` - Sorcha blockchain vocabulary
- `https://w3id.org/security#` - W3C security vocabulary
- `https://w3id.org/blockchain/v1#` - Generic blockchain vocabulary (community standard)

### 4. Extensibility

The format MUST support:
- Additional metadata fields
- Custom vocabularies via context expansion
- Backward compatibility with future versions

## JSON-LD Context Definition

### Blockchain Transaction Context

The canonical JSON-LD context for Sorcha blockchain transactions:

```json
{
  "@context": {
    "@version": 1.1,
    "@vocab": "https://sorcha.dev/blockchain/v1#",
    "sec": "https://w3id.org/security#",
    "blockchain": "https://w3id.org/blockchain/v1#",
    "xsd": "http://www.w3.org/2001/XMLSchema#",
    "did": "https://www.w3.org/ns/did/v1#",

    "TransactionReference": "blockchain:TransactionReference",
    "Transaction": "blockchain:Transaction",

    "txId": {
      "@id": "blockchain:transactionHash",
      "@type": "@id"
    },
    "previousTxHash": {
      "@id": "blockchain:previousTransactionHash",
      "@type": "@id"
    },
    "registerId": {
      "@id": "blockchain:registerId",
      "@type": "@id"
    },
    "blockNumber": {
      "@id": "blockchain:blockNumber",
      "@type": "xsd:unsignedLong"
    },
    "timestamp": {
      "@id": "sec:created",
      "@type": "xsd:dateTime"
    },
    "senderWallet": {
      "@id": "blockchain:from",
      "@type": "@id"
    },
    "recipients": {
      "@id": "blockchain:to",
      "@type": "@id",
      "@container": "@list"
    },
    "metadata": {
      "@id": "blockchain:metadata",
      "@type": "@json"
    },
    "signature": {
      "@id": "sec:signatureValue",
      "@type": "xsd:base64Binary"
    },
    "payloadCount": {
      "@id": "blockchain:payloadCount",
      "@type": "xsd:unsignedInt"
    },
    "version": {
      "@id": "blockchain:version",
      "@type": "xsd:unsignedInt"
    }
  }
}
```

**Context Location:** `https://sorcha.dev/contexts/blockchain/v1.jsonld`

## Transaction Reference Format

### Minimal Transaction Reference

For referencing a transaction without full details:

```json
{
  "@context": "https://sorcha.dev/contexts/blockchain/v1.jsonld",
  "@type": "TransactionReference",
  "@id": "did:sorcha:register:a1b2c3d4-e5f6-7890-abcd-ef1234567890/tx/abc123def456",
  "registerId": "a1b2c3d4-e5f6-7890-abcd-ef1234567890",
  "txId": "abc123def456",
  "timestamp": "2025-11-13T12:00:00Z"
}
```

### Full Transaction Format

Complete transaction with all metadata:

```json
{
  "@context": "https://sorcha.dev/contexts/blockchain/v1.jsonld",
  "@type": "Transaction",
  "@id": "did:sorcha:register:a1b2c3d4-e5f6-7890-abcd-ef1234567890/tx/abc123def456",

  "registerId": "a1b2c3d4-e5f6-7890-abcd-ef1234567890",
  "txId": "abc123def456",
  "previousTxHash": "xyz789uvw012",
  "blockNumber": 12345,
  "version": 4,
  "timestamp": "2025-11-13T12:00:00Z",

  "senderWallet": "ws1qyqszqgp123...",
  "recipients": [
    "ws1recipient1...",
    "ws1recipient2..."
  ],

  "metadata": {
    "blueprintId": "workflow-123",
    "instanceId": "instance-456",
    "actionId": 2,
    "type": "Action"
  },

  "signature": "base64encodedsignature==",
  "payloadCount": 1
}
```

## DID URI Format

### Specification

```
did:sorcha:register:{registerId}/tx/{txId}
```

**Components:**
- `did` - DID scheme identifier
- `sorcha` - DID method (Sorcha platform)
- `register:{registerId}` - Specific register/ledger identifier (UUID format)
- `tx/{txId}` - Transaction hash within the register

### Examples

```
did:sorcha:register:550e8400-e29b-41d4-a716-446655440000/tx/a1b2c3d4e5f6
did:sorcha:register:123e4567-e89b-12d3-a456-426614174000/tx/xyz789uvw012
```

### Resolution

DID URIs MUST be resolvable via the Register Service API:

```
GET /api/registers/{registerId}/transactions/{txId}
```

Response:
```json
{
  "@context": "https://sorcha.dev/contexts/blockchain/v1.jsonld",
  "@type": "Transaction",
  "@id": "did:sorcha:register:{registerId}/tx/{txId}",
  ...
}
```

## Integration with Blueprint Models

### Action Model Transaction References

The `Action` model MUST use DID-based transaction references:

```json
{
  "@context": [
    "https://sorcha.dev/blueprint/v1",
    "https://sorcha.dev/contexts/blockchain/v1.jsonld"
  ],
  "@type": "as:Activity",
  "id": 1,
  "title": "Submit Order",
  "previousTxId": "did:sorcha:register:550e8400-e29b-41d4-a716-446655440000/tx/abc123",
  "published": "2025-11-13T12:00:00Z",
  "sender": "buyer",
  "target": "seller"
}
```

### Participant Model Wallet Addresses

Participant wallet addresses SHOULD be represented as DIDs where possible:

```json
{
  "@type": "schema:Person",
  "id": "participant-001",
  "walletAddress": "ws1qyqszqgp123...",
  "didUri": "did:sorcha:wallet:ws1qyqszqgp123..."
}
```

## Implementation Requirements

### 1. JSON-LD Context Service

**Location:** `src/Common/Sorcha.Blueprint.Models/JsonLd/BlockchainContext.cs`

- Provide static blockchain context definition
- Support context merging with Blueprint contexts
- Cache context for performance

### 2. Transaction Reference Model

**Location:** `src/Common/Sorcha.TransactionHandler/Models/TransactionReference.cs`

- Implement `TransactionReference` class with JSON-LD serialization
- Provide DID URI generation and parsing
- Validate registerId and txId formats

### 3. Transaction Model Updates

**Location:** `src/Common/Sorcha.TransactionHandler/Core/Transaction.cs`

- Add JSON-LD context support
- Include `@type` and `@id` fields in serialization
- Support both legacy and JSON-LD formats for backward compatibility

### 4. API Response Format

All Register Service APIs returning transactions MUST:
- Include `@context` in response
- Use DID URIs for `@id` field
- Support content negotiation (`application/ld+json`)

### 5. Backward Compatibility

During migration:
- Support both formats in API responses (via Accept header)
- Provide conversion utilities between legacy and JSON-LD formats
- Maintain legacy endpoints for 2 major versions

## Verification and Validation

### JSON-LD Validation

Transaction documents MUST:
- Be valid JSON-LD 1.1 documents
- Expand successfully without errors
- Reference resolvable contexts

### DID URI Validation

DID URIs MUST:
- Match the format: `did:sorcha:register:{uuid}/tx/{hex}`
- Use valid UUID v4 for registerId
- Use valid transaction hash format for txId

### Schema Validation

Optionally provide JSON Schema for validation:

**Location:** `src/Common/blockchain-transaction.schema.json`

## Security Considerations

### 1. Context Integrity

- Host contexts on HTTPS
- Implement context caching to prevent tampering
- Use subresource integrity (SRI) where applicable

### 2. DID Resolution Security

- Validate registerId exists before resolution
- Implement rate limiting on DID resolution endpoints
- Audit all transaction access attempts

### 3. Signature Verification

- Maintain `signature` field for cryptographic verification
- Support external signature verification via `/verify` endpoint
- Include signature metadata (algorithm, key ID)

## Performance Considerations

### 1. Context Caching

- Cache JSON-LD contexts in-memory
- Set appropriate HTTP cache headers
- Use CDN for context distribution

### 2. Compact vs. Expanded Forms

- Store transactions in compact form
- Expand on-demand for semantic queries
- Provide both formats via API

### 3. Indexing

- Index `@id` field for fast DID resolution
- Index `registerId` and `txId` separately
- Create composite index for common queries

## Migration Strategy

### Phase 1: Context Introduction (Week 1-2)
- Create `BlockchainContext.cs`
- Update `JsonLdContext.cs` to include blockchain context
- Deploy context to hosting endpoint

### Phase 2: Model Updates (Week 3-4)
- Add `TransactionReference` model
- Update `Transaction.cs` serialization
- Update `Action.cs` to use DID references

### Phase 3: API Updates (Week 5-6)
- Update Register Service APIs
- Implement DID resolution endpoints
- Add content negotiation support

### Phase 4: Migration and Testing (Week 7-8)
- Migrate existing transaction references
- Update all consumers
- Comprehensive integration testing

### Phase 5: Documentation and Deprecation (Week 9-10)
- Update API documentation
- Announce deprecation timeline for legacy format
- Monitor adoption metrics

## Compliance Checklist

All implementations MUST:

- [ ] Use the canonical JSON-LD context
- [ ] Generate valid DID URIs for all transactions
- [ ] Support DID resolution via API
- [ ] Include `@context`, `@type`, and `@id` in all transaction documents
- [ ] Maintain backward compatibility during migration
- [ ] Validate JSON-LD documents before storage
- [ ] Implement context caching
- [ ] Document API endpoints with OpenAPI
- [ ] Provide examples in API documentation
- [ ] Add integration tests for JSON-LD serialization

## Related Standards

### W3C Standards
- [JSON-LD 1.1](https://www.w3.org/TR/json-ld11/)
- [Decentralized Identifiers (DIDs) v1.0](https://www.w3.org/TR/did-core/)
- [Verifiable Credentials Data Model v1.1](https://www.w3.org/TR/vc-data-model/)

### Community Standards
- [Blockcerts](https://www.blockcerts.org/) - Blockchain certificate schemas
- [Universal Wallet Interop Spec](https://w3c-ccg.github.io/universal-wallet-interop-spec/)

### Sorcha Documentation
- [Architecture](architecture.md)
- [JSON-LD Implementation Summary](../guides/blueprints/json-ld-implementation-summary.md)
- [Blueprint Architecture](../guides/blueprints/blueprint-architecture.md)
- [Register Service Specification](../.specify/specs/sorcha-register-service.md)
- [Transaction Handler Specification](../.specify/specs/sorcha-transaction-handler.md)

## Examples

### Example 1: Simple Transaction Reference in Blueprint

```json
{
  "@context": [
    "https://sorcha.dev/blueprint/v1",
    "https://sorcha.dev/contexts/blockchain/v1.jsonld"
  ],
  "title": "Purchase Order Workflow",
  "actions": [
    {
      "id": 1,
      "title": "Create Order",
      "sender": "buyer",
      "previousTxId": null
    },
    {
      "id": 2,
      "title": "Accept Order",
      "sender": "seller",
      "previousTxId": "did:sorcha:register:550e8400-e29b-41d4-a716-446655440000/tx/abc123"
    }
  ]
}
```

### Example 2: Full Transaction with Verification

```json
{
  "@context": "https://sorcha.dev/contexts/blockchain/v1.jsonld",
  "@type": "Transaction",
  "@id": "did:sorcha:register:550e8400-e29b-41d4-a716-446655440000/tx/def456",

  "registerId": "550e8400-e29b-41d4-a716-446655440000",
  "txId": "def456",
  "previousTxHash": "abc123",
  "blockNumber": 42,
  "version": 4,
  "timestamp": "2025-11-13T14:30:00Z",

  "senderWallet": "ws1buyer123",
  "recipients": ["ws1seller456"],

  "proof": {
    "@type": "sec:Ed25519Signature2020",
    "created": "2025-11-13T14:30:00Z",
    "verificationMethod": "did:sorcha:wallet:ws1buyer123#key-1",
    "proofPurpose": "assertionMethod",
    "proofValue": "base64encodedsignature=="
  },

  "metadata": {
    "blueprintId": "purchase-order-v1",
    "actionId": 2,
    "type": "AcceptOrder"
  }
}
```

## Approval and Governance

### Approval Status

- [x] Architecture Team Review
- [x] Security Team Review
- [ ] Implementation Team Review
- [ ] Technical Documentation Complete

### Version History

| Version | Date | Changes | Author |
|---------|------|---------|--------|
| 1.0.0 | 2025-11-13 | Initial specification | Claude (Architectural Requirement) |

### Change Control

Changes to this specification require:
1. Architecture team approval
2. Impact assessment on existing implementations
3. Migration plan for breaking changes
4. Update to related documentation

---

**Document Control**
- **Created:** 2025-11-13
- **Authority:** Sorcha Architecture Team
- **Review Frequency:** Quarterly
- **Next Review:** 2026-02-13
