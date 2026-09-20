using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.IO;
using System.Diagnostics;

using Nationalbanken;

namespace InteractiveBrokers
{
    public class PooledPosition
    {
        public string symbol;
        public string isin;
        public decimal quantity;
        public decimal averageCostPerUnit;

        public PooledPosition(string symbol, string isin, decimal quantity, decimal averageCostPerUnit)
        {
            this.symbol = symbol;
            this.isin = isin;
            this.quantity = quantity;
            this.averageCostPerUnit = averageCostPerUnit;
        }
    }
    
    public class TaxReport
    {
        public decimal totalGainLoss;       // Rubrik 454: Foreign Capital Gains/Losses (calculated in DKK)
        public decimal totalDividends;      // Rubrik 452: Foreign Dividends - Gross
        public decimal totalWithholdingTax; // Rubrik 496: Foreign Withholding Tax Paid
        public decimal interestReceived;    // Rubrik 431: Foreign interest income and other capital income.
        public decimal yearEndAccountValue; // Rubrik 490: Year-End Custody Account Value (from year-end prices)
        
        public List<PooledPosition> yearEndPositions;
    }

    

    public enum PoolEventType
    {
        Trade,
        Split,
        Transfer,
        ReturnOfCapital
    }

    public class PoolEvent
    {
        public string symbol;
        public string isin;
        public string dateTime;

        public PoolEventType type;

        // Trade or Transfer or Split type
        public decimal? quantity;   // signed for Trade (+buy/-sell); positive for transfers; extra shares for splits.
        public decimal? price;      // per-unit price (trade price, or carried-over cost basis for transfers)
        public decimal? commission; // Trade only

        // Split only
        public decimal? splitRatio; // e.g. 2.0 for a 2-for-1 split   // TODO quantity ?

        // ReturnOfCapital only
        public decimal? amount;     // total distribution amount      // TODO quantity?

        private PoolEvent(
            string symbol,
            string isin,
            string dateTime,
            PoolEventType type,
            decimal? quantity,
            decimal? price,
            decimal? commission,
            decimal? splitRatio,    // temp?
            decimal? amount)        // temp?
        {
            this.symbol = symbol;
            this.isin = isin;
            this.dateTime = dateTime;
            this.type = type;
            this.quantity = quantity;
            this.price = price;
            this.commission = commission;
            this.splitRatio = splitRatio;   // temp?
            this.amount = amount;           // temp?
        }

        public static PoolEvent FromTrade(Trade trade, ConvertionRates fxRates)
        {
            Trace.Assert(trade.quantity != 0);

            decimal? dailyRate = fxRates.GetRate(trade.dateTime);
            Trace.Assert(dailyRate != null);  

            return new PoolEvent(
                trade.symbol,
                trade.isin,
                trade.dateTime, 
                PoolEventType.Trade, 
                dailyRate * trade.quantity,
                trade.tradePrice,
                trade.commission,
                null,                          // temp?
                null);                         // temp?
        }


        public static PoolEvent FromTransfer(Transfer transfer, ConvertionRates fxRates)
        {
            Trace.Assert(transfer.quantity != 0);
            Trace.Assert((transfer.type == "IN") && (transfer.quantity > 0) 
                     || (transfer.type == "OUT") && (transfer.quantity < 0));

            decimal? dailyRate = fxRates.GetRate(transfer.dateTime);
            Trace.Assert(dailyRate != null);

            return new PoolEvent(
                transfer.symbol,
                transfer.isin,
                transfer.dateTime,
                PoolEventType.Transfer,
                transfer.quantity,
                dailyRate * transfer.costBasis / transfer.quantity,  // CostBasisPerUnit
                transfer.transferPrice,                              // Commission is the transferPrice (usually zero).        
                null,                          // temp?
                null);                         // temp?
        }

        public static PoolEvent FromCorporateAction(CorporateAction corporateAction)
        {
            Trace.Assert(corporateAction.quantity != 0);
            Trace.Assert(corporateAction.type == "FS"); // Only case managed, the Forward Split.

            //decimal? dailyRate = fxRates.GetRate(corporateAction.dateTime);
            //Trace.Assert(dailyRate != null);

            // TODO The corporateAction.description can be parsed to derive the split ratio.
            // TODO The split ration should match pre-split shares (

            return new PoolEvent(
                corporateAction.symbol,
                corporateAction.isin,
                corporateAction.dateTime,
                PoolEventType.Split,
                corporateAction.quantity, // The number of extra shares you get with this split.
                0.0M, //dailyRate * transfer.costBasis / transfer.quantity,  // CostBasisPerUnit
                0.0M, // transfer.transferPrice,                              // Commission is the transferPrice (usually zero).        
                null,                          // temp?
                null);                         // temp?
        }

        public static PoolEvent FromCashTransaction(CashTransaction cashTransaction, ConvertionRates fxRates)
        {
            /*
            Trace.Assert(cashTransaction.amount != 0);
            Trace.Assert(cashTransaction.type == "FS"); // Only case managed, the Forward Split.

            decimal? dailyRate = fxRates.GetRate(corporateAction.dateTime);
            Trace.Assert(dailyRate != null);


            return new PoolEvent(
                corporateAction.symbol,
                corporateAction.isin,
                corporateAction.dateTime,
                PoolEventType.Split,
                corporateAction.quantity, // The number of extra shares you get with this split.
                0.0M, //dailyRate * transfer.costBasis / transfer.quantity,  // CostBasisPerUnit
                0.0M, // transfer.transferPrice,                              // Commission is the transferPrice (usually zero).        
                null,                          // temp?
                null);                         // temp?
             * */

            return new PoolEvent(
                cashTransaction.symbol,
                cashTransaction.isin,
                cashTransaction.dateTime,
                PoolEventType.ReturnOfCapital,
                null, 
                null, 
                null,         
                null, 
                null);    
        }
       
    }


/*
    public static class AverageCostPoolCalculator
    {
        public static Dictionary<string, PooledPosition> Calculate(IEnumerable<Trade> trades)
        {
            
            Dictionary<string, decimal> quantityBySymbol = new Dictionary<string, decimal>();
            Dictionary<string, decimal> totalCostBySymbol = new Dictionary<string, decimal>();

            List<Trade> sorted = trades.OrderBy(t => t.dateTime).ToList();

            foreach (Trade trade in sorted)
            {
                decimal currentQuantity;
                decimal currentTotalCost;
                quantityBySymbol.TryGetValue(trade.symbol, out currentQuantity);
                totalCostBySymbol.TryGetValue(trade.symbol, out currentTotalCost);

                bool isBuy = (trade.quantity > 0);
                if (isBuy)
                {
                    // Buy: pool the new cost in, recompute average implicitly via totals
                    decimal tradeCost = (trade.quantity * trade.tradePrice) + trade.commission;
                    currentTotalCost += tradeCost;
                    currentQuantity += trade.quantity;
                }
                else
                {
                    // Sell: reduce quantity and cost basis at the average cost that existed BEFORE this sale
                    decimal soldQuantity = -trade.quantity;
                    decimal averageCostBeforeSale = currentQuantity == 0m ? 0m : currentTotalCost / currentQuantity;

                    currentTotalCost -= soldQuantity * averageCostBeforeSale;
                    currentQuantity -= soldQuantity;
                }

                quantityBySymbol[trade.symbol] = currentQuantity;
                totalCostBySymbol[trade.symbol] = currentTotalCost;
            }

            Dictionary<string, PooledPosition> result = new Dictionary<string, PooledPosition>();
            foreach (KeyValuePair<string, decimal> kvp in quantityBySymbol)
            {
                string symbol = kvp.Key;
                decimal quantity = kvp.Value;
                decimal totalCost = totalCostBySymbol[symbol];
                decimal averageCost = quantity == 0m ? 0m : totalCost / quantity;

                result[symbol] = new PooledPosition(symbol, isin, quantity, averageCost);
            }

            return result;
        }
    }
*/
}
