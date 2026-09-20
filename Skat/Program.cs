using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO; // temp

using Nationalbanken;
using InteractiveBrokers;

namespace LedgerToTax
{
    class Program
    {
        static void Main(string[] args)
        {
            const string Depot = "C:\\Users\\Admin\\Downloads\\";

            var fxRates = ConvertionRates.Import(Depot + "nationalbanken_2023-2026-09-16.csv");
            var queryResponse = FlexQueryResponse.Import(Depot + "IBKR_2023_1709.csv");
            //var queryResultPreviousYear = QueryResult.Load(Depot + "IBKR_2022.csv");

            /*
            TODO
            - Create a List<PoolEvent> from sections: trades, transfers, corporate actions, cash transactions.
              * And while creating, apply the fxRate to the target Currency (DKK) 
              - A pool event is an event that can affect the PooledPositions.
              - So the pool events are: 
                * trades
                * transfers
                * splits
                - Return Of Capital 
              - NOTES 
                - From CashTransactions, you get:
                  - Return Of Capital (ROC) => the only one that affects the PooledPositions' AverageCostBasis.
                  - WithouldingTax, Dividends => don't affect the PooledPositions and could be added to totals already here.
                  - broker interest received => same, it goes to total interest received.
                  - Broker fees/commissions => should someway be folded into cost basis / sale proceeds.
                  - other non-trade-tied fees (like monthly account fees) => not deductible.

             - Sort the poolEvents by dateTime.

             - Process the poolEvents to update the PooledPositions
               - BUY/SELL/transfer will 
                 - update the PooledPositions
                 - recalculate the new AverageCostPerUnit
               - SELL triggers gain/loss and add up to totalGainLoss 
                 - Add a warning for the ETF MAG7
            */
            var poolEvents = new List<PoolEvent>();
            
            foreach (var trade in queryResponse.trades)
            {
                poolEvents.Add(PoolEvent.FromTrade(trade, fxRates));
            }
            
            foreach (var transfer in queryResponse.transfers)
            {
                poolEvents.Add(PoolEvent.FromTransfer(transfer, fxRates));
            }

            foreach (var corporateAction in queryResponse.corporateActions)
            {
                poolEvents.Add(PoolEvent.FromCorporateAction(corporateAction));
            }

            foreach (var cashTransaction in queryResponse.cashTransactions)
            {
                // Interested only in ReturnOfCapital events
                PoolEvent roc = PoolEvent.FromCashTransaction(cashTransaction, fxRates);
                if (roc.type == PoolEventType.ReturnOfCapital)
                {
                    poolEvents.Add(roc);
                }
            }

            return;



            // NOTES
            //
            // Gennemsnitsmetoden's average
            // ============================
            // The OpenPositions section produced by the IBKR Flex Query 
            // is useful only to verify the symbols and the quantities tracked by IBKR
            // but it doesn't know about Schwab.
            // Also CostBasisPrice or OpenPrice are not relateable to the gennemsnitsmetoden's average.
            // Unfortunately the gennemsnitsmetoden's average will have to be calculated 
            // by re-playing all buy/sell trades on the stock since the beginning I purchased the first position.

            // However, a positions list that includes quantities and averagecost, is useful as input 
            // for the AverageCostPoolCalculator, to limit the re-play of the trades to only the current year.
            // And that section can take place at the beginning, just like the one produced by the Flex Query.

            // Rubrik 454: Steps
            // =================
            // https://gemini.google.com/app/12d42110431fceec
            // ==============================================
            // * Inject artificially the 918 stocks from the Schwab account witht that costbasis.
            // - Sort the trades by dateTime.
            // - Apply splits.
            // - Convert to DKK.
            // - Calculate Average Cost per company (Gennemsnitsmetoden) - HOW ????
            // - 



            // OUTPUT: TaxReport
        }
             

    }

    public class TaxEngine
    {
        public class Pool
        {
            public double Shares { get; set; }
            public double TotalCostDkk { get; set; }
        }

        public class RealizedGainRecord
        {
            public string Date { get; set; }
            public string Symbol { get; set; }
            public double Shares { get; set; }
            public double ProceedsDkk { get; set; }
            public double CostDkk { get; set; }
            public double RealizedGlDkk { get; set; }
        }

        // Explicit instantiation (C# 3.0 compatible; 'new()' syntax is not allowed here)
        private Dictionary<string, double> _fxRates = new Dictionary<string, double>();
        private Dictionary<string, Pool> _pools = new Dictionary<string, Pool>(StringComparer.OrdinalIgnoreCase);
        
        public List<RealizedGainRecord> RealizedGains = new List<RealizedGainRecord>();
        public List<string> Dividends = new List<string>();

        public void LoadExchangeRates(string fxFilePath)
        {
            string[] lines = File.ReadAllLines(fxFilePath);
            for (int i = 1; i < lines.Length; i++) // Skip header
            {
                string line = lines[i];
                string[] parts = line.Split(',');
                if (parts.Length >= 2)
                {
                    string dateStr = parts[0].Trim();
                    double rate;
                    if (double.TryParse(parts[1].Trim(), NumberStyles.Any, CultureInfo.InvariantCulture, out rate))
                    {
                        if (rate > 10) rate /= 100.0; // Normalizing if per 100 USD
                        _fxRates[dateStr] = rate;
                    }
                }
            }
        }

        public void LoadOpeningInventory(string inventoryFilePath)
        {
            string[] lines = File.ReadAllLines(inventoryFilePath);
            for (int i = 1; i < lines.Length; i++) // Skip header
            {
                string line = lines[i];
                string[] parts = line.Split(',');
                if (parts.Length >= 3)
                {
                    string symbol = parts[0].Trim().ToUpper();
                    double shares = double.Parse(parts[1].Trim(), CultureInfo.InvariantCulture);
                    double totalCostDkk = double.Parse(parts[2].Trim(), CultureInfo.InvariantCulture);

                    _pools[symbol] = new Pool { Shares = shares, TotalCostDkk = totalCostDkk };
                }
            }
        }

        private double GetFxRate(string dateStr)
        {
            DateTime dt;
            if (!DateTime.TryParse(dateStr, out dt))
            {
                throw new ArgumentException("Invalid date format: " + dateStr);
            }

            for (int i = 0; i < 10; i++)
            {
                string checkStr = dt.ToString("yyyy-MM-dd");
                double rate;
                if (_fxRates.TryGetValue(checkStr, out rate))
                {
                    return rate;
                }
                dt = dt.AddDays(-1);
            }

            throw new Exception("Exchange rate not found for date " + dateStr + " or preceding days.");
        }

        public void ProcessTrade(string date, string symbol, double quantity, double priceUsd, double commUsd)
        {
            symbol = symbol.ToUpper();
            double fx = GetFxRate(date);
            double priceDkk = priceUsd * fx;
            double commDkk = commUsd * fx;

            if (!_pools.ContainsKey(symbol))
            {
                _pools[symbol] = new Pool { Shares = 0.0, TotalCostDkk = 0.0 };
            }

            Pool pool = _pools[symbol];

            if (quantity > 0) // BUY
            {
                double costDkk = (quantity * priceDkk) + commDkk;
                pool.Shares += quantity;
                pool.TotalCostDkk += costDkk;
            }
            else if (quantity < 0) // SELL
            {
                double sharesSold = Math.Abs(quantity);
                if (pool.Shares <= 0)
                {
                    throw new InvalidOperationException("Attempting to sell " + symbol + " with zero or negative inventory pool.");
                }

                double avgCostPerShareDkk = pool.TotalCostDkk / pool.Shares;
                double costOfSoldShares = sharesSold * avgCostPerShareDkk;
                double proceedsDkk = (sharesSold * priceDkk) - commDkk;
                double realizedGlDkk = proceedsDkk - costOfSoldShares;

                pool.Shares -= sharesSold;
                pool.TotalCostDkk -= costOfSoldShares;

                if (pool.Shares <= 1e-6)
                {
                    pool.Shares = 0.0;
                    pool.TotalCostDkk = 0.0;
                }

                RealizedGains.Add(new RealizedGainRecord
                {
                    Date = date,
                    Symbol = symbol,
                    Shares = sharesSold,
                    ProceedsDkk = proceedsDkk,
                    CostDkk = costOfSoldShares,
                    RealizedGlDkk = realizedGlDkk
                });
            }
        }
    }
}