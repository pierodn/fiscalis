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
    public class RealizedGain
    {
        public string symbol;
        public decimal amount;    // positive = gain, negative = loss
        public string dateTime;

        public RealizedGain(string symbol, decimal amount, string dateTime)
        {
            this.symbol = symbol;
            this.amount = amount;
            this.dateTime = dateTime;
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
        public decimal yearEndCashUSD;              // Uninvested cash
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
        public List<RealizedGain> realizedGainsDKK = new List<RealizedGain>();

        public TaxReport(List<Position> openPositions) 
        {
            // TODO convert to DKK using the start period daily fxrate (?)
            positions = openPositions.ToDictionary(p => p.symbol);
        }

        public void WritePositions()
        {
            WritePositions(Console.Out);
        }

        public void WritePositions(TextWriter writer)
        {
            TextWriter original = Console.Out;
            Console.SetOut(writer);

            List<string> keys = new List<string>(this.positions.Keys);
            keys.Sort(StringComparer.Ordinal);
            foreach (string key in keys)
            {
                Console.WriteLine(this.positions[key]);
            }

            Console.SetOut(original); // restore
        }

        public static void WritePositions(List<Position> positions)
        {
            WritePositions(positions, Console.Out);
        }

        public static void WritePositions(List<Position> positions, TextWriter writer)
        {
            TextWriter original = Console.Out;
            Console.SetOut(writer);

            var positionsDic = positions.ToDictionary(p => p.symbol);

            List<string> keys = new List<string>(positionsDic.Keys);
            keys.Sort(StringComparer.Ordinal);
            foreach (string key in keys)
            {
                Console.WriteLine(positionsDic[key]);
            }

            Console.SetOut(original); // restore
        }
    }

    // ========================================
    // Events
    // ========================================

    public enum EventType
    {
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

        private Event()
        {
        }

        private Event(
            string symbol,
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
            /* 
            foreach (var grant in activityReport.grants)
            {
                events.Add(Event.FromGrantActivity(grant, fxRates));
            }
            */
            foreach (var cashTransaction in activityReport.cashTransactions)
            {
                events.Add(Event.FromCashTransaction(cashTransaction, fxRates));
            }

            events = events 
                .OrderBy(e => e.dateTime)
                .ThenBy(e => e.quantity > 0 ? 0 : 1) // acquisitions first at the same timestamp
                .ToList();

            return events;
        }

        public static Event FromTrade(Trade trade, FxRates fxRates)
        {
            Trace.Assert(trade.quantity != 0);
            Trace.Assert((trade.buySell.ToUpperInvariant() == "BUY") && (trade.quantity > 0) && (trade.proceeds < 0)
                      || (trade.buySell.ToUpperInvariant() == "SELL") && (trade.quantity < 0) && (trade.proceeds > 0));

            decimal? dailyRateFound = fxRates.GetRate(trade.dateTime);
            Trace.Assert(dailyRateFound.HasValue);
            decimal dailyRate = dailyRateFound.Value;

            return new Event(
                trade.symbol,
                trade.dateTime,
                dailyRate,
                EventType.Trade, 
                trade.quantity,
                trade.tradePrice, // TODO remove ?
                trade.commission, // TODO remove ?
                trade.proceeds + trade.commission,  // netCashFlow => amount // negative = cash out (buy), positive = cash in (sell)
                0.0m); // splitRatio
        }

        public static Event FromTransfer(Transfer transfer, FxRates fxRates)
        {
            Trace.Assert(transfer.quantity != 0);
            Trace.Assert((transfer.type == "IN") && (transfer.quantity > 0) 
                      || (transfer.type == "OUT") && (transfer.quantity < 0));

            decimal? dailyRateFound = fxRates.GetRate(transfer.dateTime);
            Trace.Assert(dailyRateFound.HasValue);
            decimal dailyRate = dailyRateFound.Value;

            return new Event(
                transfer.symbol,
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
            Trace.Assert(corporateAction.quantity != 0);

            decimal? dailyRateFound = fxRates.GetRate(corporateAction.dateTime);
            Trace.Assert(dailyRateFound.HasValue);
            decimal dailyRate = dailyRateFound.Value;

            Event evt = new Event();
            evt.symbol = corporateAction.symbol;
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
                    evt.quantity = corporateAction.quantity;       // should be negative = full position close-out
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
/*
        public static Event FromGrantActivity(GrantActivity grantActivity, FxRates fxRates)
        {
            Trace.Assert(grantActivity.value != 0);

            decimal? dailyRateFound = fxRates.GetRate(grantActivity.vestingDate);
            Trace.Assert(dailyRateFound.HasValue);
            decimal dailyRate = dailyRateFound.Value;

            Event evt = new Event();
            evt.symbol = grantActivity.symbol;
            evt.dateTime = grantActivity.vestingDate;
            evt.dailyRate = dailyRate;

            evt.type = EventType.Transfer;
            evt.quantity = grantActivity.quantity;
            evt.amount = grantActivity.value;

            return evt;
        }
  */      
        public static Event FromCashTransaction(CashTransaction cashTransaction, FxRates fxRates)
        {
            Trace.Assert(cashTransaction.amount != 0);

            decimal? dailyRateFound = fxRates.GetRate(cashTransaction.dateTime);
            Trace.Assert(dailyRateFound.HasValue);
            decimal dailyRate = dailyRateFound.Value;

            Event evt = new Event();
            evt.symbol = cashTransaction.symbol;
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
            else if (t.IndexOf("deposit") >= 0)
            {
                evt.type = EventType.DepositOrWithdrawal;
            }
            else if (t.IndexOf("interest") >= 0)
            {
                evt.type = EventType.Interest;
            }
            else
            {
                Trace.TraceError("Unhandled cashTransaction.type: " + cashTransaction.type);
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
            string eventTypeColumn = "";
            string qtyByPriceColumn = "";
            string detailsColumn = "";

            switch (type)
            {
                case EventType.Trade:
                    eventTypeColumn = (quantity > 0 ? "BUY" : "SELL");
                    qtyByPriceColumn = (quantity > 0 ? " " : "") + quantity + " * " + price;
                    detailsColumn = "Amount=" + amount + " Commission=" + commission;
                    break;
                case EventType.Split:
                    eventTypeColumn = "SPLIT";
                    detailsColumn = "Getting extra " + quantity;
                    break;
                case EventType.Transfer:
                    eventTypeColumn = "TRANSFER";
                    detailsColumn = "Quantity=" + quantity + " Amount=" + amount + " (costBasis including commission " + commission + ")";
                    break;
                case EventType.ReturnOfCapital:
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
                symbolColumn.PadRight(6) +
                eventTypeColumn.PadRight(20) + 
                qtyByPriceColumn.PadRight(20) +
                detailsColumn;

           return line;
        }

        public static TaxReport Process(List<Position> positions, List<Event> events, bool WriteEventSequence)
        {
            TaxReport report = new TaxReport(positions);

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


                Position position;
                if (!report.positions.TryGetValue(evt.symbol, out position))
                {
                    position = new Position();
                    position.symbol = evt.symbol;
                    report.positions[evt.symbol] = position;
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
                            Precondition.Check(position.quantity > 0, "Selling implies quantity should be positive");

                            decimal averageCostUSD = position.quantity == 0 ? 0m : position.totalCostUSD / position.quantity;
                            decimal averageCostDKK = position.quantity == 0 ? 0m : position.totalCostDKK / position.quantity;

                            decimal soldQuantity = -evt.quantity;
                            decimal costOfSoldUSD = averageCostUSD * soldQuantity;
                            decimal costOfSoldDKK = averageCostDKK * soldQuantity;

                            decimal realizedGainUSD = evt.amount - costOfSoldUSD;
                            decimal realizedGainDKK = evt.dailyRate * evt.amount - costOfSoldDKK;

                            RealizedGain gain = new RealizedGain(evt.symbol, realizedGainDKK, evt.dateTime);
                            report.realizedGainsDKK.Add(gain);

                            report.totalGainLossUSD += realizedGainUSD;
                            report.totalGainLossDKK += realizedGainDKK;

                            position.quantity += evt.quantity; // it's subtracting
                            position.totalCostUSD -= costOfSoldUSD;
                            position.totalCostDKK -= costOfSoldDKK;

                            if (position.quantity < 0.001m)
                            {
                                report.positions.Remove(evt.symbol);
                            }

                            report.totalCommissionsUSD += evt.commission;
                            report.totalCommissionsDKK += evt.dailyRate * evt.commission;
                        }
                        break;

                    case EventType.Split:
                        Precondition.Check(position.quantity > 0, "Split should happen with quantity > 0");
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
                            Precondition.Check(position.quantity > 0, "Transfer out should happen with quantity > 0");

                            decimal averageCostUSD = position.quantity == 0 ? 0m : position.totalCostUSD / position.quantity;
                            decimal averageCostDKK = position.quantity == 0 ? 0m : position.totalCostDKK / position.quantity;

                            decimal transferedQuantity = -evt.quantity;

                            decimal costOfTransferedUSD = averageCostUSD * transferedQuantity;
                            decimal costOfTransferedDKK = averageCostDKK * transferedQuantity;

                            position.quantity += evt.quantity;
                            position.totalCostUSD -= costOfTransferedUSD;
                            position.totalCostDKK -= costOfTransferedDKK;

                            if (position.quantity < 0.001m)
                            {
                                report.positions.Remove(evt.symbol);
                            }
                        }
                        break;

                    case EventType.ReturnOfCapital:
                        Precondition.Check(position.quantity > 0, "ReturnOfCapital implies quantity > 0");
                        Precondition.Check(position.totalCostDKK > 0, "ReturnOfCapital implies totalCostDKK > 0");

                        // The company handing you back a piece of your own original investment, not profit.
                        // But if the capital returned is more than what you invested, then it is a profit (gain).
                        // The capital returned is supposed to only reduce your totalCost, not the quantity.

                        bool IsROCMoreThanWhatInvested = evt.dailyRate * evt.amount > position.totalCostDKK;
                        if (IsROCMoreThanWhatInvested)
                        {
                            decimal realizedGainUSD = evt.amount - position.totalCostUSD;
                            decimal realizedGainDKK = evt.dailyRate * evt.amount - position.totalCostDKK;

                            RealizedGain gain = new RealizedGain(evt.symbol, realizedGainDKK, evt.dateTime);
                            report.realizedGainsDKK.Add(gain);

                            report.totalGainLossUSD += realizedGainUSD;
                            report.totalGainLossDKK += realizedGainDKK;

                            position.totalCostUSD = 0m;
                            position.totalCostDKK = 0m;

                            Trace.TraceError("Case ROC amount > costBasisTotal. Difference: {0} USD", realizedGainUSD);
                        }
                        else
                        {
                            position.totalCostUSD -= evt.amount;
                            position.totalCostDKK -= evt.dailyRate * evt.amount;
                        }
                        break;
                }
            }

            return report;
        }
       
    }

    

}
