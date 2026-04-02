# Data Model: Trade Finance Walkthrough

## config.json (Extended Walkthrough Manifest)

```json
{
  "name": "TradeFinance",
  "description": "SME Procurement-to-Pay with Invoice Finance",
  "category": "multi-org",
  "secretsKey": "trade-finance",
  "requiresRegister": true,
  "requiresParticipants": true,
  "organizations": [
    {
      "name": "Cairngorm Construction Ltd",
      "subdomain": "cairngorm",
      "role": "buyer",
      "participants": [
        { "id": "procurement-mgr", "displayName": "Procurement Manager", "algorithm": "ED25519" },
        { "id": "site-mgr", "displayName": "Site Manager", "algorithm": "ED25519" }
      ]
    },
    {
      "name": "Highland Timber Supplies",
      "subdomain": "highland-timber",
      "role": "supplier",
      "participants": [
        { "id": "sales-mgr", "displayName": "Sales Manager", "algorithm": "ED25519" },
        { "id": "finance-director", "displayName": "Finance Director", "algorithm": "ED25519" }
      ]
    },
    {
      "name": "ScotTrade Finance",
      "subdomain": "scottrade",
      "role": "funder",
      "participants": [
        { "id": "credit-analyst", "displayName": "Credit Analyst", "algorithm": "ED25519" }
      ]
    },
    {
      "name": "UK Trade Credit Bureau",
      "subdomain": "trade-credit",
      "role": "credit-insurer",
      "participants": [
        { "id": "assessment-svc", "displayName": "Assessment Service", "algorithm": "ED25519" }
      ]
    }
  ],
  "registers": [
    {
      "name": "SME Trade Register",
      "purpose": "Procurement-to-pay workflow between buyer and supplier",
      "ownerOrg": "cairngorm",
      "template": "procurement-to-pay-template.json"
    },
    {
      "name": "Trade Finance Register",
      "purpose": "Invoice financing between supplier, funder, and credit insurer",
      "ownerOrg": "scottrade",
      "template": "invoice-finance-template.json"
    }
  ],
  "templates": [
    "procurement-to-pay-template.json",
    "invoice-finance-template.json"
  ],
  "scenarios": [
    "data/scenario-golden-path.json",
    "data/scenario-disputed.json",
    "data/scenario-declined.json"
  ],
  "agentAssignments": {
    "box1": {
      "label": "Buyer Side",
      "organizations": ["cairngorm", "trade-credit"],
      "participants": ["procurement-mgr", "site-mgr", "assessment-svc"]
    },
    "box2": {
      "label": "Supplier/Funder Side",
      "organizations": ["highland-timber", "scottrade"],
      "participants": ["sales-mgr", "finance-director", "credit-analyst"]
    }
  }
}
```

## state.json (Setup Output)

Written by setup.ps1 or the setup wizard after bootstrapping.

```json
{
  "profile": "gateway",
  "gatewayUrl": "https://n1.sorcha.dev",
  "adminEmail": "admin@sorcha.dev",
  "adminPassword": "...",
  "organizations": {
    "cairngorm": "<org-id>",
    "highland-timber": "<org-id>",
    "scottrade": "<org-id>",
    "trade-credit": "<org-id>"
  },
  "registers": {
    "trade": { "id": "<register-id>", "name": "SME Trade Register" },
    "finance": { "id": "<register-id>", "name": "Trade Finance Register" }
  },
  "blueprints": {
    "procurement": { "id": "<blueprint-id>", "registerId": "<register-id>" },
    "finance": { "id": "<blueprint-id>", "registerId": "<register-id>" }
  },
  "wallets": {
    "procurement-mgr": "<wallet-address>",
    "site-mgr": "<wallet-address>",
    "sales-mgr": "<wallet-address>",
    "finance-director": "<wallet-address>",
    "credit-analyst": "<wallet-address>",
    "assessment-svc": "<wallet-address>"
  },
  "roles": {
    "procurement-mgr": {
      "organizationId": "<org-id>",
      "walletAddress": "<addr>",
      "participantId": "<participant-id>",
      "orgKey": "cairngorm",
      "email": "procurement-mgr@cairngorm.sorcha.dev",
      "password": "..."
    }
  }
}
```

## Scenario Data (e.g., scenario-golden-path.json)

```json
{
  "name": "Scenario A: Golden Path",
  "description": "Full procurement-to-pay with approved invoice financing",
  "expectedProcurementPath": [1, 2, 3, 4, 5, 6],
  "expectedFinancePath": [1, 2, 3, 4],
  "expectedInvoiceTotal": 47500.00,
  "expectedDaysSinceDelivery": 3,
  "expectedAdvanceAmount": 42750.00,
  "expectedFeeAmount": 1068.75,
  "expectedNetAdvance": 41681.25,
  "expectedRejection": false,
  "procurement": {
    "1": {
      "poReference": "PO-CAIRN-2026-00142",
      "projectName": "Aviemore Heights Phase 2",
      "siteAddress": "Plot 14-18, Craig na Gower Road, Aviemore PH22 1RN",
      "lineItems": [
        { "description": "Treated Structural Timber 47x200mm", "quantity": 500, "unit": "linear metre", "unitPrice": 8.50 },
        { "description": "OSB Sheathing Board 18mm", "quantity": 120, "unit": "sheet", "unitPrice": 32.00 },
        { "description": "Timber Connectors Assorted", "quantity": 10, "unit": "box", "unitPrice": 45.00 }
      ],
      "deliveryAddress": "Site Compound, Craig na Gower Road, Aviemore PH22 1RN",
      "paymentTerms": "Net 30",
      "requiredDeliveryDate": "2026-04-15"
    },
    "2": {
      "accepted": true,
      "estimatedDeliveryDate": "2026-04-14",
      "orderConfirmationRef": "HTS-ACK-2026-0892",
      "notes": "Stock confirmed. Delivery by Highland Haulage."
    },
    "3": {
      "deliveryNoteRef": "HTS-DN-2026-1204",
      "actualDeliveryDate": "2026-04-14",
      "deliveredItems": [
        { "description": "Treated Structural Timber 47x200mm", "quantityDelivered": 500 },
        { "description": "OSB Sheathing Board 18mm", "quantityDelivered": 120 },
        { "description": "Timber Connectors Assorted", "quantityDelivered": 10 }
      ],
      "deliveryCondition": "Good — no damage noted"
    },
    "4": {
      "grnReference": "CAIRN-GRN-2026-00418",
      "receivedDate": "2026-04-14",
      "conditionNotes": "All items received in good condition, quantities verified against DN",
      "discrepancyFlag": false
    },
    "5": {
      "invoiceNumber": "HTS-INV-2026-03847",
      "invoiceDate": "2026-04-17",
      "lineItems": [
        { "description": "Treated Structural Timber 47x200mm", "quantity": 500, "unitPrice": 8.50, "lineTotal": 4250.00 },
        { "description": "OSB Sheathing Board 18mm", "quantity": 120, "unitPrice": 32.00, "lineTotal": 3840.00 },
        { "description": "Timber Connectors Assorted", "quantity": 10, "unitPrice": 45.00, "lineTotal": 450.00 }
      ],
      "subtotal": 8540.00,
      "vatRate": 0.20,
      "vatAmount": 1708.00,
      "invoiceTotal": 10248.00,
      "paymentTerms": "Net 30",
      "paymentDueDate": "2026-05-17",
      "supplierCostBreakdown": {
        "materialCost": 6200.00,
        "logistics": 480.00,
        "margin": 1860.00,
        "marginPercentage": 21.8
      }
    },
    "6": {
      "decision": "approve",
      "approvalNotes": "Invoice matches PO and GRN. Approved for payment.",
      "approvedAmount": 10248.00
    }
  },
  "finance": {
    "1": {
      "invoiceReference": "HTS-INV-2026-03847",
      "invoiceAmount": 10248.00,
      "buyerName": "Cairngorm Construction Ltd",
      "requestedAdvancePercentage": 90,
      "urgency": "standard"
    },
    "2": {
      "buyerCreditScore": 85,
      "creditLimit": 250000.00,
      "riskRating": "low",
      "assessmentDate": "2026-04-17",
      "paymentHistoryScore": 92,
      "yearsTrading": 14,
      "assessmentNotes": "Strong payment history. No defaults. Established Highland contractor."
    },
    "3": {
      "evaluationNotes": "Verified invoice credential confirmed. Buyer credit score 85/100 — well above threshold. Invoice amount within credit limit.",
      "advancePercentage": 90,
      "feeRate": 2.5
    },
    "4": {
      "decision": "approve",
      "advanceAmount": 9223.20,
      "feeAmount": 230.58,
      "netAdvance": 8992.62,
      "repaymentTerms": "Net 30 from original invoice due date",
      "repaymentDate": "2026-05-17",
      "financingReference": "STF-FIN-2026-00291"
    }
  }
}
```

## credit-scores.json (Credit Insurer Lookup Data)

```json
{
  "description": "Scripted buyer credit data for the UK Trade Credit Bureau Assessment Service",
  "buyers": {
    "cairngorm": {
      "buyerName": "Cairngorm Construction Ltd",
      "creditScore": 85,
      "creditLimit": 250000.00,
      "riskRating": "low",
      "paymentHistoryScore": 92,
      "yearsTrading": 14,
      "notes": "Strong payment history. No defaults. Established Highland contractor."
    },
    "lowcredit": {
      "buyerName": "Lochside Developments Ltd",
      "creditScore": 35,
      "creditLimit": 25000.00,
      "riskRating": "high",
      "paymentHistoryScore": 41,
      "yearsTrading": 2,
      "notes": "Recent CCJ. Multiple late payments. Limited trading history."
    }
  }
}
```

## MCP Config Template (mcp-configs/template.json)

```json
{
  "mcpServers": {
    "sorcha-PARTICIPANT_ID": {
      "command": "dotnet",
      "args": ["run", "--project", "src/Apps/Sorcha.McpServer", "--", "--jwt-token", "JWT_TOKEN_PLACEHOLDER"],
      "env": {
        "SORCHA_GATEWAY_URL": "GATEWAY_URL_PLACEHOLDER"
      }
    }
  }
}
```

The setup wizard replaces `PARTICIPANT_ID`, `JWT_TOKEN_PLACEHOLDER`, and `GATEWAY_URL_PLACEHOLDER` for each participant, then merges the configs into the operator's Claude Code settings.

## Blueprint Data Schemas

### Procurement-to-Pay Blueprint — Action Field Summary

| Action | Key Fields | Calculated | Disclosed To |
|--------|-----------|------------|--------------|
| 1. Raise PO | poReference, projectName, siteAddress, lineItems[], deliveryAddress, paymentTerms, requiredDeliveryDate | — | Buyer(all), Supplier(all), Funder(paymentTerms only) |
| 2. Acknowledge PO | accepted, estimatedDeliveryDate, orderConfirmationRef, notes | — | Buyer(all), Supplier(all) |
| 3. Confirm Delivery | deliveryNoteRef, actualDeliveryDate, deliveredItems[], deliveryCondition | — | Buyer(all), Supplier(all) |
| 4. Confirm GRN | grnReference, receivedDate, conditionNotes, discrepancyFlag | — | Buyer(all), Supplier(all) |
| 5. Raise Invoice | invoiceNumber, invoiceDate, lineItems[], subtotal, vatRate, vatAmount, invoiceTotal, paymentTerms, paymentDueDate, supplierCostBreakdown | invoiceTotal (sum of lineItems), daysSinceDelivery | Buyer(all except supplierCostBreakdown), Supplier(all), Funder(invoiceTotal, paymentTerms) |
| 6. Approve Invoice | decision, approvalNotes, approvedAmount | — | Buyer(all), Supplier(all), Funder(decision, approvedAmount) |

### Invoice Finance Blueprint — Action Field Summary

| Action | Key Fields | Calculated | Disclosed To |
|--------|-----------|------------|--------------|
| 1. Request Financing | invoiceReference, invoiceAmount, buyerName, requestedAdvancePercentage, urgency | — | Supplier(all), Funder(all) |
| 2. Buyer Assessment | buyerCreditScore, creditLimit, riskRating, assessmentDate, paymentHistoryScore, yearsTrading, assessmentNotes | — | Funder(all), Credit Insurer(all) |
| 3. Evaluate Application | evaluationNotes, advancePercentage, feeRate | advanceAmount, feeAmount, netAdvance | Supplier(advancePercentage, feeRate), Funder(all) |
| 4. Approve/Decline | decision, advanceAmount, feeAmount, netAdvance, repaymentTerms, repaymentDate, financingReference | — | Supplier(all except evaluationNotes), Funder(all) |
