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

            var usdRates = ConvertionRates.Load(Depot + "nationalbanken_2023-2026-09-16.csv");
            var queryResultCurrentYear = QueryResult.Load(Depot + "IBKR_2023_1709.csv");
            //var queryResultPreviousYear = QueryResult.Load(Depot + "IBKR_2022.csv");

            // TODO 

            // PROCESS
            // ==============
            // - Sort the trades by dateTime
            // - apply splits
            // - convert to DKK
            // - calculate Average Cost per company (Gennemsnitsmetoden) - HOW ????
            // - 




            // OUTPUT
            // ===============
            // Rubrik 454: Net Realized Capital Gains / Losses (calculated in DKK)
            // Rubrik 452: Foreign Dividends - Gross
            // Rubrik 496: Foreign Withholding Tax Paid
            // Rubrik 490: Year-End Custody Account Value
            //
            // For verification purpose, it calculates the end-of-year positions, 
            // that should coincide with the openPositions of the new year.
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