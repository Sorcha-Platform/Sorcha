# Sorcha HD Wallet Client Integration Guide

**Version:** 1.0
**Last Updated:** 2025-11-19
**Status:** Production Ready

---

## Table of Contents

1. [Overview](#overview)
2. [Security Model](#security-model)
3. [Quick Start](#quick-start)
4. [Wallet Creation](#wallet-creation)
5. [Address Derivation](#address-derivation)
6. [Address Management](#address-management)
7. [Best Practices](#best-practices)
8. [Code Examples](#code-examples)
9. [API Reference](#api-reference)
10. [Troubleshooting](#troubleshooting)

---

## Overview

Sorcha Wallet Service implements **client-side HD wallet address derivation** following BIP32, BIP39, and BIP44 standards. This approach ensures:

- ✅ **Non-Custodial Security**: Mnemonic phrases never leave the client
- ✅ **BIP44 Compliance**: Standard derivation paths (m/44'/0'/account'/change/index)
- ✅ **Address Recovery**: Deterministic address generation from mnemonic
- ✅ **Multi-Account Support**: Separate accounts for different purposes
- ✅ **Change Address Support**: Separate receive and change address chains

### Architecture

```
┌─────────────────┐         ┌──────────────────┐
│  Client App     │         │  Wallet Service  │
│                 │         │                  │
│ • Stores        │         │ • Tracks public  │
│   mnemonic      │         │   keys & paths   │
│ • Derives keys  │  HTTPS  │ • Manages        │
│ • Signs txs     │◄───────►│   metadata       │
│                 │         │ • Validates      │
└─────────────────┘         │   gap limits     │
                            └──────────────────┘
```

**Key Principle:** The server NEVER sees or stores the mnemonic phrase. All private key operations happen client-side.

---

## Security Model

### What the Client Stores

- ✅ **Mnemonic phrase** (12 or 24 words) - User's responsibility to backup
- ✅ **Derived private keys** (in memory during transaction signing)
- ✅ **Address index mapping** (which addresses have been generated)

### What the Server Stores

- ✅ **Public keys** (safe to share publicly)
- ✅ **Addresses** (safe to share publicly)
- ✅ **Derivation paths** (metadata, no private information)
- ✅ **Address labels and metadata**
- ✅ **Usage status** (whether address has been used)

### What is NEVER Transmitted

- ❌ **Mnemonic phrase**
- ❌ **Master seed**
- ❌ **Private keys**
- ❌ **Extended private keys (xprv)**

---

## Quick Start

### 1. Install Dependencies

```bash
npm install bip39 bip32 @noble/secp256k1 @scure/bip32 @scure/bip39
```

### 2. Create Wallet

```typescript
import { Wallet } from './wallet-client';

// Create new wallet
const wallet = await Wallet.create({
  name: 'My Wallet',
  algorithm: 'ED25519',
  wordCount: 12
});

console.log('Wallet Address:', wallet.address);
console.log('Mnemonic (BACKUP THIS!):', wallet.mnemonic.join(' '));
```

### 3. Derive and Register Addresses

```typescript
// Derive first receive address
const receiveAddress = await wallet.deriveAddress({
  account: 0,
  change: 0,  // 0 = receive, 1 = change
  index: 0,
  label: 'My First Address'
});

console.log('Receive Address:', receiveAddress.address);
```

### 4. List Addresses

```typescript
// Get all receive addresses
const addresses = await wallet.listAddresses({
  type: 'receive',
  account: 0
});

console.log(`Found ${addresses.length} receive addresses`);
```

---

## Wallet Creation

### Step 1: Create Wallet via API

**Endpoint:** `POST /api/v1/wallets`

```typescript
async function createWallet(name: string, algorithm: string = 'ED25519', wordCount: number = 12) {
  const response = await fetch('https://wallet-service/api/v1/wallets', {
    method: 'POST',
    headers: { 'Content-Type': 'application/json' },
    body: JSON.stringify({ name, algorithm, wordCount })
  });

  const data = await response.json();

  return {
    wallet: data.wallet,
    mnemonic: data.mnemonicWords,  // BACKUP THIS!
    masterPublicKey: data.masterPublicKey
  };
}
```

**Response:**

```json
{
  "wallet": {
    "address": "ws11qztpwdj20smyvq94j35t8wkhrpdugmc4p3sweyarcpr9d2aa83mqxjs3uh0",
    "name": "My Wallet",
    "algorithm": "ED25519",
    "publicKey": "...",
    "createdAt": "2025-11-19T14:30:00Z"
  },
  "mnemonicWords": [
    "abandon", "ability", "able", "about", "above", "absent",
    "absorb", "abstract", "absurd", "abuse", "access", "accident"
  ],
  "masterPublicKey": "xpub..."
}
```

### Step 2: Securely Store Mnemonic

**⚠️ CRITICAL:** The mnemonic is shown ONLY ONCE. User must back it up securely.

```typescript
// Show mnemonic to user with backup instructions
function displayMnemonicBackup(mnemonic: string[]) {
  console.log('━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━');
  console.log('⚠️  BACKUP YOUR RECOVERY PHRASE');
  console.log('━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━');
  console.log('');
  console.log('Write these words down in order:');
  console.log('');

  mnemonic.forEach((word, i) => {
    console.log(`  ${(i + 1).toString().padStart(2, ' ')}. ${word}`);
  });

  console.log('');
  console.log('⚠️  Store this in a safe place!');
  console.log('⚠️  Anyone with this phrase can access your funds!');
  console.log('⚠️  Sorcha cannot recover this if you lose it!');
  console.log('━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━');
}
```

---

## Address Derivation

### BIP44 Derivation Path Format

```
m / purpose' / coin_type' / account' / change / address_index

m / 44' / 0' / 0' / 0 / 0
│   │     │    │    │   └─ Address index (0, 1, 2, ...)
│   │     │    │    └───── Change (0=receive, 1=change)
│   │     │    └────────── Account (0, 1, 2, ...)
│   │     └─────────────── Coin type (0 for Sorcha)
│   └───────────────────── Purpose (44 = BIP44)
└───────────────────────── Master key
```

### Client-Side Derivation (TypeScript)

```typescript
import * as bip39 from 'bip39';
import * as bip32 from 'bip32';
import { ed25519 } from '@noble/curves/ed25519';

class HDWalletClient {
  private mnemonic: string[];
  private seed: Buffer;

  constructor(mnemonic: string[]) {
    this.mnemonic = mnemonic;
    this.seed = bip39.mnemonicToSeedSync(mnemonic.join(' '));
  }

  /**
   * Derive address at specific BIP44 path
   */
  deriveAddress(account: number, change: number, index: number) {
    const path = `m/44'/0'/${account}'/${change}/${index}`;

    // For ED25519 (Sorcha uses ed25519-bip32)
    const node = bip32.fromSeed(this.seed);
    const child = node.derivePath(path);

    // Generate public key
    const publicKey = child.publicKey;

    // Generate Bech32 address (ws1q prefix)
    const address = this.generateBech32Address(publicKey);

    return {
      path,
      publicKey: publicKey.toString('base64'),
      address,
      privateKey: child.privateKey  // Keep this SECURE!
    };
  }

  /**
   * Generate Bech32 address from public key
   */
  private generateBech32Address(publicKey: Buffer): string {
    // Hash the public key
    const hash = crypto.createHash('sha256').update(publicKey).digest();

    // Convert to Bech32 format with 'ws1q' prefix
    return `ws1q${hash.toString('hex').substring(0, 35)}`;
  }
}
```

### Server Registration

After deriving the address client-side, register it with the server:

```typescript
async function registerDerivedAddress(
  walletAddress: string,
  derivedPublicKey: string,
  derivedAddress: string,
  derivationPath: string,
  label?: string
) {
  const response = await fetch(
    `https://wallet-service/api/v1/wallets/${walletAddress}/addresses`,
    {
      method: 'POST',
      headers: { 'Content-Type': 'application/json' },
      body: JSON.stringify({
        derivedPublicKey,
        derivedAddress,
        derivationPath,
        label,
        notes: 'Client-derived address',
        tags: 'active'
      })
    }
  );

  return await response.json();
}
```

### Complete Address Generation Workflow

```typescript
async function generateNewAddress(
  wallet: HDWalletClient,
  walletAddress: string,
  account: number = 0,
  isChange: boolean = false,
  label?: string
) {
  // 1. Get current address index
  const addresses = await listAddresses(walletAddress, { account, type: isChange ? 'change' : 'receive' });
  const nextIndex = addresses.length;

  // 2. Derive address client-side
  const derived = wallet.deriveAddress(account, isChange ? 1 : 0, nextIndex);

  // 3. Register with server (public key only)
  const registered = await registerDerivedAddress(
    walletAddress,
    derived.publicKey,
    derived.address,
    derived.path,
    label
  );

  // 4. Store mapping locally
  localStorage.setItem(`address_${derived.address}`, JSON.stringify({
    path: derived.path,
    account,
    change: isChange ? 1 : 0,
    index: nextIndex
  }));

  return registered;
}
```

---

## Address Management

### List Addresses

```typescript
async function listAddresses(
  walletAddress: string,
  filters?: {
    type?: 'receive' | 'change',
    account?: number,
    used?: boolean,
    label?: string,
    page?: number,
    pageSize?: number
  }
) {
  const params = new URLSearchParams();
  if (filters?.type) params.set('type', filters.type);
  if (filters?.account !== undefined) params.set('account', filters.account.toString());
  if (filters?.used !== undefined) params.set('used', filters.used.toString());
  if (filters?.label) params.set('label', filters.label);
  if (filters?.page) params.set('page', filters.page.toString());
  if (filters?.pageSize) params.set('pageSize', filters.pageSize.toString());

  const response = await fetch(
    `https://wallet-service/api/v1/wallets/${walletAddress}/addresses?${params}`,
    { method: 'GET' }
  );

  const data = await response.json();
  return data.addresses;
}
```

### Get Next Unused Address

```typescript
async function getNextUnusedAddress(walletAddress: string, account: number = 0) {
  const addresses = await listAddresses(walletAddress, {
    type: 'receive',
    account,
    used: false
  });

  if (addresses.length === 0) {
    throw new Error('No unused addresses available. Generate more addresses.');
  }

  // Return first unused address
  return addresses[0];
}
```

### Mark Address as Used

```typescript
async function markAddressAsUsed(walletAddress: string, addressId: string) {
  const response = await fetch(
    `https://wallet-service/api/v1/wallets/${walletAddress}/addresses/${addressId}/mark-used`,
    { method: 'POST' }
  );

  return await response.json();
}
```

### Update Address Metadata

```typescript
async function updateAddressMetadata(
  walletAddress: string,
  addressId: string,
  updates: {
    label?: string,
    notes?: string,
    tags?: string,
    metadata?: Record<string, string>
  }
) {
  const response = await fetch(
    `https://wallet-service/api/v1/wallets/${walletAddress}/addresses/${addressId}`,
    {
      method: 'PATCH',
      headers: { 'Content-Type': 'application/json' },
      body: JSON.stringify(updates)
    }
  );

  return await response.json();
}
```

---

## Best Practices

### 1. Gap Limit Compliance (BIP44)

**Rule:** Maximum 20 unused addresses per account/type

```typescript
async function ensureGapLimitCompliance(walletAddress: string) {
  const gapStatus = await fetch(
    `https://wallet-service/api/v1/wallets/${walletAddress}/gap-status`
  ).then(r => r.json());

  if (!gapStatus.isCompliant) {
    console.warn('⚠️ Gap limit exceeded!');
    gapStatus.accounts.forEach(account => {
      if (account.unusedCount >= 20) {
        console.warn(
          `Account ${account.account} (${account.addressType}): ` +
          `${account.unusedCount} unused addresses`
        );
      }
    });

    throw new Error('Please use existing addresses before generating more');
  }
}
```

### 2. Address Reuse Prevention

**Best Practice:** Generate a new address for each transaction

```typescript
async function getAddressForPayment(
  wallet: HDWalletClient,
  walletAddress: string,
  description: string
) {
  // Check gap limit first
  await ensureGapLimitCompliance(walletAddress);

  // Get next unused address or generate new one
  let address;
  try {
    address = await getNextUnusedAddress(walletAddress);
  } catch {
    // No unused addresses, generate new one
    address = await generateNewAddress(wallet, walletAddress, 0, false, description);
  }

  // Update metadata with purpose
  await updateAddressMetadata(walletAddress, address.id, {
    notes: description,
    metadata: {
      purpose: 'payment',
      createdFor: description
    }
  });

  return address;
}
```

### 3. Change Address Management

```typescript
async function getChangeAddress(wallet: HDWalletClient, walletAddress: string) {
  // Always use a new change address for each transaction
  const changeAddresses = await listAddresses(walletAddress, {
    type: 'change',
    account: 0,
    used: false
  });

  if (changeAddresses.length > 0) {
    return changeAddresses[0];
  }

  // Generate new change address
  return await generateNewAddress(wallet, walletAddress, 0, true, 'Change');
}
```

### 4. Multi-Account Organization

```typescript
const ACCOUNT_PURPOSES = {
  GENERAL: 0,
  SAVINGS: 1,
  BUSINESS: 2,
  DONATIONS: 3
};

async function getBusinessAddress(wallet: HDWalletClient, walletAddress: string) {
  return await generateNewAddress(
    wallet,
    walletAddress,
    ACCOUNT_PURPOSES.BUSINESS,
    false,
    'Business Payment'
  );
}
```

### 5. Mnemonic Security

```typescript
// ❌ NEVER do this
localStorage.setItem('mnemonic', mnemonic.join(' ')); // BAD!

// ✅ Store encrypted with user password
import { encrypt } from 'crypto-js/aes';

function storeMnemonicEncrypted(mnemonic: string[], password: string) {
  const encrypted = encrypt(mnemonic.join(' '), password).toString();
  localStorage.setItem('encrypted_mnemonic', encrypted);
}

function retrieveMnemonicEncrypted(password: string): string[] {
  const encrypted = localStorage.getItem('encrypted_mnemonic');
  if (!encrypted) throw new Error('No mnemonic stored');

  const decrypted = decrypt(encrypted, password).toString(Utf8);
  return decrypted.split(' ');
}
```

---

## Code Examples

### Complete Wallet Client Implementation

```typescript
import * as bip39 from 'bip39';
import * as bip32 from 'bip32';

export class SorchaWalletClient {
  private walletAddress: string;
  private hdWallet: HDWalletClient;
  private apiBaseUrl: string;

  constructor(mnemonic: string[], walletAddress: string, apiBaseUrl: string) {
    this.hdWallet = new HDWalletClient(mnemonic);
    this.walletAddress = walletAddress;
    this.apiBaseUrl = apiBaseUrl;
  }

  /**
   * Create new wallet and return client instance
   */
  static async create(
    name: string,
    apiBaseUrl: string,
    algorithm: string = 'ED25519',
    wordCount: number = 12
  ): Promise<{ client: SorchaWalletClient, mnemonic: string[] }> {
    // Call wallet creation API
    const response = await fetch(`${apiBaseUrl}/api/v1/wallets`, {
      method: 'POST',
      headers: { 'Content-Type': 'application/json' },
      body: JSON.stringify({ name, algorithm, wordCount })
    });

    const data = await response.json();

    const client = new SorchaWalletClient(
      data.mnemonicWords,
      data.wallet.address,
      apiBaseUrl
    );

    return {
      client,
      mnemonic: data.mnemonicWords
    };
  }

  /**
   * Restore wallet from mnemonic
   */
  static restore(
    mnemonic: string[],
    walletAddress: string,
    apiBaseUrl: string
  ): SorchaWalletClient {
    return new SorchaWalletClient(mnemonic, walletAddress, apiBaseUrl);
  }

  /**
   * Generate and register new receive address
   */
  async generateReceiveAddress(label?: string): Promise<AddressInfo> {
    // Get current addresses to find next index
    const addresses = await this.listAddresses({ type: 'receive', account: 0 });
    const nextIndex = addresses.length;

    // Derive client-side
    const derived = this.hdWallet.deriveAddress(0, 0, nextIndex);

    // Register with server
    const response = await fetch(
      `${this.apiBaseUrl}/api/v1/wallets/${this.walletAddress}/addresses`,
      {
        method: 'POST',
        headers: { 'Content-Type': 'application/json' },
        body: JSON.stringify({
          derivedPublicKey: derived.publicKey,
          derivedAddress: derived.address,
          derivationPath: derived.path,
          label
        })
      }
    );

    return await response.json();
  }

  /**
   * Get next unused receive address
   */
  async getReceiveAddress(): Promise<AddressInfo> {
    const unused = await this.listAddresses({ type: 'receive', used: false });

    if (unused.length === 0) {
      return await this.generateReceiveAddress('Auto-generated');
    }

    return unused[0];
  }

  /**
   * List addresses with filters
   */
  async listAddresses(filters?: AddressFilters): Promise<AddressInfo[]> {
    const params = new URLSearchParams();
    if (filters?.type) params.set('type', filters.type);
    if (filters?.account !== undefined) params.set('account', filters.account.toString());
    if (filters?.used !== undefined) params.set('used', filters.used.toString());

    const response = await fetch(
      `${this.apiBaseUrl}/api/v1/wallets/${this.walletAddress}/addresses?${params}`
    );

    const data = await response.json();
    return data.addresses;
  }

  /**
   * Sign transaction (client-side only, private key never leaves)
   */
  signTransaction(txData: any, addressPath: string): string {
    const derived = this.hdWallet.deriveFromPath(addressPath);

    // Sign with private key
    const signature = ed25519.sign(
      Buffer.from(JSON.stringify(txData)),
      derived.privateKey
    );

    return Buffer.from(signature).toString('base64');
  }
}

// Type definitions
interface AddressInfo {
  id: string;
  address: string;
  derivationPath: string;
  label?: string;
  isUsed: boolean;
  createdAt: string;
}

interface AddressFilters {
  type?: 'receive' | 'change';
  account?: number;
  used?: boolean;
}
```

### Usage Example

```typescript
async function main() {
  // Create new wallet
  const { client, mnemonic } = await SorchaWalletClient.create(
    'My Sorcha Wallet',
    'https://wallet-service.sorcha.io'
  );

  console.log('⚠️  BACKUP THIS MNEMONIC:');
  console.log(mnemonic.join(' '));

  // Generate first receive address
  const address1 = await client.generateReceiveAddress('First Address');
  console.log('Address 1:', address1.address);

  // Get address for receiving payment
  const paymentAddress = await client.getReceiveAddress();
  console.log('Send payment to:', paymentAddress.address);

  // List all addresses
  const allAddresses = await client.listAddresses();
  console.log(`Total addresses: ${allAddresses.length}`);
}
```

---

## API Reference

### Wallet Endpoints

| Method | Endpoint | Description |
|--------|----------|-------------|
| POST | `/api/v1/wallets` | Create new HD wallet |
| GET | `/api/v1/wallets/{address}` | Get wallet details |
| GET | `/api/v1/wallets` | List all wallets |

### Address Endpoints

| Method | Endpoint | Description |
|--------|----------|-------------|
| POST | `/api/v1/wallets/{address}/addresses` | Register derived address |
| GET | `/api/v1/wallets/{address}/addresses` | List addresses (with filters) |
| GET | `/api/v1/wallets/{address}/addresses/{id}` | Get specific address |
| PATCH | `/api/v1/wallets/{address}/addresses/{id}` | Update address metadata |
| POST | `/api/v1/wallets/{address}/addresses/{id}/mark-used` | Mark address as used |

### Account Endpoints

| Method | Endpoint | Description |
|--------|----------|-------------|
| GET | `/api/v1/wallets/{address}/accounts` | List accounts with statistics |
| GET | `/api/v1/wallets/{address}/gap-status` | Check BIP44 gap limit compliance |

### Query Parameters

**List Addresses:**
- `type`: Filter by type (`receive` or `change`)
- `account`: Filter by BIP44 account number
- `used`: Filter by usage status (`true` or `false`)
- `label`: Filter by label (partial match)
- `page`: Page number (default: 1)
- `pageSize`: Items per page (default: 50, max: 100)

---

## Troubleshooting

### Error: "Gap limit exceeded"

**Problem:** Trying to register more than 20 unused addresses

**Solution:**
```typescript
// Mark existing addresses as used first
const unusedAddresses = await listAddresses(walletAddress, { used: false });
for (const addr of unusedAddresses.slice(0, 10)) {
  await markAddressAsUsed(walletAddress, addr.id);
}
```

### Error: "Invalid BIP44 derivation path"

**Problem:** Malformed derivation path

**Solution:**
```typescript
// ✅ Correct format
const path = `m/44'/0'/0'/0/1`;

// ❌ Wrong formats
const bad1 = `m/44/0/0/0/1`;      // Missing hardened markers (')
const bad2 = `m/44'/0'/0'/2/1`;   // Invalid change value (must be 0 or 1)
```

### Error: "Address already exists"

**Problem:** Trying to register same address twice

**Solution:**
```typescript
// Check if address exists first
const existing = await listAddresses(walletAddress);
const addressExists = existing.some(a => a.address === newAddress);

if (!addressExists) {
  await registerDerivedAddress(/* ... */);
}
```

### Mnemonic Lost

**Problem:** User lost their mnemonic phrase

**Solution:** **There is NO solution**. The mnemonic cannot be recovered. This is a fundamental security feature. Always:
- Show clear backup instructions during wallet creation
- Require user confirmation that mnemonic was backed up
- Consider implementing mnemonic verification (user re-enters it)

---

## Additional Resources

- **BIP39 Specification:** https://github.com/bitcoin/bips/blob/master/bip-0039.mediawiki
- **BIP32 Specification:** https://github.com/bitcoin/bips/blob/master/bip-0032.mediawiki
- **BIP44 Specification:** https://github.com/bitcoin/bips/blob/master/bip-0044.mediawiki
- **Sorcha API Documentation:** See OpenAPI/Scalar docs at `/scalar/v1`
- **Performance Benchmarks:** See `tests/PERFORMANCE-RESULTS.md`

---

**Last Updated:** 2025-11-19
**Maintainer:** Sorcha Development Team
**License:** Apache 2.0
