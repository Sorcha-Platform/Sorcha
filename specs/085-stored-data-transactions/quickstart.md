# Quickstart: Stored Data Transactions

**Feature**: 085-stored-data-transactions

## Prerequisites

- Docker Desktop running with all services: `docker-compose up -d`
- .NET 10 SDK installed
- A published blueprint with at least one file field

## 1. Create a Blueprint with a File Field

Add a file field to a blueprint action's dataSchema:

```json
{
  "id": "inspection-blueprint",
  "title": "Site Inspection",
  "description": "Site inspection with photo evidence",
  "version": 1,
  "participants": [
    { "id": "inspector", "name": "Inspector", "description": "Performs the inspection" },
    { "id": "reviewer", "name": "Reviewer", "description": "Reviews the inspection" }
  ],
  "actions": [
    {
      "id": 0,
      "title": "Submit Inspection",
      "sender": "inspector",
      "isStartingAction": true,
      "dataSchemas": [
        {
          "type": "object",
          "properties": {
            "notes": { "type": "string", "minLength": 1 },
            "sitePhotos": {
              "type": "array",
              "items": { "type": "string", "format": "file-reference" },
              "minItems": 1,
              "maxItems": 5,
              "x-file": {
                "accept": ["image/jpeg", "image/png"],
                "maxSizePerFile": "16MB",
                "maxChunks": 10
              }
            }
          },
          "required": ["notes", "sitePhotos"]
        }
      ],
      "routes": [
        { "id": "to-review", "nextActionIds": [1], "isDefault": true }
      ]
    },
    {
      "id": 1,
      "title": "Review Inspection",
      "sender": "reviewer",
      "dataSchemas": [
        {
          "type": "object",
          "properties": {
            "decision": { "type": "string", "enum": ["approved", "rejected"] },
            "comments": { "type": "string" }
          },
          "required": ["decision"]
        }
      ],
      "routes": []
    }
  ]
}
```

## 2. Upload a File Attachment (API)

### Step 1: Submit file chunks

For each chunk of the file (≤4MB each):

```bash
curl -X POST http://localhost:80/api/file-chunks \
  -H "Authorization: Bearer $TOKEN" \
  -H "Content-Type: application/json" \
  -d '{
    "senderWallet": "ws1q...",
    "registerAddress": "reg-id",
    "chunkIndex": 0,
    "totalChunks": 1,
    "fileHash": "sha256:abc123...",
    "contentType": "image/jpeg",
    "encryptedPayload": "<base64-encrypted-chunk>"
  }'
```

Response: `{ "chunkTransactionId": "tx-chunk-001", "chunkIndex": 0 }`

### Step 2: Submit the action with file reference

```bash
curl -X POST http://localhost:80/api/instances/{instanceId}/actions/0/execute \
  -H "Authorization: Bearer $TOKEN" \
  -H "Content-Type: application/json" \
  -d '{
    "blueprintId": "inspection-blueprint",
    "actionId": "0",
    "senderWallet": "ws1q...",
    "registerAddress": "reg-id",
    "payloadData": {
      "notes": "Foundation inspection complete",
      "sitePhotos": [
        {
          "fileName": "foundation-east.jpg",
          "contentType": "image/jpeg",
          "size": 2048576,
          "hash": "sha256:abc123...",
          "salt": "<base64-salt>",
          "chunkTransactionIds": ["tx-chunk-001"],
          "masterKeyId": "<action-tx-id>"
        }
      ]
    }
  }'
```

## 3. Download a File Attachment

```bash
curl -X GET "http://localhost:80/api/wallets/ws1q.../files/download?actionTxId=tx-action-001&fieldName=sitePhotos&fileIndex=0" \
  -H "Authorization: Bearer $TOKEN" \
  -o downloaded-photo.jpg
```

The Wallet Service fetches chunks, decrypts, reassembles, verifies integrity, and streams the file.

## 4. Use the UI

1. Navigate to the workflow instance in Sorcha UI
2. On a file field, click "Choose File" or "Take Photo" (mobile)
3. Watch the upload progress bar
4. Submit the action once all files are uploaded
5. On the completed action view, click the download link next to any file attachment

## Key Limits

| Parameter | Value |
|-----------|-------|
| Max chunk size | 4MB |
| Max chunks per file | 10 |
| Max total file size | 40MB |
| Orphan chunk timeout | 30 minutes |
