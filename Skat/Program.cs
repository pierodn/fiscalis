using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO; // temp
using System.Diagnostics;
using MyDiagnostics;

using InteractiveBrokers;
using Nationalbanken;
using TaxEngine;


namespace LedgerToTax
{
    class Program
    {
        static void Main(string[] args)
        {
            // ====================================
            // Input
            // ====================================

            const string Depot = "C:\\Users\\Admin\\Downloads\\";

            var fxRates = FxRates.Import(Depot + "nationalbanken_2023-2026-09-16.csv");
            var previousActivityReport = ActivityReport.Import(Depot + "IBKR_2022_Schwab.csv");
            var currentActivityReport = ActivityReport.Import(Depot + "IBKR_2023_1709.csv");


            // ====================================
            // Convert into dated events
            // ====================================

            var events = new List<Event>();
            
            foreach (var trade in currentActivityReport.trades)
            {
                events.Add(Event.FromTrade(trade, fxRates));
            }
            
            foreach (var transfer in currentActivityReport.transfers)
            {
                events.Add(Event.FromTransfer(transfer, fxRates));
            }

            foreach (var corporateAction in currentActivityReport.corporateActions)
            {
                events.Add(Event.FromCorporateAction(corporateAction, fxRates));
            }

            foreach (var cashTransaction in currentActivityReport.cashTransactions)
            {
                events.Add(Event.FromCashTransaction(cashTransaction, fxRates));
            }

            // TODO: Deposits & Withdrawals
            // TODO: Interest

            //
            // Process events in order by date
            //

            TaxReport report = new TaxReport(previousActivityReport.openPositions);

            Console.WriteLine();
            Console.WriteLine("Start Period Positions");
            Console.WriteLine("======================");
            report.WritePositions();

            Console.WriteLine();
            Console.WriteLine("Pooling events");
            Console.WriteLine("==============");

            events.Sort(delegate(Event a, Event b) { return a.dateTime.CompareTo(b.dateTime); });

            foreach (Event evt in events)
            {
                Console.WriteLine(evt);

                if (evt.type == EventType.WithholdingTax)
                {
                    report.totalWithholdingTax += evt.amount;
                    continue; 
                }
                else if (evt.type == EventType.Dividends)
                {
                    report.totalDividends += evt.amount;
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
                    case EventType.Trade:
                        if (evt.quantity > 0)
                        {
                            position.quantity += evt.quantity;
                            position.costBasisTotal += -evt.amount; // TODO ask Claude to come back to ABS(amount)
                        }
                        else
                        {
                            Precondition.Check(position.quantity > 0);

                            decimal averageCost = position.quantity == 0 ? 0m : position.costBasisTotal / position.quantity;
                            decimal soldQuantity = -evt.quantity; 
                            decimal costOfSold = averageCost * soldQuantity;
                            decimal realizedGain = evt.amount - costOfSold;

                            RealizedGain gain = new RealizedGain(evt.symbol, realizedGain, evt.dateTime);
                            report.realizedGains.Add(gain);
                            report.totalGainLoss += realizedGain;

                            position.quantity += evt.quantity;
                            position.costBasisTotal -= costOfSold;

                            if (position.quantity < 1)
                            {
                                report.positions.Remove(evt.symbol);
                            }
                        }
                        break;

                    case EventType.Split:
                        Precondition.Check(position.quantity > 0);
                        position.quantity += evt.quantity; // cost basis total unchanged
                        break;

                    case EventType.Transfer:
                        if (evt.quantity > 0)
                        {
                            position.quantity += evt.quantity;
                            position.costBasisTotal += evt.amount;
                        }
                        else
                        {
                            Precondition.Check(position.quantity > 0);

                            decimal avgCost = position.quantity == 0 ? 0m : position.costBasisTotal / position.quantity;
                            decimal xferQty = -evt.quantity;
                            decimal costOfTransfer = avgCost * xferQty;

                            position.quantity += evt.quantity;
                            position.costBasisTotal -= costOfTransfer;

                            if (position.quantity < 1)
                            {
                                report.positions.Remove(evt.symbol);
                            }
                        }
                        break;

                    case EventType.ReturnOfCapital:
                        Precondition.Check(position.quantity > 0);
                        if (evt.amount > position.costBasisTotal) // The company handing you back a piece of your own original investment, not profit;
                        {
                            decimal realizedGain = evt.amount - position.costBasisTotal;
                            RealizedGain gain = new RealizedGain(evt.symbol, realizedGain, evt.dateTime);
                            report.realizedGains.Add(gain);

                            report.totalGainLoss += realizedGain;
                            position.costBasisTotal = 0m;
                            Trace.TraceError("Case ROC amount > costBasisTotal. Difference: " + realizedGain);
                        }
                        else
                        {
                            position.costBasisTotal -= evt.amount;
                        }
                        break;
                }
            }


            decimal approxDKKtoUSDrate = 0.15m;

            Console.WriteLine();
            Console.WriteLine("Tax Report");
            Console.WriteLine("==========");
            Console.WriteLine("totalGainLoss        (Rubrik 454): {0:F2} (USD {1:F2})", report.totalGainLoss, approxDKKtoUSDrate * report.totalGainLoss);
            Console.WriteLine("totalDividends       (Rubrik 452): {0:F2} (USD {1:F2})", report.totalDividends, approxDKKtoUSDrate * report.totalDividends);
            Console.WriteLine("totalWithholdingTax  (Rubrik 496): {0:F2} (USD {1:F2})", report.totalWithholdingTax, approxDKKtoUSDrate * report.totalWithholdingTax);
            Console.WriteLine("interestReceived     (Rubrik 431): {0:F2} (USD {1:F2})", report.interestReceived, approxDKKtoUSDrate * report.interestReceived);
            Console.WriteLine("yearEndAccountValue  (Rubrik 490): {0:F2} (USD {1:F2})", report.yearEndAccountValue, approxDKKtoUSDrate * report.yearEndAccountValue);

            Console.WriteLine();
            Console.WriteLine("End Period Positions");
            Console.WriteLine("====================");
            report.WritePositions();


            // Press Enter
            Console.ReadLine();


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


        }
             

    }

}