using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.IO;
using System.Globalization;
using System.Diagnostics;

using MyDiagnostics;
using Nationalbanken;
using InteractiveBrokers;


namespace EventProcessor
{
    public class RealizedGainRecord
    {
        public string symbol;
        public string isin;
        public string dateTime;
        public decimal fxRate;
        public decimal proceedsUSD;
        public decimal costOfSoldUSD;
        public decimal realizedGainUSD;
        public decimal proceedsDKK;
        public decimal costOfSoldDKK;
        public decimal realizedGainDKK;

        public RealizedGainRecord(string symbol, string isin, string dateTime, decimal fxRate,
            decimal proceedsUSD, decimal costOfSoldUSD, decimal realizedGainUSD,
            decimal proceedsDKK, decimal costOfSoldDKK, decimal realizedGainDKK)
        {
            this.symbol = symbol;
            this.isin = isin;
            this.dateTime = dateTime;
            this.fxRate = fxRate;
            this.proceedsUSD = proceedsUSD;
            this.costOfSoldUSD = costOfSoldUSD;
            this.realizedGainUSD = realizedGainUSD;
            this.proceedsDKK = proceedsDKK;
            this.costOfSoldDKK = costOfSoldDKK;
            this.realizedGainDKK = realizedGainDKK;
        }
    }

    public class TaxReport
    {
        // For me to verify against Activity Statement (Change in NAV)
        public decimal totalInterestUSD;
        public decimal totalInterestExpenseUSD;
        public decimal totalDividendsUSD;
        public decimal totalGainLossUSD;
        public decimal totalWithholdingTaxUSD;
        public decimal totalCommissionsUSD;
        public decimal yearEndPorfolioValueUSD;     // Market value of all shares at the End-Year.
        public decimal yearEndCashUSD;              // TODO remove?
        public decimal totalDepositsWithdrawalsUSD;

        // For reporting to SKAT
        public decimal totalInterestDKK;            // Rubrik 431: interest (positive)
        public decimal totalInterestExpenseDKK;     // Rubrik 432: interest (negative) paid because of leverage
        public decimal totalDividendsDKK;           // Rubrik 452: Foreign Dividends - Gross
        public decimal totalGainLossDKK;            // Rubrik 454: Foreign Capital Gains/Losses (calculated in DKK)
        public decimal totalWithholdingTaxDKK;      // Rubrik 496: Foreign Withholding Tax Paid
        public decimal totalCommissionsDKK;         //
        public decimal yearEndPorfolioValueDKK;     // Rubrik 490: Market value of all shares at the End-Year (in DKK)
        public decimal yearEndCashDKK;              // Rubrik 492: Uninvested cash converted from USD to DKK (end-year rate)
        public decimal netFXGainLossFIFO;           // Rubrik 434: FIFO GainLoss when withdrawing cash (if greater than +/-2000 DKK).
        public decimal totalDepositsWithdrawalsDKK; //
        // TODO Rubrik 493. If you owe IBKR money at year end

        // StartPeriodPositions and EndPeriodPositions after the events
        public Dictionary<string, Position> positions = new Dictionary<string, Position>();

        // For SKAT inspection
        public List<RealizedGainRecord> realizedGainRecords = new List<RealizedGainRecord>();

        public TaxReport(List<Position> openPositions) 
        {
            positions = openPositions.ToDictionary(p => p.Key);
        }

        public void WritePositions()
        {
            Position.WritePositions(this.positions);
        }

        public void WriteRealizedRecords()
        {
            Console.WriteLine();
            Console.WriteLine("Realized Gain Records");
            Console.WriteLine("============================================ FX ====== Proceeds ===== BuyCost ====== GainLoss ===== Proceeds ===== BuyCost ====== GainLoss =====");

            decimal totalRealizedGainUSD = 0;
            decimal totalRealizedGainDKK = 0;
            
            foreach (var record in this.realizedGainRecords)
            {
                Console.WriteLine("{0,-20} {1,-14} {2,-8} {3,6:F4} {4,10:F2} USD {5,10:F2} USD {6,10:F2} USD {7,10:F2} DKK {8,10:F2} DKK {9,10:F2} DKK ",
                    record.dateTime, record.isin, record.symbol, record.fxRate, 
                    record.proceedsUSD, record.costOfSoldUSD, record.realizedGainUSD,
                    record.proceedsDKK, record.costOfSoldDKK, record.realizedGainDKK);

                totalRealizedGainUSD += record.realizedGainUSD;
                totalRealizedGainDKK += record.realizedGainDKK;
            }
            Console.WriteLine("{0,10:F2} USD".PadLeft(95) + "{1,10:F2} DKK".PadLeft(44), totalRealizedGainUSD, totalRealizedGainDKK);
        }
    }

    // ========================================
    // Events
    // ========================================

    public enum EventType
    {
        Unknown,
        Trade,
        Split,
        Transfer,
        ReturnOfCapital,
        WithholdingTax,
        Dividends,
        DepositOrWithdrawal,
        Interest
    }

    public class Event
    {
        public string symbol;
        public string isin;
        public string dateTime;
        public decimal dailyRate;
        public EventType type;

        public decimal quantity;   // signed for Trade (+buy/-sell); positive for transfers; extra shares for splits.
        public decimal price;      // per-unit price (trade price, or carried-over cost basis for transfers)
        public decimal commission; // Trade only
        public decimal amount;     // total distribution amount (also ReturnOfCapital)  
        public decimal splitRatio; // e.g. 2.0 for a 2-for-1 split   // It's in quantity. TODO remove ?

        public string PositionKey
        {
            get
            {
                return !string.IsNullOrEmpty(this.isin) && this.isin.Trim().Length > 0 ? this.isin.Trim() : this.symbol.Trim();
            }
        }

        private Event()
        {
        }

        private Event(
            string symbol,
            string isin,
            string dateTime,
            decimal dailyRate, // to reportingCurrency
            // TODO string broker // IBKR, SAXO
            EventType type,
            decimal quantity,
            decimal price,      // TODO rename priceUSD ?
            decimal commission, // TODO rename commissionUSD ?
            decimal amount,     // TODO rename amountUSD ?
            decimal splitRatio  // TODO remove ?
            ) 
        {
            this.symbol = symbol;
            this.isin = isin;
            this.dateTime = dateTime;
            this.dailyRate = dailyRate;
            this.type = type;
            this.quantity = quantity;
            this.price = price;
            this.commission = commission;
            this.amount = amount;
            this.splitRatio = splitRatio;
        }

        public static List<Event> FromActivityReport(ActivityReport activityReport, FxRates fxRates)
        {
            var events = new List<Event>();

            foreach (var trade in activityReport.trades)
            {
                events.Add(Event.FromTrade(trade, fxRates));
            }

            foreach (var transfer in activityReport.transfers)
            {
                events.Add(Event.FromTransfer(transfer, fxRates));
            }

            foreach (var corporateAction in activityReport.corporateActions)
            {
                events.Add(Event.FromCorporateAction(corporateAction, fxRates));
            }
            
            foreach (var grant in activityReport.grants)
            {
                bool isWithin =
                    String.CompareOrdinal(grant.vestingDate, activityReport.changeInNAV.fromDate) >= 0 &&
                    String.CompareOrdinal(grant.vestingDate, activityReport.changeInNAV.toDate) <= 0;

                if (isWithin)
                {
                    events.Add(Event.FromGrantActivity(grant, fxRates));
                }
            }
            
            foreach (var cashTransaction in activityReport.cashTransactions)
            {
                events.Add(Event.FromCashTransaction(cashTransaction, fxRates));
            }

            events = events
                .OrderBy(e => e.dateTime.Substring(0, 10))      // Group by Day first
                .ThenBy(e => e.type == EventType.Split ? 0 : 1) // Splits take effect before market opens
                .ThenBy(e => e.dateTime)
                .ThenBy(e => e.quantity > 0 ? 0 : 1)            // acquisitions first at the same timestamp
                .ToList();

            return events;
        }

        public static Event FromTrade(Trade trade, FxRates fxRates)
        {
            Precondition.ASSERT(trade.quantity != 0);
            Precondition.ASSERT((trade.buySell.ToUpperInvariant() == "BUY") && (trade.quantity > 0) && (trade.proceeds < 0)
                      || (trade.buySell.ToUpperInvariant() == "SELL") && (trade.quantity < 0) && (trade.proceeds > 0));
            

            decimal dailyRate = fxRates.GetRate(trade.dateTime);

            return new Event(
                trade.symbol,
                trade.isin,
                trade.dateTime,
                dailyRate,
                EventType.Trade, 
                trade.quantity,
                trade.tradePrice, // TODO remove ?  Because Proceeds = Quantity × Trade Price + commission
                trade.commission, // TODO remove ?
                trade.proceeds + trade.commission,  // netCashFlow => amount // negative = cash out (buy), positive = cash in (sell)
                0.0m); // splitRatio
        }

        public static Event FromTransfer(Transfer transfer, FxRates fxRates)
        {
            Precondition.ASSERT(transfer.quantity != 0);
            // TODO this can be verified in 2026
            Precondition.ASSERT((transfer.type == "IN") && (transfer.quantity > 0) 
                            || (transfer.type == "OUT") && (transfer.quantity < 0));

            decimal? dailyRateFound = fxRates.GetRate(transfer.dateTime);
            Precondition.ASSERT(dailyRateFound.HasValue);
            decimal dailyRate = dailyRateFound.Value;

            return new Event(
                transfer.symbol,
                transfer.isin,
                transfer.dateTime,
                dailyRate,
                EventType.Transfer,
                transfer.quantity,
                transfer.costBasis / transfer.quantity,  // CostBasisPerUnit
                transfer.transferPrice,                  // Commission is the transferPrice (usually zero).        
                transfer.costBasis,                      // CostBasis (Amount)
                0.0m);                                   // no splitRatio
        }

        public static Event FromCorporateAction(CorporateAction corporateAction, FxRates fxRates)
        {
            Precondition.ASSERT(corporateAction.quantity != 0);

            decimal dailyRate = fxRates.GetRate(corporateAction.dateTime);

            Event evt = new Event();
            evt.symbol = corporateAction.symbol;
            evt.isin = corporateAction.isin;
            evt.dateTime = corporateAction.dateTime;
            evt.dailyRate = dailyRate;

            switch (corporateAction.type)
            {
                case "FS": // Forward Split
                case "RS": // Reverse Split
                case "SD": // Stock Dividend
                case "FI": // Forward Split Issue
                    evt.type = EventType.Split;
                    evt.quantity = corporateAction.quantity;         // signed delta, as established earlier
                    evt.amount = 0m;                     // cost basis total unchanged by a split
                    break;

                //case "TC": // Merger
                case "DW": // Delisted Worthless
                //case "OR": // Asset Purchase
                //case "TO": // Tender
                    // Sanity check: DW should zero out the entire position, not partially reduce it
                    // (verify at the point where you have access to the current Position, likely in BuildPositions
                    //  rather than here in BuildCorporateActionEvent, since this function doesn't see Position state)
                    evt.type = EventType.Trade;
                    evt.quantity = -Math.Abs(corporateAction.quantity); // Expected negtaive, enforce is negative => full position close-out
                    evt.amount = 0m; // proceeds received (0 for DW)
                    break;

                default:
                    Trace.TraceError("Unhandled corporateAction.type: " + corporateAction.type);
                    break;
            }
            
            // TODO The corporateAction.description can be parsed to derive the split ratio.
            // TODO The split ration should match pre-split shares.

            return evt;     
        }

        public static Event FromGrantActivity(GrantActivity grantActivity, FxRates fxRates)
        {
            Precondition.ASSERT(grantActivity.value != 0);

            decimal dailyRate = fxRates.GetRate(grantActivity.vestingDate);

            Event evt = new Event();
            evt.symbol = grantActivity.symbol;
            evt.isin = grantActivity.isin;
            evt.dateTime = grantActivity.vestingDate;
            evt.dailyRate = dailyRate;

            evt.type = EventType.Transfer;
            evt.quantity = grantActivity.quantity;
            evt.amount = grantActivity.value;

            return evt;
        }
 
        public static Event FromCashTransaction(CashTransaction cashTransaction, FxRates fxRates)
        {
            Precondition.ASSERT(cashTransaction.amount != 0);

            decimal dailyRate = fxRates.GetRate(cashTransaction.dateTime);

            Event evt = new Event();
            evt.symbol = cashTransaction.symbol;
            evt.isin = cashTransaction.isin;
            evt.dateTime = cashTransaction.dateTime;
            evt.dailyRate = dailyRate;

            string t = (cashTransaction.type ?? "").Trim().ToLowerInvariant();
            string d = (cashTransaction.description ?? "").Trim().ToLowerInvariant();
            if (t.IndexOf("return of capital") >= 0 || d.IndexOf("return of capital") >= 0 || d.IndexOf("non-taxable") >= 0)
            {
                evt.type = EventType.ReturnOfCapital;
            }
            else if (t.IndexOf("withholding") >= 0)
            {
                evt.type = EventType.WithholdingTax;
            }
            else if (t.IndexOf("dividend") >= 0)
            {
                evt.type = EventType.Dividends;
            }
            else if (t.IndexOf("deposit") >= 0 || t.IndexOf("withdrawal") >= 0 || d.IndexOf("deposit") >= 0 || d.IndexOf("withdrawal") >= 0)
            {
                evt.type = EventType.DepositOrWithdrawal;
            }
            else if (t.IndexOf("interest") >= 0)
            {
                evt.type = EventType.Interest;
            }
            else
            {
                throw new InvalidOperationException("Unhandled cashTransaction.type: " + cashTransaction.type);
            }

            evt.quantity = 0m;
            evt.amount = cashTransaction.amount;
            evt.splitRatio = 0m;

            return evt;
        }

        public override string ToString()
        {
            string dateColumn = dateTime;
            string symbolColumn = symbol;
            string isinColumn = isin;
            string eventTypeColumn = "";
            string quantityColumn = "";
            string priceColumn = "";
            string detailsColumn = "";

            switch (type)
            {
                case EventType.Trade:
                    eventTypeColumn = (quantity > 0 ? "BUY" : "SELL");
                    quantityColumn = string.Format("{0,9}", quantity.ToString("F2")).Replace(".00", "   ");
                    priceColumn = string.Format("{0,7}", price.ToString("F2"));
                    detailsColumn = "Amount=" + amount + " Commission=" + commission;
                    break;
                case EventType.Split:
                    eventTypeColumn = "SPLIT";
                    detailsColumn = "Getting extra " + quantity;
                    break;
                case EventType.Transfer:
                    eventTypeColumn = "TX";
                    detailsColumn = "Quantity=" + quantity + " Amount=" + amount + " (costBasis including commission " + commission + ")";
                    break;
                case EventType.ReturnOfCapital:
                    eventTypeColumn = "ROC";
                    detailsColumn = "Return Of Capital: " + amount;
                    break;
                case EventType.Dividends:
                    detailsColumn = "Dividends: " + amount;
                    break;
                case EventType.WithholdingTax:
                    detailsColumn = "Withholding tax: " + amount;
                    break;
                case EventType.DepositOrWithdrawal:
                    detailsColumn = "Deposit/withdrawal: " + amount;
                    break;
                case EventType.Interest:
                    detailsColumn = "Interest: " + amount;
                    break;
                default:
                    Trace.TraceError("Unhandled event type: " + Enum.GetName(typeof(EventType), type));
                    break;
            }


            string line = 
                dateColumn.PadRight(20) +
                isinColumn.PadRight(16) +
                symbolColumn.PadRight(9) +
                eventTypeColumn.PadRight(6) +
                quantityColumn.PadRight(10) + 
                priceColumn.PadRight(12) +
                detailsColumn;

           return line;
        }

        public static TaxReport Process(List<Position> positions, List<Event> events, bool WriteEventSequence)
        {
            TaxReport report = new TaxReport(positions);

            if (WriteEventSequence)
            {
                Console.WriteLine();
                Console.WriteLine("Events Sequence");
                Console.WriteLine("================================================================================================================");
            }

            foreach (Event evt in events)
            {
                if (WriteEventSequence)
                {
                    Console.WriteLine(evt);
                }

                if (evt.type == EventType.WithholdingTax)
                {
                    report.totalWithholdingTaxUSD += evt.amount;
                    report.totalWithholdingTaxDKK += evt.dailyRate * evt.amount;
                    continue;
                }
                else if (evt.type == EventType.Dividends)
                {
                    report.totalDividendsUSD += evt.amount;
                    report.totalDividendsDKK += evt.dailyRate * evt.amount;
                    continue;
                }
                else if (evt.type == EventType.Interest)
                {
                    bool isInterestExpense = (evt.amount < 0);
                    if (isInterestExpense)
                    {
                        // This is negative, paid to broker normally when you have leverage
                        report.totalInterestExpenseUSD += evt.amount;
                        report.totalInterestExpenseDKK += evt.dailyRate * evt.amount;
                    }
                    else
                    {
                        // This is positive interested paid by broker
                        report.totalInterestUSD += evt.amount;
                        report.totalInterestDKK += evt.dailyRate * evt.amount;
                    }
                    continue;
                }
                else if (evt.type == EventType.DepositOrWithdrawal)
                {
                    report.totalDepositsWithdrawalsUSD += evt.amount;
                    report.totalDepositsWithdrawalsDKK += evt.dailyRate * evt.amount;
                    continue;
                }

                string key = evt.PositionKey;
                Position position;
                if (!report.positions.TryGetValue(key, out position))
                {
                    // Graceful migration check: if previously keyed by symbol, update to ISIN
                    if (!string.IsNullOrEmpty(evt.symbol) && report.positions.TryGetValue(evt.symbol, out position))
                    {
                        report.positions.Remove(evt.symbol);
                        position.isin = evt.isin;
                        report.positions[key] = position;
                    }
                    else
                    {
                        position = new Position();
                        position.symbol = evt.symbol;
                        position.isin = evt.isin;
                        report.positions[key] = position;
                    }
                }

                switch (evt.type)
                {
                    // TODO Have EventType.Buy and EventType.Sell and positive quantity and amount
                    // instead of EventType.Trade and positive/negative quantity and amount. 
                    case EventType.Trade:
                        Boolean IsBUY = evt.quantity > 0;
                        if (IsBUY)
                        {
                            position.quantity += evt.quantity;
                            position.totalCostUSD += -evt.amount;
                            position.totalCostDKK += -evt.dailyRate * evt.amount;

                            report.totalCommissionsUSD += evt.commission;
                            report.totalCommissionsDKK += evt.dailyRate * evt.commission;
                        }
                        else // SELL
                        {
                            const decimal Epsilon = 0.0001m;

                            decimal soldQuantity = -evt.quantity;
                            Precondition.ASSERT(soldQuantity > 0);
                            Precondition.ASSERT(position.totalCostUSD >= 0);
                            Precondition.ASSERT(position.totalCostDKK >= 0);

                            // 1. Detect if this sale is effectively closing the full position (within rounding tolerance)
                            bool closesPosition = (position.quantity <= soldQuantity) || Math.Abs(position.quantity - soldQuantity) < Epsilon;
                            if (closesPosition)
                            {
                                // Snap soldQuantity to exact position quantity to absorb any CSV rounding dust
                                soldQuantity = position.quantity;
                            }
                            else
                            {
                                Precondition.ASSERT(soldQuantity < position.quantity);
                            }

                            decimal averageCostUSD = position.totalCostUSD / position.quantity;
                            decimal averageCostDKK = position.totalCostDKK / position.quantity;

                            // 2. If closing, allocate the entire remaining pool to avoid residual cents
                            decimal costOfSoldUSD = closesPosition ? position.totalCostUSD : averageCostUSD * soldQuantity;
                            decimal costOfSoldDKK = closesPosition ? position.totalCostDKK : averageCostDKK * soldQuantity;

                            // NOTE:
                            //    amount = quantity * tradePrice     (which is "net proceeds", the cash received after commission)
                            //    cost   = quantity * averagePrice
                            //    gain   = proceeds - cost

                            decimal netProceedsUSD = evt.amount;  
                            decimal netProceedsDKK = evt.dailyRate * evt.amount;

                            decimal realizedGainUSD = netProceedsUSD - costOfSoldUSD;
                            decimal realizedGainDKK = netProceedsDKK - costOfSoldDKK;
                           
                            RealizedGainRecord record = new RealizedGainRecord(
                                evt.symbol, evt.isin, evt.dateTime, evt.dailyRate,
                                netProceedsUSD, costOfSoldUSD, realizedGainUSD,
                                netProceedsDKK, costOfSoldDKK, realizedGainDKK);
                            report.realizedGainRecords.Add(record);

                            report.totalGainLossUSD += realizedGainUSD;
                            report.totalGainLossDKK += realizedGainDKK;

                            position.quantity -= soldQuantity;
                            position.totalCostUSD -= costOfSoldUSD;
                            position.totalCostDKK -= costOfSoldDKK;

                            // 3. Remove from dictionary if closed or if only micro-dust remains
                            if (closesPosition || position.quantity < Epsilon)
                            {
                                report.positions.Remove(key);
                            }

                            report.totalCommissionsUSD += evt.commission;
                            report.totalCommissionsDKK += evt.dailyRate * evt.commission;
                        }
                        break;

                    case EventType.Split:
                        Precondition.ASSERT(position.quantity > 0);
                        position.quantity += evt.quantity; // total cost unchanged
                        break;

                    case EventType.Transfer:
                        // TODO EventType.TransferIn/Out and quantity always positive
                        if (evt.quantity > 0)
                        {
                            position.quantity += evt.quantity;
                            position.totalCostUSD += evt.amount;
                            position.totalCostDKK += evt.dailyRate * evt.amount;
                        }
                        else
                        {
                            Precondition.ASSERT(position.quantity > 0);
                            Precondition.ASSERT(position.totalCostUSD > 0);
                            Precondition.ASSERT(position.totalCostDKK > 0);

                            decimal averageCostUSD = position.totalCostUSD / position.quantity;
                            decimal averageCostDKK = position.totalCostDKK / position.quantity;

                            decimal transferedQuantity = -evt.quantity;

                            decimal costOfTransferedUSD = averageCostUSD * transferedQuantity;
                            decimal costOfTransferedDKK = averageCostDKK * transferedQuantity;

                            position.quantity += evt.quantity;
                            position.totalCostUSD -= costOfTransferedUSD;
                            position.totalCostDKK -= costOfTransferedDKK;

                            if (position.quantity < 0.0001m)
                            {
                                report.positions.Remove(key);
                            }
                        }
                        break;

                    case EventType.ReturnOfCapital:

                        // The company handing you back a piece of your own original investment, not profit.
                        // But if the capital returned is more than what you invested, then it is a profit (gain).
                        // The capital returned is supposed to only reduce your totalCost, not the quantity.

                        Precondition.ASSERT(position.quantity > 0);
                        Precondition.ASSERT(position.totalCostDKK > 0);
                        Precondition.ASSERT(position.totalCostUSD > 0);

                        decimal rocUSD = evt.amount;
                        decimal rocDKK = evt.dailyRate * evt.amount;

                        decimal costAbsorbedUSD;
                        decimal rocRealizedGainUSD = 0m;

                        decimal costAbsorbedDKK;
                        decimal rocRealizedGainDKK = 0m;

                        // 1. Process USD basis reduction
                        if (rocUSD > position.totalCostUSD)
                        {
                            // Capital returned exceeds total cost: excess is a taxable gain
                            costAbsorbedUSD = position.totalCostUSD;
                            rocRealizedGainUSD = rocUSD - position.totalCostUSD;
                            position.totalCostUSD = 0m;
                        }
                        else
                        {
                            // Capital returned simply reduces basis
                            costAbsorbedUSD = rocUSD;
                            position.totalCostUSD -= rocUSD;
                        }

                        // 2. Process DKK basis reduction independently
                        if (rocDKK > position.totalCostDKK)
                        {
                            // Capital returned exceeds total DKK cost: excess is a taxable gain in DKK
                            costAbsorbedDKK = position.totalCostDKK;
                            rocRealizedGainDKK = rocDKK - position.totalCostDKK;
                            position.totalCostDKK = 0m;
                        }
                        else
                        {
                            // Capital returned simply reduces DKK basis
                            costAbsorbedDKK = rocDKK;
                            position.totalCostDKK -= rocDKK;
                        }

                        // 3. Record realized gain if either currency generated excess profit
                        if (rocRealizedGainUSD > 0m || rocRealizedGainDKK > 0m)
                        {
                            RealizedGainRecord record = new RealizedGainRecord(
                                evt.symbol, evt.isin, evt.dateTime, evt.dailyRate,
                                rocUSD, costAbsorbedUSD, rocRealizedGainUSD,
                                rocDKK, costAbsorbedDKK, rocRealizedGainDKK);
                            report.realizedGainRecords.Add(record);
                            report.totalGainLossUSD += rocRealizedGainUSD;
                            report.totalGainLossDKK += rocRealizedGainDKK;
                        }
                        break;
                }
            }

            return report;
        }
       
    }

    

}
