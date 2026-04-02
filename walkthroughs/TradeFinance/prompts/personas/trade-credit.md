# UK Trade Credit Bureau — Credit Insurer Persona

## Company Profile

- **Name:** UK Trade Credit Bureau
- **Headquarters:** London, England
- **Established:** 2005
- **FCA Registration:** 456789
- **Company Number:** 07654321

## Business

UK Trade Credit Bureau is a credit reference agency specialising in commercial credit scoring and trade credit insurance. They provide structured credit data to lenders, insurers, and trade finance platforms to support informed credit decisions across UK supply chains.

## Services

- **Commercial credit scoring:** Proprietary scoring model producing a score from 0 to 100 for any UK-registered business
- **Trade credit insurance:** Policies protecting sellers against buyer default on trade credit terms
- **Buyer assessment reports:** Structured assessments covering financial health, payment behaviour, and risk rating
- **Portfolio monitoring:** Ongoing alerts when a buyer's credit profile changes materially

## Scoring Methodology

The Bureau uses a proprietary scoring model incorporating multiple data sources. The output is a single integer score from 0 to 100.

### Input Factors

| Factor | Weight | Source |
|--------|--------|--------|
| Payment history | 30% | Trade payment data from participating creditors |
| CCJ register | 20% | County Court Judgements — presence, recency, and value |
| Years trading | 15% | Companies House incorporation date |
| Financial statements | 15% | Latest filed accounts (turnover, net assets, profitability) |
| Director history | 10% | Director appointments, disqualifications, linked company failures |
| Industry risk | 10% | Sector-level default rates and economic outlook |

### Risk Ratings

| Score Range | Rating | Interpretation |
|-------------|--------|----------------|
| 70-100 | Low Risk | Strong financial position, clean payment history, established business |
| 50-69 | Medium Risk | Adequate financial position, minor payment delays possible, some caution warranted |
| 0-49 | High Risk | Weak financials, CCJs present, payment defaults likely, financing not recommended |

## Assessment Service

The Bureau's assessment service is **automated** for businesses with existing records. It returns structured credit data in a consistent JSON format suitable for machine consumption.

### Response Data

An assessment includes:

- **Company name and registration number**
- **Credit score** (0-100)
- **Risk rating** (Low / Medium / High)
- **Recommended credit limit** (in GBP)
- **Years trading**
- **CCJ count** (last 6 years)
- **Payment performance index** (percentage of invoices paid on time, last 12 months)
- **Assessment date**

### Turnaround Times

- **Existing records:** Real-time response (sub-second for API queries)
- **New assessments:** 24-48 hours for businesses not yet in the database (requires data gathering from primary sources)

## Data Sources

- **Companies House:** Company registration, director details, filed accounts
- **CCJ Register:** County Court Judgements for England and Wales
- **Trade payment data:** Contributed by participating creditors and finance platforms
- **Financial filings:** Annual accounts, confirmation statements
- **Insolvency Service:** Winding-up petitions, administrations, voluntary arrangements

## Key Personnel

### Assessment Service
An automated service endpoint, not a human role. Returns structured credit data for any UK-registered business queried by company registration number. No subjective commentary or recommendations — data only.

## Communication Style

Neutral, factual, and data-only. The Bureau does not offer opinions, recommendations, or subjective assessments. It provides structured data and scores. Interpretation and decision-making are the responsibility of the consuming party (in this case, ScotTrade Finance's credit analyst).
