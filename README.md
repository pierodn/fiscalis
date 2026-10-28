# Fiscalis

Fiscalis is a small command-line utility for preparing Danish tax figures from
Interactive Brokers (IBKR) activity reports. It replays investment activity,
maintains a weighted-average cost basis, converts relevant USD amounts to DKK
using Danmarks Nationalbank daily rates, and prints figures intended to help
with SKAT reporting.

> This project is a personal calculation aid, not tax advice. Verify the
> output, the applicable tax rules, and all final figures before filing.

## What it calculates

For the configured reporting period, the program imports opening holdings and
the current IBKR activity report, then calculates or displays:

- realised share gains and losses using the average-cost method
  (`gennemsnitsmetoden`);
- foreign dividends and withholding tax;
- positive and negative interest;
- commissions and deposits/withdrawals;
- period-end share value and cash value, converted to DKK; and
- a reconciliation against selected values in the IBKR report.

Each sale uses the DKK-weighted average cost of the shares held immediately
before the sale. Purchase commissions are included in the cost basis and sale
commissions reduce net sale proceeds.

## Requirements

- Windows
- .NET Framework 3.5 / Visual Studio capable of building a .NET 3.5 project
- An IBKR CSV Activity Report for the prior and current period
- A USD/DKK daily-rate CSV export from Danmarks Nationalbank

The solution is [`Fiscalis.sln`](Fiscalis.sln); the executable project is
[`Skat/ledger2tax.csproj`](Skat/ledger2tax.csproj).

## Input files

The program currently expects its input files in:

```text
C:\Users\Admin\Downloads\
```

The active configuration in [`Skat/Program.cs`](Skat/Program.cs) uses:

```text
nationalbanken_2010-2026-10-08_USD.csv
IBKR_2024.csv        # opening positions
IBKR_2025.csv        # activity for the reporting period
```

Change `Depot` and the selected filenames in `Program.cs` before running for a
different machine or tax year.

### IBKR activity report

The importer expects a CSV report with these sections, in this order:

1. Change in NAV
2. Realized & Unrealized Performance Summary / realised performance
3. Cash Report
4. Open Positions
5. Trades
6. Corporate Actions
7. Grant Activity
8. Cash Transactions
9. Transfers

Opening positions are loaded from the prior-period report. The DKK cost basis
is not available in the normal IBKR position section, so the program asks for
it interactively unless an additional DKK-cost column is already present.

### FX rates

Export daily USD exchange rates from Danmarks Nationalbank as CSV. The current
importer expects the semicolon-separated format containing a `dd-mm-yyyy` date
and a USD quotation per 100 USD. It converts this to DKK per USD internally.

## Build and run

Open `Fiscalis.sln` in a compatible Visual Studio version and build the
`Skat` project, then run the resulting executable. The program is interactive:
it may request missing opening DKK cost bases and waits for Enter before exit.

## Important limitations

- The current workflow is designed for USD-denominated IBKR activity.
- Input locations and reporting-year filenames are hard-coded in `Program.cs`.
- The parser expects the specific IBKR report layout described above.
- `Rubrik 434` foreign-exchange FIFO gain/loss is displayed as a placeholder;
  it is not calculated.
- Corporate actions and transfers should be reviewed carefully against source
  documents before relying on the results.

## Project layout

```text
Skat/
  ActivityReport.cs   IBKR CSV parsing and report models
  EventProcessor.cs   event conversion, position pool, and tax calculations
  FxRates.cs          Danmarks Nationalbank USD/DKK import and lookup
  Program.cs          configured workflow and console output
  Precondition.cs     calculation invariants
```

## License

See [LICENSE](LICENSE).
