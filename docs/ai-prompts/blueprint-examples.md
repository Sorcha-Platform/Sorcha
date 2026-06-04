## Example Conversations for Blueprint Building

These examples show how an LLM should interact with users when helping them build Sorcha blueprints.

> **Model corrections** (these conversational examples predate the current schema). When generating a
> blueprint: use **`dataSchemas`** (an array) not `dataSchema`; route via **`routes[]`**
> (`nextActionIds` → action ids, JSON-Logic `condition`, `isDefault`) not a `routing`/`condition`
> block returning a participant; participants have **no `type`** field. The authoritative model is the
> `blueprint-builder` skill and [Blueprint Format](../guides/blueprints/blueprint-format.md).

---

### Example 1: Simple Purchase Order

**User:** "Create a purchase order workflow"

**Assistant:** "I'll create a purchase order workflow. Let me clarify a few details:

Who are the main participants? I'm thinking:
- **Buyer**: Creates and submits the purchase order
- **Supplier**: Reviews and accepts/rejects the order

What information should be collected in the purchase order? I suggest:
- Item description
- Quantity
- Unit price
- Total amount
- Delivery date
- Delivery address

Does this match what you need, or would you like any additions?"

**User:** "Yes, that sounds good. Also add a field for special instructions."

**Assistant:** "Perfect! Here's the blueprint I've created:

```json
{
  "title": "Purchase Order Workflow",
  "description": "Simple two-party workflow for creating and approving purchase orders between buyer and supplier",
  "participants": [
    {
      "id": "buyer",
      "name": "Buyer",
      "type": "person"
    },
    {
      "id": "supplier",
      "name": "Supplier",
      "type": "person"
    }
  ],
  "actions": [
    {
      "id": 0,
      "title": "Create Purchase Order",
      "description": "Buyer creates a new purchase order with item details",
      "sender": "buyer",
      "isStartingAction": true,
      "dataSchema": {
        "type": "object",
        "properties": {
          "itemDescription": {
            "type": "string",
            "title": "Item Description",
            "minLength": 5,
            "maxLength": 500
          },
          "quantity": {
            "type": "integer",
            "title": "Quantity",
            "minimum": 1
          },
          "unitPrice": {
            "type": "number",
            "title": "Unit Price ($)",
            "minimum": 0.01,
            "multipleOf": 0.01
          },
          "totalAmount": {
            "type": "number",
            "title": "Total Amount ($)",
            "minimum": 0.01,
            "multipleOf": 0.01
          },
          "deliveryDate": {
            "type": "string",
            "format": "date",
            "title": "Requested Delivery Date"
          },
          "deliveryAddress": {
            "type": "string",
            "title": "Delivery Address",
            "minLength": 10,
            "maxLength": 200
          },
          "specialInstructions": {
            "type": "string",
            "title": "Special Instructions",
            "maxLength": 1000
          }
        },
        "required": ["itemDescription", "quantity", "unitPrice", "totalAmount", "deliveryDate", "deliveryAddress"]
      }
    },
    {
      "id": 1,
      "title": "Review and Approve Order",
      "description": "Supplier reviews the purchase order and accepts or rejects",
      "sender": "supplier",
      "dataSchema": {
        "type": "object",
        "properties": {
          "status": {
            "type": "string",
            "title": "Decision",
            "enum": ["approved", "rejected"]
          },
          "supplierComments": {
            "type": "string",
            "title": "Supplier Comments",
            "maxLength": 500
          }
        },
        "required": ["status"]
      }
    }
  ]
}
```

This blueprint:
- Has 2 participants (buyer and supplier)
- Has 2 actions in sequence
- The buyer creates the order (starting action)
- The supplier then reviews and approves/rejects
- All required fields have appropriate validation
- Special instructions are optional (not in 'required' array)

Would you like to add any conditional routing (e.g., route to different suppliers based on amount) or privacy rules?"

---

### Example 2: Loan Application with Conditional Routing

**User:** "I need a loan application workflow where small loans go to one approver and large loans go to senior management"

**Assistant:** "Great! I'll create a loan application workflow with conditional routing. A few questions:

1. What's the threshold amount that distinguishes small vs. large loans?
2. What information do applicants need to provide?
3. Should approvers have access to all applicant information, or should some fields be private?"

**User:** "Loans under $50,000 go to standard approver, anything over goes to senior approver. Collect: applicant name, email, loan amount, purpose, employment info, and annual income."

**Assistant:** "Perfect! Here's the blueprint with conditional routing:

```json
{
  "title": "Loan Application Workflow",
  "description": "Loan application with automatic routing based on loan amount threshold",
  "participants": [
    {
      "id": "applicant",
      "name": "Loan Applicant",
      "type": "person"
    },
    {
      "id": "standard_approver",
      "name": "Standard Loan Approver",
      "type": "person"
    },
    {
      "id": "senior_approver",
      "name": "Senior Loan Approver",
      "type": "person"
    }
  ],
  "actions": [
    {
      "id": 0,
      "title": "Submit Loan Application",
      "description": "Applicant submits loan application with personal and financial details",
      "sender": "applicant",
      "isStartingAction": true,
      "dataSchema": {
        "type": "object",
        "properties": {
          "applicantName": {
            "type": "string",
            "title": "Full Name",
            "minLength": 2,
            "maxLength": 100
          },
          "email": {
            "type": "string",
            "title": "Email Address",
            "format": "email"
          },
          "loanAmount": {
            "type": "number",
            "title": "Loan Amount Requested ($)",
            "minimum": 1000,
            "maximum": 500000,
            "multipleOf": 100
          },
          "loanPurpose": {
            "type": "string",
            "title": "Purpose of Loan",
            "enum": ["home_purchase", "home_improvement", "business", "debt_consolidation", "other"]
          },
          "employer": {
            "type": "string",
            "title": "Current Employer",
            "minLength": 2,
            "maxLength": 100
          },
          "annualIncome": {
            "type": "number",
            "title": "Annual Income ($)",
            "minimum": 0,
            "multipleOf": 1000
          }
        },
        "required": ["applicantName", "email", "loanAmount", "loanPurpose", "employer", "annualIncome"]
      },
      "routing": {
        "conditions": [
          {
            "field": "/loanAmount",
            "operator": "greaterThan",
            "value": 50000,
            "routeTo": "senior_approver"
          }
        ],
        "defaultRoute": "standard_approver"
      }
    },
    {
      "id": 1,
      "title": "Standard Approval Review",
      "description": "Standard approver reviews loan applications under $50K",
      "sender": "standard_approver",
      "dataSchema": {
        "type": "object",
        "properties": {
          "decision": {
            "type": "string",
            "title": "Approval Decision",
            "enum": ["approved", "rejected", "needs_more_info"]
          },
          "approverComments": {
            "type": "string",
            "title": "Comments",
            "maxLength": 1000
          }
        },
        "required": ["decision"]
      }
    },
    {
      "id": 2,
      "title": "Senior Approval Review",
      "description": "Senior approver reviews loan applications $50K and above",
      "sender": "senior_approver",
      "dataSchema": {
        "type": "object",
        "properties": {
          "decision": {
            "type": "string",
            "title": "Approval Decision",
            "enum": ["approved", "rejected", "needs_more_info"]
          },
          "approverComments": {
            "type": "string",
            "title": "Comments",
            "maxLength": 1000
          }
        },
        "required": ["decision"]
      }
    }
  ]
}
```

Key features:
- **Conditional Routing**: Action 0 has routing logic that checks the loan amount
  - If loanAmount > $50,000 → Routes to senior_approver (action 2)
  - Otherwise → Routes to standard_approver (action 1)
- **Validation**: Loan amount constrained to $1,000 - $500,000 range
- **Enum Fields**: Loan purpose and decisions use dropdown options
- **Email Validation**: Email field has format validation

Would you like to add privacy rules so approvers can't see certain sensitive information like annual income?"

---

### Example 3: Document Review with Privacy Controls

**User:** "Create a document review workflow where multiple reviewers add comments, but each reviewer can only see their own comments, not others' comments"

**Assistant:** "Interesting privacy requirement! Let me clarify the workflow:

1. Who uploads the initial document - an author?
2. How many reviewers will there be?
3. After all reviews are done, who finalizes - the original author?
4. Should the author be able to see all reviewer comments?"

**User:** "Yes, author uploads, then 3 reviewers review independently, then author sees all comments and finalizes"

**Assistant:** "Perfect! This requires careful disclosure rules. Here's the blueprint:

```json
{
  "title": "Confidential Document Review",
  "description": "Multi-reviewer workflow with privacy controls ensuring reviewers cannot see each other's feedback",
  "participants": [
    {
      "id": "author",
      "name": "Document Author",
      "type": "person"
    },
    {
      "id": "reviewer1",
      "name": "Reviewer 1",
      "type": "person"
    },
    {
      "id": "reviewer2",
      "name": "Reviewer 2",
      "type": "person"
    },
    {
      "id": "reviewer3",
      "name": "Reviewer 3",
      "type": "person"
    }
  ],
  "actions": [
    {
      "id": 0,
      "title": "Upload Document for Review",
      "description": "Author uploads document and requests reviews",
      "sender": "author",
      "isStartingAction": true,
      "dataSchema": {
        "type": "object",
        "properties": {
          "documentTitle": {
            "type": "string",
            "title": "Document Title",
            "minLength": 3,
            "maxLength": 200
          },
          "documentFile": {
            "type": "string",
            "format": "binary",
            "title": "Upload Document"
          },
          "reviewDeadline": {
            "type": "string",
            "format": "date",
            "title": "Review Deadline"
          }
        },
        "required": ["documentTitle", "documentFile", "reviewDeadline"]
      },
      "disclosures": [
        {
          "participantAddress": "author",
          "dataPointers": ["/*"]
        },
        {
          "participantAddress": "reviewer1",
          "dataPointers": ["/documentTitle", "/documentFile", "/reviewDeadline"]
        },
        {
          "participantAddress": "reviewer2",
          "dataPointers": ["/documentTitle", "/documentFile", "/reviewDeadline"]
        },
        {
          "participantAddress": "reviewer3",
          "dataPointers": ["/documentTitle", "/documentFile", "/reviewDeadline"]
        }
      ]
    },
    {
      "id": 1,
      "title": "Reviewer 1 Feedback",
      "sender": "reviewer1",
      "dataSchema": {
        "type": "object",
        "properties": {
          "rating": {
            "type": "integer",
            "title": "Overall Rating",
            "minimum": 1,
            "maximum": 5
          },
          "comments": {
            "type": "string",
            "title": "Review Comments",
            "minLength": 10,
            "maxLength": 2000
          }
        },
        "required": ["rating", "comments"]
      },
      "disclosures": [
        {
          "participantAddress": "author",
          "dataPointers": ["/*"]
        },
        {
          "participantAddress": "reviewer1",
          "dataPointers": ["/*"]
        }
      ]
    },
    {
      "id": 2,
      "title": "Reviewer 2 Feedback",
      "sender": "reviewer2",
      "dataSchema": {
        "type": "object",
        "properties": {
          "rating": {
            "type": "integer",
            "title": "Overall Rating",
            "minimum": 1,
            "maximum": 5
          },
          "comments": {
            "type": "string",
            "title": "Review Comments",
            "minLength": 10,
            "maxLength": 2000
          }
        },
        "required": ["rating", "comments"]
      },
      "disclosures": [
        {
          "participantAddress": "author",
          "dataPointers": ["/*"]
        },
        {
          "participantAddress": "reviewer2",
          "dataPointers": ["/*"]
        }
      ]
    },
    {
      "id": 3,
      "title": "Reviewer 3 Feedback",
      "sender": "reviewer3",
      "dataSchema": {
        "type": "object",
        "properties": {
          "rating": {
            "type": "integer",
            "title": "Overall Rating",
            "minimum": 1,
            "maximum": 5
          },
          "comments": {
            "type": "string",
            "title": "Review Comments",
            "minLength": 10,
            "maxLength": 2000
          }
        },
        "required": ["rating", "comments"]
      },
      "disclosures": [
        {
          "participantAddress": "author",
          "dataPointers": ["/*"]
        },
        {
          "participantAddress": "reviewer3",
          "dataPointers": ["/*"]
        }
      ]
    },
    {
      "id": 4,
      "title": "Finalize Document",
      "description": "Author reviews all feedback and finalizes document",
      "sender": "author",
      "dataSchema": {
        "type": "object",
        "properties": {
          "finalDecision": {
            "type": "string",
            "title": "Final Decision",
            "enum": ["accepted", "rejected", "revise_and_resubmit"]
          },
          "authorResponse": {
            "type": "string",
            "title": "Response to Reviewers",
            "maxLength": 2000
          }
        },
        "required": ["finalDecision"]
      },
      "disclosures": [
        {
          "participantAddress": "author",
          "dataPointers": ["/*"]
        }
      ]
    }
  ]
}
```

**Privacy Implementation:**
- **Action 0** (Upload): All reviewers can see document details but NOT each other's existence
- **Actions 1-3** (Reviews): Each reviewer's feedback is disclosed ONLY to:
  - The author (who needs to see all feedback)
  - The reviewer themselves (who wrote it)
- **Action 4** (Finalize): Only author sees the final decision

This ensures reviewers work independently without being influenced by each other's opinions. The author gets all feedback aggregated at the end."

---

### Tips for Effective Blueprint Generation

**DO:**
- ✅ Ask clarifying questions before generating
- ✅ Explain your design decisions
- ✅ Provide sensible defaults for common workflows
- ✅ Validate participant IDs match sender references
- ✅ Use descriptive field names and titles
- ✅ Add appropriate validation constraints
- ✅ Mark at least one action as `isStartingAction: true`
- ✅ Suggest improvements (routing, privacy, validation)
- ✅ Show the complete JSON blueprint in your response

**DON'T:**
- ❌ Create blueprints with <2 participants
- ❌ Forget to mark a starting action
- ❌ Use undefined participant IDs in sender fields
- ❌ Start action IDs from anything other than 0
- ❌ Skip required fields like title, description, sender
- ❌ Create overly complex routing for simple workflows
- ❌ Add disclosures unless privacy is explicitly needed
- ❌ Use non-sequential action IDs (0, 1, 2, 3...)

---

## Usage

Copy the "System Prompt" section above and paste it into your LLM's system prompt field when you want it to help build Sorcha blueprints. The prompt includes:

1. **Context** about what Sorcha blueprints are
2. **Structure** with complete JSON examples
3. **Rules** for valid blueprints
4. **Patterns** for common workflow types
5. **Guidelines** for effective conversations
6. **Examples** showing proper interaction style

This will enable the LLM to create well-structured, validated blueprints that match Sorcha's requirements.
