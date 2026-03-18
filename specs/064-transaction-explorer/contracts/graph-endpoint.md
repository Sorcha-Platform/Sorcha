# API Contract: Transaction Graph Endpoint

**Service**: Register Service
**Base Path**: `/api/registers/{registerId}/transactions`

## GET /graph

Returns a lightweight projection of all transactions in a register, containing only the fields needed to build a DAG visualization. Omits payload data, signatures, and challenges to minimize response size.

### Request

**Path Parameters**:
| Parameter | Type | Required | Description |
|-----------|------|----------|-------------|
| registerId | string | yes | Register ID |

**Query Parameters**:
| Parameter | Type | Default | Description |
|-----------|------|---------|-------------|
| limit | int | 200 | Maximum nodes to return (most recent first) |
| before | string | null | Return transactions with timestamp before this TxId (cursor-based pagination for "load more") |

### Response (200 OK)

```json
{
  "registerId": "abc-123",
  "nodes": [
    {
      "txId": "a1b2c3d4...",
      "prevTxId": "0000000000000000000000000000000000000000000000000000000000000000",
      "senderWallet": "ws1abc...",
      "timeStamp": "2026-03-18T14:22:00Z",
      "docketNumber": 42,
      "blueprintId": "bp-456",
      "instanceId": "inst-789",
      "transactionType": 1
    }
  ],
  "totalCount": 350,
  "hasMore": true
}
```

### Response Fields

| Field | Type | Description |
|-------|------|-------------|
| registerId | string | Echo of request parameter |
| nodes | TransactionGraphNodeDto[] | Lightweight transaction projections |
| totalCount | int | Total transaction count in register |
| hasMore | bool | Whether more nodes exist before the oldest returned |

### TransactionGraphNodeDto

| Field | Type | Nullable | Description |
|-------|------|----------|-------------|
| txId | string | no | 64-char hex transaction ID |
| prevTxId | string | no | 64-char hex previous TX ID (all-zeros for genesis) |
| senderWallet | string | no | Base58 sender wallet address |
| timeStamp | DateTime | no | UTC timestamp |
| docketNumber | ulong | yes | Docket number if confirmed |
| blueprintId | string | yes | Blueprint ID from metadata |
| instanceId | string | yes | Workflow instance ID from metadata |
| transactionType | int | yes | TransactionType enum value |

### Errors

| Status | When |
|--------|------|
| 404 | Register not found |
| 400 | Invalid `limit` (must be 1-1000) |

### Size Estimate

Each node is ~200 bytes JSON. For 200 nodes: ~40KB response.
Compare with full transaction list (200 items with payloads): 2-5MB.

### OpenAPI Metadata

```
.WithName("GetTransactionGraph")
.WithSummary("Get lightweight transaction graph for DAG visualization")
.WithDescription("Returns transaction IDs and PrevTxId links without payload data. Used by the Register Map UI for building the transaction lineage DAG.")
.WithTags("Query")
```
