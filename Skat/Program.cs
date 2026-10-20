using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO; // temp
using System.Diagnostics;
using MyDiagnostics;

using InteractiveBrokers;
using Nationalbanken;
using EventProcessor;

/***
 * Purpose: 
 * - Replay the trading activity over an initial position pool to compute gains, 
 *   dividends, interest, etc., under SKAT's average method (gennemsnitsmetoden), 
 *   applying daily USD/DKK exchange rates.
 * - Work with data from multiple accounts combined.
 * 
 * Inputs
 * - USD/DKK Rates: daily rates from Danmarks Nationalbank.
 * - Initial Positions: a merge of prior-period end positions from all accounts.
 * - Trading Activity: a merge of current-period trading events from all accounts.
 * 
 * Outputs
 * - Tax Report: final calculated figures for SKAT filing.
 * - Closing Positions: end-of-period holdings and total portfolio valuation.
 * 
 ***/


namespace LedgerToTax
{
    class Program
    {
        static string DKK = "DKK", USD = "USD";
        static string NotAvailable = "".PadLeft(14); //"N/A".PadLeft(14);

        static Func<decimal, string, string> FormatAmount = (amount, currency) => string.Format("{0,14}", amount.ToString("F2") + " " + currency);
        static Func<decimal, string> FormatDKK = amount => string.Format("{0,14}", amount.ToString("F2") + " " + DKK);
        static Func<decimal, string> FormatUSD = amount => string.Format("{0,14}", amount.ToString("F2") + " " + USD);
        static Func<decimal, string> FormatPercentage = amount => string.Format("{0,14}", amount.ToString("F2") + "%");
        static Func<string, string, string> Verify = (a, b) => a.Equals(b) ? "OK" : "FAILED";

        static void Main(string[] args)
        {
            const bool WriteEventSequence = true;
            const bool WriteRealizedGainsList = true;

            Console.WriteLine();
            Console.WriteLine("INPUT");
            Console.WriteLine("==================================================================================");

            const string Depot = "C:\\Users\\Admin\\Downloads\\";

            // HOW: https://www.nationalbanken.dk/en => Exchange Rates => US dollars => Download Excel
            var fxRates = FxRates.Import2(Depot + "nationalbanken_2010-2026-10-08_USD.csv");
            
            // 2023
            var startPeriodPositions = ActivityReport.ExtractPositions(Depot + "Schwab_2022.csv"); // include manually the positions from other accounts (Schwab, SAXO)
            var currentActivityReport = ActivityReport.Import(Depot + "IBKR_2023.csv");

            // 2024
            //var startPeriodPositions = ActivityReport.ExtractPositions(Depot + "IBKR_2023.csv");
            //var currentActivityReport = ActivityReport.Import(Depot + "IBKR_2024.csv");



            Console.WriteLine();
            Console.WriteLine("Start Period Positions");
            Position.WritePositions(startPeriodPositions);
            //TaxReport.WritePositions(startPeriodPositions);



            var events = Event.FromActivityReport(currentActivityReport, fxRates);

            TaxReport taxReport = Event.Process(startPeriodPositions, events, WriteEventSequence);


            if (WriteRealizedGainsList)
            {
                taxReport.WriteRealizedRecords();
            }

            
            

            string totalInterestDKK = FormatDKK(taxReport.totalInterestDKK);
            string totalInterestExpenseDKK = FormatDKK(taxReport.totalInterestExpenseDKK);
            string totalDividendsDKK = FormatDKK(taxReport.totalDividendsDKK);
            string totalGainLossDKK = FormatDKK(taxReport.totalGainLossDKK);
            string totalWithholdingTaxDKK = FormatDKK(taxReport.totalWithholdingTaxDKK);
            string totalCommissionsDKK = FormatDKK(taxReport.totalCommissionsDKK);
            string totalDepositsWithdrawalsDKK = FormatDKK(taxReport.totalDepositsWithdrawalsDKK);

            string totalInterestUSD = FormatUSD(taxReport.totalInterestUSD);
            string totalInterestExpenseUSD = FormatUSD(taxReport.totalInterestExpenseUSD);
            string totalDividendsUSD = FormatUSD(taxReport.totalDividendsUSD);
            string totalGainLossUSD = FormatUSD(taxReport.totalGainLossUSD);
            string totalWithholdingTaxUSD = FormatUSD(taxReport.totalWithholdingTaxUSD);
            string totalCommissionsUSD = FormatUSD(taxReport.totalCommissionsUSD);
            string totalDepositsWithdrawalsUSD = FormatUSD(taxReport.totalDepositsWithdrawalsUSD);

            string changeInNAVInterestUSD = FormatUSD(currentActivityReport.changeInNAV.interest);
            //string changeInNAVInterestExpense = string.Format("{0,15}", currentActivityReport.changeInNAV.totalInterestExpenseUSD.ToString("F2") + " USD");
            string changeInNAVDividendsUSD = FormatUSD(currentActivityReport.changeInNAV.dividends);
            //string changeInNAVGainLossMTM = FormatUSD(currentActivityReport.changeInNAV.markToMarket);
            string changeInNAVWithholdingTaxUSD = FormatUSD(currentActivityReport.changeInNAV.withholdingTax);
            string changeInNAVCommissionsUSD = FormatUSD(currentActivityReport.changeInNAV.commissions);
            string changeInNAVDepositsWithdrawalsUSD = FormatUSD(currentActivityReport.changeInNAV.depositsWithdrawals);
            string changeInNAVStartingValueUSD = FormatUSD(currentActivityReport.changeInNAV.startingValue);
            string changeInNAVEndingValueUSD = FormatUSD(currentActivityReport.changeInNAV.endingValue);
            string changeInNAVTimeWeightedReturnUSD = FormatPercentage(currentActivityReport.changeInNAV.timeWeightedReturn);
            string totalRealizedGainLossUSD = FormatUSD(currentActivityReport.GetTotalRealizedGainLoss());


            Dictionary<string, Position> calculatedEndYearPositions = taxReport.positions;
            Dictionary<string, Position> currentActivityReportOpenPositions = currentActivityReport.GetOpenPositions();

            decimal totalPositionValue = 0;

            foreach (var pair in calculatedEndYearPositions)
            {
                string calculatedPositionSymbol = pair.Key;
                Position calculatedPosition = pair.Value;

                // Always pick the end-year price if present
                if (currentActivityReportOpenPositions.ContainsKey(calculatedPositionSymbol))
                {
                    //Trace.Assert(calculatedPosition.positionValue == 0);    
                    //Precondition.Check(calculatedPosition.positionValue > 0, "PositionValue not present in Previous Activity Report.");

                    Position openPosition = currentActivityReportOpenPositions[calculatedPositionSymbol];
                    Trace.Assert(openPosition.positionValue > 0);

                    decimal endYearSharePrice = openPosition.positionValue / openPosition.quantity;
                    calculatedPosition.positionValue = endYearSharePrice * calculatedPosition.quantity;    
                }
                else
                {
                    Precondition.Check(calculatedPosition.positionValue > 0, "PositionValue not present in Activity Report.");
                }
 
                totalPositionValue += calculatedPosition.positionValue;
            }

            //currentActivityReportPositions.ToDictionary(p => p.symbol);

            // Use the currentActivityReport OpenPositions to pick the end-year sharePrice

            /*
            foreach (Position position in this.endYearPositions.OrderBy(x => x.symbol))
            {
                Console.WriteLine(position);
            }
             */

            // Some of these values are in the TaxReport. 
            // They were supposed to be calculated in some way.
            // But eventually retrieved here below.
            // TODO remove from tax report

            string endYearDate = currentActivityReport.changeInNAV.toDate;
            decimal? endYearRate = fxRates.GetRate(endYearDate);

            string yearEndPorfolioValueUSD = FormatUSD(totalPositionValue);
            string yearEndPorfolioValueDKK = FormatDKK(endYearRate.Value * totalPositionValue);

            decimal endingCash = currentActivityReport.cashReport.endingCash;
            string yearEndCashUSD = FormatUSD(endingCash);
            string yearEndCashDKK = FormatDKK(endYearRate.Value * endingCash);

            decimal stocksValueApproximation = currentActivityReport.changeInNAV.endingValue - endingCash;
            string stocksValueApproximationUSD = FormatUSD(stocksValueApproximation);
  
            
            

  
            Console.WriteLine();
            Console.WriteLine("PERIOD: {0} / {1}         CALCULATED     CALCULATED      IBKR DATA    CHECK", currentActivityReport.changeInNAV.fromDate, currentActivityReport.changeInNAV.toDate);
            Console.WriteLine("==========================================================================================");
            Console.WriteLine("totalInterest        (Rubrik 431) : {0} {1} {2}    {3}", totalInterestDKK, totalInterestUSD, changeInNAVInterestUSD, Verify(totalInterestUSD, changeInNAVInterestUSD));
            Console.WriteLine("totalInterestExpense (Rubrik 432) : {0} {1} {2}", totalInterestExpenseDKK, totalInterestExpenseUSD, NotAvailable);
            Console.WriteLine("totalDividends       (Rubrik 452) : {0} {1} {2}    {3}", totalDividendsDKK, totalDividendsUSD, changeInNAVDividendsUSD, Verify(totalDividendsUSD, changeInNAVDividendsUSD));
            Console.WriteLine("totalGainLoss        (Rubrik 454) : {0} {1} {2}    {3}", totalGainLossDKK, totalGainLossUSD, totalRealizedGainLossUSD, Verify(totalGainLossUSD, totalRealizedGainLossUSD));
            Console.WriteLine("totalWithholdingTax  (Rubrik 496) : {0} {1} {2}    {3}", totalWithholdingTaxDKK, totalWithholdingTaxUSD, changeInNAVWithholdingTaxUSD, Verify(totalWithholdingTaxUSD, changeInNAVWithholdingTaxUSD));
            Console.WriteLine("totalCommissions                  : {0} {1} {2}    {3}", totalCommissionsDKK, totalCommissionsUSD, changeInNAVCommissionsUSD, Verify(totalCommissionsUSD, changeInNAVCommissionsUSD));
            Console.WriteLine("yearEndPorfolioValue (Rubrik 490) : {0} {1} {2}    {3} (*)", yearEndPorfolioValueDKK, yearEndPorfolioValueUSD, stocksValueApproximationUSD, Verify(yearEndPorfolioValueUSD, stocksValueApproximationUSD));
            Console.WriteLine("yearEndCash          (Rubrik 492) : {0} {1} {2}", yearEndCashDKK, NotAvailable, yearEndCashUSD);
            Console.WriteLine("netFXGainLossFIFO    (Rubrik 434) : ");
            Console.WriteLine("totalDepositsWithdrawals          : {0} {1} {2}    {3}", totalDepositsWithdrawalsDKK, totalDepositsWithdrawalsUSD, changeInNAVDepositsWithdrawalsUSD, Verify(totalDepositsWithdrawalsUSD, changeInNAVDepositsWithdrawalsUSD));
            Console.WriteLine("startingValue                     : {0} {1} {2}", NotAvailable, NotAvailable, changeInNAVStartingValueUSD);
            Console.WriteLine("endingValue                       : {0} {1} {2}", NotAvailable, NotAvailable, changeInNAVEndingValueUSD);
            Console.WriteLine("timeWeightedReturn (TWR)          : {0} {1} {2}", NotAvailable, NotAvailable, changeInNAVTimeWeightedReturnUSD);

            Console.WriteLine();
            Console.WriteLine("End Period Positions (CALCULATED)");
            taxReport.WritePositions();

            Console.WriteLine();
            Console.WriteLine("End Period Positions (IBKR DATA)");
            currentActivityReport.WriteOpenPositions();
            Console.WriteLine();
            Console.WriteLine("(*) This figure may account for positions that IBKR doesn't know.");

            Console.WriteLine();
            Console.WriteLine("PRESS ENTER");
            Console.ReadLine();
        }
             

    }

}