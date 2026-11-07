# Fiscalis

Fiscalis is a multi-account tax utility that aggregates trading histories, converts to a target currency, and replays transaction logs to compute realized P/L using custom cost-basis methods (FIFO, Average Cost).

## Motivation

After trading for several years across multiple foreign brokers and currencies, I realized my local tax agency would not simply accept the realized gain/loss reported by my brokers. Instead, they required all calculations to be processed as if every trade occurred within a single unified asset pool, with foreign amounts converted using daily exchange rates.

Manually reconstructing thousands of trades across multiple years under these rules was an insurmountable task. To make matters worse, foreign brokers calculated realized P/L using FIFO, whereas my tax authority strictly mandated the Average Cost method.

I built Fiscalis to automate this exact problem: ingesting exported trading activity from disparate accounts, normalizing execution dates and exchange rates, and deterministically replaying history to compute accurate, tax-compliant P/L.

> This project is a personal calculation aid, not tax advice. Verify the
> output, the applicable tax rules, and all final figures before filing.

## Features & Calculations

For a configured reporting period, Fiscalis ingests historical ledger data, opening positions, and execution logs to calculate and display:

- Realized share gains and losses using target cost-basis accounting (e.g., Average Cost pooling or FIFO).
- Foreign dividends and withholding taxes converted to reporting currency.
- Positive and negative cash interest.
- Brokerage commissions, deposits, and cash withdrawals.
- Period-end portfolio market values and total cash balances converted to the target reporting currency.
- Reconciliation checks against source account statements and raw broker reports.

Each transaction evaluates the weighted cost basis of the shared asset pool immediately prior to execution. Purchase commissions are integrated into the asset cost basis, while sale commissions directly reduce net sale proceeds.

## Requirements

- Windows
- .NET Framework 3.5 / Visual Studio capable of building a .NET 3.5 project
- Account activity exports or transaction CSV files for prior and current periods
- Daily FX exchange rate reference file (CSV) from a central bank or financial authority

## Input Files & Architecture

Fiscalis relies on structured account statements and daily exchange rate feeds to perform deterministic replay calculations.

The core pipeline processes:

1. **FX Exchange Rates:** Daily rate CSV files mapping foreign transaction currencies to your target base reporting currency.
2. **Opening Positions:** Historical position statements establishing starting quantity pools and cost bases.
3. **Activity Logs:** Time-ordered trading activity, including order fills, dividend income, cash movements, and corporate actions.


### Broker Activity Reports

The built-in parser ingests standardized multi-section CSV reports containing key operational blocks:

1. Changes in NAV / Account Summaries
2. Realized & Unrealized Performance Summaries
3. Cash Reports
4. Open Positions & Holdings
5. Executed Trades
6. Corporate Actions
7. Cash Movements & Dividends
8. Account Transfers

If starting cost bases in the target reporting currency are missing from initial position imports, Fiscalis can prompt interactively to establish baseline values for the asset pool.


## Features

- **Expanded Parsers:** Ongoing extension of transaction parsers to support diverse multi-broker statement formats.
- **Multi-Currency Normalization:** Generalizing FX pipelines to dynamically convert multiple foreign trade currencies into any arbitrarily configured target base currency.
- **Advanced Currency P/L:** Expanding foreign exchange cash gain/loss calculations.
- **Complex Corporate Actions:** Ongoing refinement for handling complex corporate restructurings, spin-offs, and stock splits.
