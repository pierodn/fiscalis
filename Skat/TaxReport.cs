using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.IO;
using System.Diagnostics;

using MyDiagnostics;
using Nationalbanken;

namespace InteractiveBrokers
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

        // For inspection
        public List<RealizedGain> realizedGains = new List<RealizedGain>();

        // for testing
        public Dictionary<string, Position> positions = new Dictionary<string, Position>();
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
        public decimal splitRatio; // e.g. 2.0 for a 2-for-1 split   // TODO quantity ?

        private Event()
        {
        }

        private Event(
            string symbol,
            string isin,
            string dateTime,
            EventType type,
            decimal quantity,
            decimal price,
            decimal commission,
            decimal amount,
            decimal splitRatio    // temp?
            ) 
        {
            this.symbol = symbol;
            this.isin = isin;
            this.dateTime = dateTime;
            this.type = type;
            this.quantity = quantity;
            this.price = price;
            this.commission = commission;
            this.amount = amount;  
            this.splitRatio = splitRatio;   // temp?
        }

        public static Event FromTrade(Trade trade, ConvertionRates fxRates)
        {
            Trace.Assert(trade.quantity != 0);
            //SoftAssert.Check( Math.Abs( Math.Abs(trade.proceeds) - trade.quantity * trade.tradePrice < 0.01m), "Assuming they are equal");
            Trace.Assert(trade.buySell.Equals("BUY") && (trade.quantity > 0) && (trade.proceeds < 0)
                      || trade.buySell.Equals("SELL") && (trade.quantity < 0) && (trade.proceeds > 0));

            decimal? dailyRatefound = fxRates.GetRate(trade.dateTime);
            Trace.Assert(dailyRatefound != null);
            decimal dailyRate = dailyRatefound.GetValueOrDefault();

            return new Event(
                trade.symbol,
                trade.isin,
                trade.dateTime, 
                EventType.Trade, 
                trade.quantity,
                dailyRate * trade.tradePrice,
                dailyRate * trade.commission,
                0.0m,  // amount
                0.0m); // splitRatio
        }

        public static Event FromTransfer(Transfer transfer, ConvertionRates fxRates)
        {
            Trace.Assert(transfer.quantity != 0);
            Trace.Assert((transfer.type == "IN") && (transfer.quantity > 0) 
                      || (transfer.type == "OUT") && (transfer.quantity < 0));

            decimal? dailyRatefound = fxRates.GetRate(transfer.dateTime);
            Trace.Assert(dailyRatefound != null);
            decimal dailyRate = dailyRatefound.GetValueOrDefault();

            return new Event(
                transfer.symbol,
                transfer.isin,
                transfer.dateTime,
                EventType.Transfer,
                transfer.quantity,
                dailyRate * transfer.costBasis / transfer.quantity,  // CostBasisPerUnit
                dailyRate * transfer.transferPrice,                  // Commission is the transferPrice (usually zero).        
                dailyRate * transfer.costBasis,                      // CostBasis (Amount)
                0.0m);                                               // no splitRatio
        }

        public static Event FromCorporateAction(CorporateAction corporateAction, ConvertionRates fxRates)
        {
            Trace.Assert(corporateAction.quantity != 0);
/*
            decimal? dailyRatefound = fxRates.GetRate(corporateAction.dateTime);
            Trace.Assert(dailyRatefound != null);
            decimal dailyRate = dailyRatefound.GetValueOrDefault();
            */
            Event evt = new Event();
            evt.symbol = corporateAction.symbol;
            evt.isin = corporateAction.isin;
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

        public static Event FromCashTransaction(CashTransaction cashTransaction, ConvertionRates fxRates)
        {
            Trace.Assert(cashTransaction.amount != 0);

            decimal? dailyRatefound = fxRates.GetRate(cashTransaction.dateTime);
            Trace.Assert(dailyRatefound != null);
            decimal dailyRate = dailyRatefound.GetValueOrDefault();

            Event evt = new Event();
            evt.symbol = cashTransaction.symbol;
            evt.isin = cashTransaction.isin;
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
            string s = dateTime.Substring(0, 10) + " " + symbol.PadRight(5) + Enum.GetName(typeof(EventType), type).PadRight(16);
            s += (type == EventType.Trade) ? (((quantity > 0) ? " " : "") + quantity + " * " + price).PadRight(20) :
                (type == EventType.Split) ? (" "+quantity).PadRight(20) : "".PadRight(20);
            s += " amount=" + amount;
            s += " commission=" + commission;
            s += " splitRatio=" + splitRatio;
            return s;  
        }
       
    }

}
