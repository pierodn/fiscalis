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


namespace TaxEngine
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
        // For reporting to SKAT
        public decimal totalGainLoss;       // Rubrik 454: Foreign Capital Gains/Losses (calculated in DKK)
        public decimal totalDividends;      // Rubrik 452: Foreign Dividends - Gross
        public decimal totalWithholdingTax; // Rubrik 496: Foreign Withholding Tax Paid
        public decimal interestReceived;    // Rubrik 431: Foreign interest income and other capital income.
        public decimal yearEndAccountValue; // Rubrik 490: Year-End Custody Account Value (from year-end prices)

        public Dictionary<string, Position> positions = new Dictionary<string, Position>();

        // For inspection
        public List<RealizedGain> realizedGains = new List<RealizedGain>();

        public TaxReport(List<Position> openPositions)
        {
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
                string symbol = this.positions[key].symbol.PadRight(8);
                string quantity = this.positions[key].quantity.ToString().PadRight(8);
                string costBasisTotal = this.positions[key].costBasisTotal.ToString("F2", CultureInfo.InvariantCulture);
                Console.WriteLine(string.Format("{0} {1} {2}", symbol, quantity, costBasisTotal));
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
        Dividends
    }

    public class Event
    {
        public string symbol;
        public string isin;
        public string dateTime;

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
            EventType type,
            decimal quantity,
            decimal price,
            decimal commission, // TODO remove ?
            decimal amount,
            decimal splitRatio  // TODO remove ?
            ) 
        {
            this.symbol = symbol;
            this.dateTime = dateTime;
            this.type = type;
            this.quantity = quantity;
            this.price = price;
            this.commission = commission; // TODO remove ?
            this.amount = amount;
            this.splitRatio = splitRatio; // TODO remove ?
        }

        public static Event FromTrade(Trade trade, FxRates fxRates)
        {
            Trace.Assert(trade.quantity != 0);
            //Precondition.Check( Math.Abs( Math.Abs(trade.proceeds) - trade.quantity * trade.tradePrice < 0.01m), "Assuming they are equal");
            Trace.Assert((trade.buySell.ToUpperInvariant() == "BUY") && (trade.quantity > 0) && (trade.proceeds < 0)
                      || (trade.buySell.ToUpperInvariant() == "SELL") && (trade.quantity < 0) && (trade.proceeds > 0));

            decimal? dailyRatefound = fxRates.GetRate(trade.dateTime);
            Trace.Assert(dailyRatefound != null);
            decimal dailyRate = dailyRatefound.GetValueOrDefault();

            decimal proceeds = dailyRate * trade.proceeds;
            decimal commission = dailyRate * trade.commission;
            decimal netCashFlow = proceeds + commission; // negative = cash out (buy), positive = cash in (sell)

            return new Event(
                trade.symbol,
                trade.dateTime, 
                EventType.Trade, 
                trade.quantity,
                dailyRate * trade.tradePrice,
                dailyRate * trade.commission, // TODO remove ?
                netCashFlow,  // amount
                0.0m); // splitRatio
        }

        public static Event FromTransfer(Transfer transfer, FxRates fxRates)
        {
            Trace.Assert(transfer.quantity != 0);
            Trace.Assert((transfer.type == "IN") && (transfer.quantity > 0) 
                      || (transfer.type == "OUT") && (transfer.quantity < 0));

            decimal? dailyRatefound = fxRates.GetRate(transfer.dateTime);
            Trace.Assert(dailyRatefound != null);
            decimal dailyRate = dailyRatefound.GetValueOrDefault();

            return new Event(
                transfer.symbol,
                transfer.dateTime,
                EventType.Transfer,
                transfer.quantity,
                dailyRate * transfer.costBasis / transfer.quantity,  // CostBasisPerUnit
                dailyRate * transfer.transferPrice,                  // Commission is the transferPrice (usually zero).        
                dailyRate * transfer.costBasis,                      // CostBasis (Amount)
                0.0m);                                               // no splitRatio
        }

        public static Event FromCorporateAction(CorporateAction corporateAction, FxRates fxRates)
        {
            Trace.Assert(corporateAction.quantity != 0);
/*
            decimal? dailyRatefound = fxRates.GetRate(corporateAction.dateTime);
            Trace.Assert(dailyRatefound != null);
            decimal dailyRate = dailyRatefound.GetValueOrDefault();
            */
            Event evt = new Event();
            evt.symbol = corporateAction.symbol;
            evt.dateTime = corporateAction.dateTime;

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
                    evt.amount = 0m; //dailyRate * Math.Abs(corporateAction.proceeds); // proceeds received (0 for DW)
                    break;

                default:
                    Trace.TraceError("Unhandled corporateAction.type: " + corporateAction.type);
                    break;
            }
            
            // TODO The corporateAction.description can be parsed to derive the split ratio.
            // TODO The split ration should match pre-split shares.

            return evt;                         // temp?
        }

        public static Event FromCashTransaction(CashTransaction cashTransaction, FxRates fxRates)
        {
            Trace.Assert(cashTransaction.amount != 0);

            decimal? dailyRatefound = fxRates.GetRate(cashTransaction.dateTime);
            Trace.Assert(dailyRatefound != null);
            decimal dailyRate = dailyRatefound.GetValueOrDefault();

            Event evt = new Event();
            evt.symbol = cashTransaction.symbol;
            evt.dateTime = cashTransaction.dateTime;

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
            else
            {
                Trace.TraceError("Unhandled cashTransaction.type: " + cashTransaction.type);
            }

            evt.quantity = 0m;
            evt.amount = dailyRate * cashTransaction.amount;
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
                    eventTypeColumn = "<Split>";
                    detailsColumn = "Getting " + quantity;
                    break;
                case EventType.Transfer:
                    eventTypeColumn = "<Transfer>";
                    qtyByPriceColumn = quantity + "";
                    detailsColumn = "Amount=" + amount + " (costBasis including commission " + commission + ")";
                    break;
                case EventType.ReturnOfCapital:
                    eventTypeColumn = "<Return Of Capital>";
                    detailsColumn = "Amount=" + amount;
                    break;
                case EventType.Dividends:
                    eventTypeColumn = "(dividends)";
                    detailsColumn = "Amount=" + amount;
                    break;
                case EventType.WithholdingTax:
                    eventTypeColumn = "(withholding tax)";
                    detailsColumn = "Amount=" + amount;
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
       
    }

}
