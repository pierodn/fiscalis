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
            var startPeriodPositions = ActivityReport.ExtractPositions(Depot + "Schwab_2022.csv");
            var currentActivityReport = ActivityReport.Import(Depot + "IBKR_2023.csv");

            // 2024
            //var startPeriodPositions = ActivityReport.ExtractPositions(Depot + "IBKR_2023.csv");
            //var currentActivityReport = ActivityReport.Import(Depot + "IBKR_2024.csv");



            Console.WriteLine();
            Console.WriteLine("Start Period Positions");
            Console.WriteLine("==================================================================================");
            TaxReport.WritePositions(startPeriodPositions);

            var events = Event.FromActivityReport(currentActivityReport, fxRates);

            TaxReport report = Event.Process(startPeriodPositions, events, WriteEventSequence);


            if (WriteRealizedGainsList)
            {
                report.WriteRealizedRecords();
            }

            
            Func<decimal, string, string> formatAmount = (amount, currency) => string.Format("{0,14}", amount.ToString("F2") + " " + currency);
            string DKK = "DKK", USD = "USD";
            Func<decimal, string> formatDKK = amount => string.Format("{0,14}", amount.ToString("F2") + " " + DKK);
            Func<decimal, string> formatUSD = amount => string.Format("{0,14}", amount.ToString("F2") + " " + USD);
            Func<decimal, string> formatPercentage = amount => string.Format("{0,14}", amount.ToString("F2") + "%");

            string totalInterestDKK = formatDKK(report.totalInterestDKK);
            string totalInterestExpenseDKK = formatDKK(report.totalInterestExpenseDKK);
            string totalDividendsDKK = formatDKK(report.totalDividendsDKK);
            string totalGainLossDKK = formatDKK(report.totalGainLossDKK);
            string totalWithholdingTaxDKK = formatDKK(report.totalWithholdingTaxDKK);
            string totalCommissionsDKK = formatDKK(report.totalCommissionsDKK);
            string totalDepositsWithdrawalsDKK = formatDKK(report.totalDepositsWithdrawalsDKK);

            string totalInterestUSD = formatUSD(report.totalInterestUSD);
            string totalInterestExpenseUSD = formatUSD(report.totalInterestExpenseUSD);
            string totalDividendsUSD = formatUSD(report.totalDividendsUSD);
            string totalGainLossUSD = formatUSD(report.totalGainLossUSD);
            string totalWithholdingTaxUSD = formatUSD(report.totalWithholdingTaxUSD);
            string totalCommissionsUSD = formatUSD(report.totalCommissionsUSD);
            string totalDepositsWithdrawalsUSD = formatUSD(report.totalDepositsWithdrawalsUSD);

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
 
            string endYearDate = currentActivityReport.changeInNAV.toDate;
            decimal? endYearRate = fxRates.GetRate(endYearDate);
            decimal portfolioValue = currentActivityReport.GetOpenPositionsValue();
            string yearEndPorfolioValueUSD = formatUSD(portfolioValue);
            string yearEndPorfolioValueDKK = formatDKK(endYearRate.Value * portfolioValue);

            string NotAvailable = "N/A".PadLeft(14);
            Func<string, string, string> Verify = (a, b) => a.Equals(b) ? "OK" : "FAILED";
            

  
            Console.WriteLine();
            Console.WriteLine("Period: {0} / {1}        (CALCULATED)   (CALCULATED)  (VERIFICATION)", currentActivityReport.changeInNAV.fromDate, currentActivityReport.changeInNAV.toDate);
            Console.WriteLine("==================================================================================");
            Console.WriteLine("totalInterest        (Rubrik 431) : {0} {1} {2} => {3}", totalInterestDKK, totalInterestUSD, changeInNAVInterest, Verify(totalInterestUSD, changeInNAVInterest));
            Console.WriteLine("totalInterestExpense (Rubrik 432) : {0} {1} {2}", totalInterestExpenseDKK, totalInterestExpenseUSD, NotAvailable);
            Console.WriteLine("totalDividends       (Rubrik 452) : {0} {1} {2} => {3}", totalDividendsDKK, totalDividendsUSD, changeInNAVDividends, Verify(totalDividendsUSD, changeInNAVDividends));
            Console.WriteLine("totalGainLoss        (Rubrik 454) : {0} {1} {2} => {3}", totalGainLossDKK, totalGainLossUSD, totalRealizedGainLoss, Verify(totalGainLossUSD, totalRealizedGainLoss));
            Console.WriteLine("totalWithholdingTax  (Rubrik 496) : {0} {1} {2} => {3}", totalWithholdingTaxDKK, totalWithholdingTaxUSD, changeInNAVWithholdingTax, Verify(totalWithholdingTaxUSD, changeInNAVWithholdingTax));
            Console.WriteLine("totalCommissions                  : {0} {1} {2} => {3}", totalCommissionsDKK, totalCommissionsUSD, changeInNAVCommissions, Verify(totalCommissionsUSD, changeInNAVCommissions));
            Console.WriteLine("yearEndPorfolioValue (Rubrik 490) : {0} {1} {2} => {3}", yearEndPorfolioValueDKK, yearEndPorfolioValueUSD, yearEndPorfolioValueUSD, Verify(yearEndPorfolioValueUSD, yearEndPorfolioValueUSD));
            Console.WriteLine("yearEndCash          (Rubrik 492) : ");
            Console.WriteLine("netFXGainLossFIFO    (Rubrik 434) : ");
            Console.WriteLine("totalDepositsWithdrawals          : {0} {1} {2} => {3}", totalDepositsWithdrawalsDKK, totalDepositsWithdrawalsUSD, changeInNAVDepositsWithdrawals, Verify(totalDepositsWithdrawalsUSD, changeInNAVDepositsWithdrawals));
            Console.WriteLine("startingValue                     : {0} {1} {2}", NotAvailable, NotAvailable, changeInNAVStartingValue);
            Console.WriteLine("endingValue                       : {0} {1} {2}", NotAvailable, NotAvailable, changeInNAVEndingValue);
            Console.WriteLine("timeWeightedReturn (TWR)          : {0} {1} {2}", NotAvailable, NotAvailable, changeInNAVTimeWeightedReturn);

            Console.WriteLine();
            Console.WriteLine("End Period Positions (CALCULATED vs report)");
            Console.WriteLine("==================================================================================");
            report.WritePositions();
            Console.WriteLine();
            currentActivityReport.WriteOpenPositions();
            Console.WriteLine();
            Console.WriteLine("portfolioValue: " + formatUSD(portfolioValue));

            Console.WriteLine();
            Console.WriteLine("PRESS ENTER");
            Console.ReadLine();
        }
             

    }

}