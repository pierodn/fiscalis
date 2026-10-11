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

            const bool WriteEventSequence = true;
            const bool WriteRealizedGainsList = false;


            const string Depot = "C:\\Users\\Admin\\Downloads\\";

            // HOW: https://www.nationalbanken.dk/en => Exchange Rates => US dollars => Download Excel
            var fxRates = FxRates.Import2(Depot + "nationalbanken_2010-2026-10-08_USD.csv");
            var startPeriodPositions = ActivityReport.ExtractPositions(Depot + "IBKR_2022_Schwab.csv");
            var currentActivityReport = ActivityReport.Import(Depot + "IBKR_2023.csv");


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

            foreach (var grant in currentActivityReport.grants)
            {
                events.Add(Event.FromGrantActivity(grant, fxRates));
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

            TaxReport report = new TaxReport(startPeriodPositions);

            Console.WriteLine();
            Console.WriteLine("Start Period Positions");
            Console.WriteLine("===================================================================");
            report.WritePositions();

            events.Sort(delegate(Event a, Event b) { return a.dateTime.CompareTo(b.dateTime); });

            if (WriteEventSequence)
            {
                Console.WriteLine();
                Console.WriteLine("Pooling Events");
                Console.WriteLine("===================================================================");
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

                            report.totalCommissionsUSD +=                 evt.commission;
                            report.totalCommissionsDKK += evt.dailyRate * evt.commission;
                        }
                        else // SELL
                        {
                            Precondition.Check(position.quantity > 0);

                            decimal averageCostUSD = position.quantity == 0 ? 0m : position.totalCostUSD / position.quantity;
                            decimal averageCostDKK = position.quantity == 0 ? 0m : position.totalCostDKK / position.quantity;
                            
                            decimal soldQuantity = -evt.quantity;
                            decimal costOfSoldUSD = averageCostUSD * soldQuantity;
                            decimal costOfSoldDKK = averageCostDKK * soldQuantity;

                            decimal realizedGainUSD =                 evt.amount - costOfSoldUSD;
                            decimal realizedGainDKK = evt.dailyRate * evt.amount - costOfSoldDKK;

                            RealizedGain gain = new RealizedGain(evt.symbol, realizedGainDKK, evt.dateTime);
                            report.realizedGainsDKK.Add(gain);

                            report.totalGainLossUSD += realizedGainUSD;
                            report.totalGainLossDKK += realizedGainDKK;

                            position.quantity += evt.quantity; // it's subtracting
                            position.totalCostUSD -= costOfSoldUSD;
                            position.totalCostDKK -= costOfSoldDKK;

                            if (position.quantity < 1)
                            {
                                Precondition.Check(position.quantity == 0);
                                report.positions.Remove(evt.symbol);
                            }

                            report.totalCommissionsUSD +=                 evt.commission;
                            report.totalCommissionsDKK += evt.dailyRate * evt.commission;
                        }
                        break;

                    case EventType.Split:
                        Precondition.Check(position.quantity > 0);
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
                            Precondition.Check(position.quantity > 0);

                            decimal averageCostUSD = position.quantity == 0 ? 0m : position.totalCostUSD / position.quantity;
                            decimal averageCostDKK = position.quantity == 0 ? 0m : position.totalCostDKK / position.quantity;

                            decimal transferedQuantity = -evt.quantity;

                            decimal costOfTransferedUSD = averageCostUSD * transferedQuantity;
                            decimal costOfTransferedDKK = averageCostDKK * transferedQuantity;

                            position.quantity += evt.quantity;
                            position.totalCostUSD -= costOfTransferedUSD;
                            position.totalCostDKK -= costOfTransferedDKK;

                            if (position.quantity < 1)
                            {
                                Precondition.Check(position.quantity == 0);
                                report.positions.Remove(evt.symbol);
                            }
                        }
                        break;

                    case EventType.ReturnOfCapital:
                        Precondition.Check(position.quantity > 0);
                        Precondition.Check(position.totalCostDKK > 0);

                        // The company handing you back a piece of your own original investment, not profit.
                        // But if the capital returned is more than what you invested, then it is a profit (gain).
                        // The capital returned is supposed to only reduce your totalCost, not the quantity.

                        bool IsROCMoreThanWhatInvested = evt.dailyRate * evt.amount > position.totalCostDKK;
                        if (IsROCMoreThanWhatInvested) 
                        {
                            decimal realizedGainUSD =                 evt.amount - position.totalCostUSD;
                            decimal realizedGainDKK = evt.dailyRate * evt.amount - position.totalCostDKK;

                            Precondition.Check(realizedGainUSD > 0);
                            Precondition.Check(realizedGainDKK > 0);

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
                            position.totalCostUSD -=                 evt.amount;
                            position.totalCostDKK -= evt.dailyRate * evt.amount;
                        }
                        break;
                }
            }



            Console.WriteLine();
            Console.WriteLine("Tax Report");
            Console.WriteLine("======================================================");
            Console.WriteLine("totalInterest                     : {0:F2} USD", report.totalInterestUSD);
            Console.WriteLine("totalInterestExpense              : {0:F2} USD", report.totalInterestExpenseUSD);
            Console.WriteLine("totalDividends                    : {0:F2} USD", report.totalDividendsUSD);
            Console.WriteLine("totalGainLoss                     : {0:F2} USD", report.totalGainLossUSD);
            Console.WriteLine("totalWithholdingTax               : {0:F2} USD", report.totalWithholdingTaxUSD); 
            Console.WriteLine("totalCommissions                  : {0:F2} USD", report.totalCommissionsUSD);
            Console.WriteLine("yearEndPorfolioValue              : {0:F2} USD", report.yearEndPorfolioValueUSD);
            Console.WriteLine("totalDepositsWithdrawals          : {0:F2} USD", report.totalDepositsWithdrawalsUSD);

            Console.WriteLine();
            Console.WriteLine("Tax Report (for SKAT)");
            Console.WriteLine("======================================================");
            Console.WriteLine("totalInterest        (Rubrik 431) : {0:F2} DKK", report.totalInterestDKK);
            Console.WriteLine("totalInterestExpense (Rubrik 432) : {0:F2} DKK", report.totalInterestExpenseDKK);
            Console.WriteLine("totalDividends       (Rubrik 452) : {0:F2} DKK", report.totalDividendsDKK);
            Console.WriteLine("totalGainLoss        (Rubrik 454) : {0:F2} DKK", report.totalGainLossDKK);
            Console.WriteLine("totalWithholdingTax  (Rubrik 496) : {0:F2} DKK", report.totalWithholdingTaxDKK);
            Console.WriteLine("totalCommissions                  : {0:F2} DKK", report.totalCommissionsDKK);
            Console.WriteLine("yearEndPorfolioValue (Rubrik 490) : {0:F2} DKK", report.yearEndPorfolioValueDKK);
            Console.WriteLine("totalDepositsWithdrawals          : {0:F2} DKK", report.totalDepositsWithdrawalsDKK);


            Console.WriteLine();
            Console.WriteLine("End Period Positions (calculated from StartPeriodPositions and Events)");
            Console.WriteLine("===================================================================");
            report.WritePositions();
            Console.WriteLine();
            Console.WriteLine("NOTE: The totalCostDKK values above have to be copied MANUALLY in the StartPeriodPositions of next period.");

            if (WriteRealizedGainsList)
            {
                // TODO TODO.WriteRealizedGains();
            }

            Console.WriteLine();
            Console.WriteLine("End Period Positions (from given IBKR Activity Report)");
            Console.WriteLine("===================================================================");
            currentActivityReport.WriteOpenPositions();

            Console.WriteLine();
            Console.WriteLine("Change in NAV (from given IBKR Activity Report)");
            Console.WriteLine("===================================================================");
            currentActivityReport.WriteChangeInNAV();

            Console.WriteLine();
            Console.WriteLine("PRESS ENTER");
            Console.ReadLine();
        }
             

    }

}