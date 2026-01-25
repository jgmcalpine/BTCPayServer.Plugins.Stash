# BTCPayServer.Plugins.Stash

A BTCPay Server plugin for automated financial discipline and treasury management. Stash provides merchants with automated allocation and treasury management capabilities, addressing the volatility risk of accepting Bitcoin by allowing automatic allocation of revenue to specific destinations based on user-defined rules.

## Overview

Stash implements a "Virtual Segregation" approach - instead of splitting funds on-chain for every transaction (which is inefficient), it tracks liabilities logically in a database and executes a "Batch Withdrawal" only when it becomes economically viable.

## Features

### 1. Virtual Ledger (The Watcher)
- Listens for `InvoiceSettled` events
- Calculates a portion of income based on user-defined percentage (e.g., 20%)
- Records "Pending Allocation" in a local database
- Tracks both Satoshi amount and Fiat value at time of receipt for cost basis accounting
- Funds remain in the merchant's general Lightning balance

### 2. Threshold Monitor (The Sentinel)
- Monitors total accumulated value in the Virtual Ledger
- Compares against user-defined "Batch Threshold" (e.g., $100 USD)
- Uses "Fiat-Targeted Logic": calculates current Bitcoin amount required to achieve target fiat value, accounting for price slippage

### 3. Router (Execution Engine)
When threshold is met, executes one of two actions:

**Mode A: Liability Hedging (Swap to Stablecoin)**
- Initiates a swap from Lightning BTC to Liquid USDT using the Boltz API
- Creates a taxable disposal event but secures USD value

**Mode B: Cold Storage Sweep (Stacking)**
- Initiates an on-chain payout to a pre-defined external Bitcoin address (XPUB or single address)
- Non-taxable transfer event

### 4. Emergency Stop (User Control)
- Manual "Reset Batch" button in the UI
- Clears the Virtual Ledger without moving any funds
- Ensures merchants are never "locked out" of their liquidity

### 5. Accounting & Reporting
- Permanent log of all executed batches
- CSV export functionality
- Links specific income invoices (Source) to batch executions (Disposition)
- Compatible with crypto-tax software (Koinly, CoinTracker) format
- Distinguishes between "Trades" (Swaps) and "Transfers" (Sweeps)

### 6. User Interface (Dashboard)
- **Settings**: Allocation Percentage, Batch Threshold, Destination Type, Destination Address
- **Status Widget**: Current Pending Stash (Fiat/Sats), Total Stashed All-Time
- **History**: View executed batches and pending allocations

## Installation

1. Download the plugin package
2. Navigate to your BTCPay Server instance
3. Go to **Server Settings** → **Plugins**
4. Upload and install the plugin
5. Restart BTCPay Server

## Configuration

1. Navigate to your store's **Integrations** menu
2. Select **Stash**
3. Configure the following settings:

| Setting | Description |
|---------|-------------|
| **Enabled** | Enable/disable Stash for this store |
| **Allocation Percentage** | Percentage of each payment to allocate (0-100%) |
| **Fiat Currency** | Currency for threshold calculations (currently USD only) |
| **Batch Threshold** | Fiat amount threshold to trigger batch execution |
| **Minimum Batch Size** | Minimum sats to include in a batch (prevents tiny batches) |
| **Destination Type** | Cold Storage (on-chain) or Liquid Swap (Boltz) |
| **Bitcoin Destination Address** | Bitcoin address or XPUB for cold storage sweeps (required for Cold Storage mode) |
| **Liquid Destination Address** | Liquid network address for USDT (required for Liquid Swap mode) |

## CSV Export Format

The export is compatible with major crypto-tax software:

```csv
Date,Sent Amount,Sent Currency,Received Amount,Received Currency,Fee Amount,Fee Currency,Net Worth Amount,Net Worth Currency,Label,Description,TxHash
2024-01-15 10:30:00,0.00500000,BTC,0.00500000,BTC,0.00001000,BTC,250.00,USD,transfer,Stash cold storage sweep,abc123...
2024-01-16 14:45:00,0.00750000,BTC,375.00,USDT,0.00001500,BTC,375.00,USD,trade,Stash swap to stablecoin,swap123...
```

## Security Considerations

- **Address Validation**: Strict regex validation for Bitcoin (mainnet/testnet), XPUB, and Liquid addresses. Invalid addresses are rejected with helpful error messages.
- **Currency Restriction**: Only USD is currently supported for fiat threshold calculations to ensure compatibility with rate providers.
- **Privacy**: Supports routing API requests through Tor proxy
- **Resilience**: Failed swaps retain Virtual Ledger records and alert users

## Technical Architecture

### Database Schema

- **StashSettings**: Store-specific configuration
- **PendingAllocation**: Individual income allocations pending batch execution
- **ExecutedBatch**: Historical record of executed batches

### Services

- **InvoiceWatcherService**: Listens for invoice events and creates allocations
- **ThresholdMonitorService**: Periodically checks thresholds and triggers batches
- **BatchExecutionService**: Handles actual swap/sweep execution
- **AllocationService**: Manages pending allocations
- **StashSettingsService**: Manages store settings

## Requirements

- BTCPay Server 2.0+
- .NET 8.0
- PostgreSQL database

## License

MIT License - See [LICENSE](../../LICENSE) for details.

## Contributing

Contributions are welcome! Please follow the existing code patterns and include tests for new functionality.
