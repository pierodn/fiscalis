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
            Console.WriteLine("==================================================================================");
            TaxReport.WritePositions(startPeriodPositions);

            var events = Event.FromActivityReport(currentActivityReport, fxRates);

            TaxReport taxReport = Event.Process(startPeriodPositions, events, WriteEventSequence);


            if (WriteRealizedGainsList)
            {
                taxReport.WriteRealizedRecords();
            }

            
            Func<decimal, string, string> formatAmount = (amount, currency) => string.Format("{0,14}", amount.ToString("F2") + " " + currency);
            string DKK = "DKK", USD = "USD";
            Func<decimal, string> formatDKK = amount => string.Format("{0,14}", amount.ToString("F2") + " " + DKK);
            Func<decimal, string> formatUSD = amount => string.Format("{0,14}", amount.ToString("F2") + " " + USD);
            Func<decimal, string> formatPercentage = amount => string.Format("{0,14}", amount.ToString("F2") + "%");

            string totalInterestDKK = formatDKK(taxReport.totalInterestDKK);
            string totalInterestExpenseDKK = formatDKK(taxReport.totalInterestExpenseDKK);
            string totalDividendsDKK = formatDKK(taxReport.totalDividendsDKK);
            string totalGainLossDKK = formatDKK(taxReport.totalGainLossDKK);
            string totalWithholdingTaxDKK = formatDKK(taxReport.totalWithholdingTaxDKK);
            string totalCommissionsDKK = formatDKK(taxReport.totalCommissionsDKK);
            string totalDepositsWithdrawalsDKK = formatDKK(taxReport.totalDepositsWithdrawalsDKK);

            string totalInterestUSD = formatUSD(taxReport.totalInterestUSD);
            string totalInterestExpenseUSD = formatUSD(taxReport.totalInterestExpenseUSD);
            string totalDividendsUSD = formatUSD(taxReport.totalDividendsUSD);
            string totalGainLossUSD = formatUSD(taxReport.totalGainLossUSD);
            string totalWithholdingTaxUSD = formatUSD(taxReport.totalWithholdingTaxUSD);
            string totalCommissionsUSD = formatUSD(taxReport.totalCommissionsUSD);
            string totalDepositsWithdrawalsUSD = formatUSD(taxReport.totalDepositsWithdrawalsUSD);

            string changeInNAVInterest = formatUSD(currentActivityReport.changeInNAV.interest);
            //string changeInNAVInterestExpense = string.Format("{0,15}", currentActivityReport.changeInNAV.totalInterestExpenseUSD.ToString("F2") + " USD");
            string changeInNAVDividends = formatUSD(currentActivityReport.changeInNAV.dividends);
            //string changeInNAVGainLossMTM = formatUSD(currentActivityReport.changeInNAV.markToMarket);
            string changeInNAVWithholdingTax = formatUSD(currentActivityReport.changeInNAV.withholdingTax);
            string changeInNAVCommissions = formatUSD(currentActivityReport.changeInNAV.commissions);
            string changeInNAVDepositsWithdrawals = formatUSD(currentActivityReport.changeInNAV.depositsWithdrawals);
            string changeInNAVStartingValue = formatUSD(currentActivityReport.changeInNAV.startingValue);
            string changeInNAVEndingValue = formatUSD(currentActivityReport.changeInNAV.endingValue);
            string changeInNAVTimeWeightedReturn = formatPercentage(currentActivityReport.changeInNAV.timeWeightedReturn);
            string totalRealizedGainLoss = formatUSD(currentActivityReport.GetTotalRealizedGainLoss());
 
            // TODO EndYearPortofolioValue for SKAT (Rubrik 490), including extra shares from other accounts, excluding 


            // TODO

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
                    //Trace.Assert(calculatedPosition.positionValue > 0);
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

            string yearEndPorfolioValueUSD = formatUSD(totalPositionValue);
            string yearEndPorfolioValueDKK = formatDKK(endYearRate.Value * totalPositionValue);

            string NotAvailable = "".PadLeft(14); //"N/A".PadLeft(14);
            Func<string, string, string> Verify = (a, b) => a.Equals(b) ? "OK" : "FAILED";

            decimal endingCash = currentActivityReport.cashReport.endingCash;
            string yearEndCashUSD = formatUSD(endingCash);
            string yearEndCashDKK = formatDKK(endYearRate.Value * endingCash);

            
            

  
            Console.WriteLine();
            Console.WriteLine("Period: {0} / {1}        (CALCULATED)   (CALCULATED)  (VERIFICATION)", currentActivityReport.changeInNAV.fromDate, currentActivityReport.changeInNAV.toDate);
            Console.WriteLine("==================================================================================");
            Console.WriteLine("totalInterest        (Rubrik 431) : {0} {1} {2} => {3}", totalInterestDKK, totalInterestUSD, changeInNAVInterest, Verify(totalInterestUSD, changeInNAVInterest));
            Console.WriteLine("totalInterestExpense (Rubrik 432) : {0} {1} {2}", totalInterestExpenseDKK, totalInterestExpenseUSD, NotAvailable);
            Console.WriteLine("totalDividends       (Rubrik 452) : {0} {1} {2} => {3}", totalDividendsDKK, totalDividendsUSD, changeInNAVDividends, Verify(totalDividendsUSD, changeInNAVDividends));
            Console.WriteLine("totalGainLoss        (Rubrik 454) : {0} {1} {2} => {3}", totalGainLossDKK, totalGainLossUSD, totalRealizedGainLoss, Verify(totalGainLossUSD, totalRealizedGainLoss));
            Console.WriteLine("totalWithholdingTax  (Rubrik 496) : {0} {1} {2} => {3}", totalWithholdingTaxDKK, totalWithholdingTaxUSD, changeInNAVWithholdingTax, Verify(totalWithholdingTaxUSD, changeInNAVWithholdingTax));
            Console.WriteLine("totalCommissions                  : {0} {1} {2} => {3}", totalCommissionsDKK, totalCommissionsUSD, changeInNAVCommissions, Verify(totalCommissionsUSD, changeInNAVCommissions));
            Console.WriteLine("yearEndPorfolioValue (Rubrik 490) : {0} {1} {2}", yearEndPorfolioValueDKK, yearEndPorfolioValueUSD, NotAvailable);
            Console.WriteLine("yearEndCash          (Rubrik 492) : {0} {1} {2}", yearEndCashDKK, yearEndCashUSD, NotAvailable);
            Console.WriteLine("netFXGainLossFIFO    (Rubrik 434) : ");
            Console.WriteLine("totalDepositsWithdrawals          : {0} {1} {2} => {3}", totalDepositsWithdrawalsDKK, totalDepositsWithdrawalsUSD, changeInNAVDepositsWithdrawals, Verify(totalDepositsWithdrawalsUSD, changeInNAVDepositsWithdrawals));
            Console.WriteLine("startingValue                     : {0} {1} {2}", NotAvailable, NotAvailable, changeInNAVStartingValue);
            Console.WriteLine("endingValue                       : {0} {1} {2}", NotAvailable, NotAvailable, changeInNAVEndingValue);
            Console.WriteLine("timeWeightedReturn (TWR)          : {0} {1} {2}", NotAvailable, NotAvailable, changeInNAVTimeWeightedReturn);

            Console.WriteLine();
            Console.WriteLine("End Period Positions (CALCULATED vs report)");
            Console.WriteLine("==================================================================================");
            taxReport.WritePositions();
            Console.WriteLine();
            currentActivityReport.WriteOpenPositions();
            Console.WriteLine();
            Console.WriteLine("portfolioValue: " + formatUSD(totalPositionValue));

            Console.WriteLine();
            Console.WriteLine("PRESS ENTER");
            Console.ReadLine();
        }
             

    }

}