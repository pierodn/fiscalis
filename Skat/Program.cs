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
            const bool WriteEventSequence = false;
            const bool WriteRealizedGainsList = false;

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


            // ====================================
            // Convert into dated events
            // ====================================

            Console.WriteLine();
            Console.WriteLine("StartPeriodPositions (from previous period activity report)");
            Console.WriteLine("==================================================================================");
            TaxReport.WritePositions(startPeriodPositions);

            if (WriteEventSequence)
            {
                Console.WriteLine();
                Console.WriteLine("===================================================================");
                Console.WriteLine("CALCULATION (apply current period Events over StartPeriodPositions)");
                Console.WriteLine("===================================================================");

                Console.WriteLine();
                Console.WriteLine("Events Sequence");
                Console.WriteLine("===============");
            }


            var events = Event.FromActivityReport(currentActivityReport, fxRates);

            TaxReport report = Event.Process(startPeriodPositions, events, WriteEventSequence);

/*
            Console.WriteLine();
            Console.WriteLine("===================================================================");
            Console.WriteLine("OUTPUT (Tax Report, figures for SKAT, EndPeriodPositions)");
            Console.WriteLine("===================================================================");
            
            report.yearEndPorfolioValueUSD = currentActivityReport.GetOpenPositionsValue();

            string endYearDate = currentActivityReport.changeInNAV.toDate;
            decimal? endYearRate = fxRates.GetRate(endYearDate);
            report.yearEndPorfolioValueDKK = endYearRate.Value * report.yearEndPorfolioValueUSD;
            */

            if (WriteRealizedGainsList)
            {
                // TODO TODO.WriteRealizedGains();
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
            //string changeInNAVGainLoss = string.Format("{0,15}", currentActivityReport.changeInNAV.totalGainLossUSD.ToString("F2") + " USD");
            string changeInNAVWithholdingTax = formatUSD(currentActivityReport.changeInNAV.withholdingTax);
            string changeInNAVCommissions = formatUSD(currentActivityReport.changeInNAV.commissions);
            string changeInNAVDepositsWithdrawals = formatUSD(currentActivityReport.changeInNAV.depositsWithdrawals);
            string changeInNAVStartingValue = formatUSD(currentActivityReport.changeInNAV.startingValue);
            string changeInNAVEndingValue = formatUSD(currentActivityReport.changeInNAV.endingValue);
            string changeInNAVTimeWeightedReturn = formatPercentage(currentActivityReport.changeInNAV.timeWeightedReturn);
  
            Console.WriteLine();
            Console.WriteLine("Period: {0} / {1}        (CALCULATED)   (CALCULATED)  (ChangeInNav)", currentActivityReport.changeInNAV.fromDate, currentActivityReport.changeInNAV.toDate);
            Console.WriteLine("==================================================================================");
            Console.WriteLine("totalInterest        (Rubrik 431) : {0} {1} {2}", totalInterestDKK, totalInterestUSD, changeInNAVInterest);
            Console.WriteLine("totalInterestExpense (Rubrik 432) : {0} {1} {2}", totalInterestExpenseDKK, totalInterestExpenseUSD, formatUSD(0));
            Console.WriteLine("totalDividends       (Rubrik 452) : {0} {1} {2}", totalDividendsDKK, totalDividendsUSD, changeInNAVDividends);
            Console.WriteLine("totalGainLoss        (Rubrik 454) : {0} {1} {2}", totalGainLossDKK, totalGainLossUSD, formatUSD(0));
            Console.WriteLine("totalWithholdingTax  (Rubrik 496) : {0} {1} {2}", totalWithholdingTaxDKK, totalWithholdingTaxUSD, changeInNAVWithholdingTax);
            Console.WriteLine("totalCommissions                  : {0} {1} {2}", totalCommissionsDKK, totalCommissionsUSD, changeInNAVCommissions);
            Console.WriteLine("yearEndPorfolioValue (Rubrik 490) : ");
            Console.WriteLine("yearEndCash          (Rubrik 492) : ");
            Console.WriteLine("netFXGainLossFIFO    (Rubrik 434) : ");
            Console.WriteLine("totalDepositsWithdrawals          : {0} {1} {2}", totalDepositsWithdrawalsDKK, totalDepositsWithdrawalsUSD, changeInNAVDepositsWithdrawals);
            Console.WriteLine("startingValue                     : {0} {1} {2}", formatDKK(0), formatUSD(0), changeInNAVStartingValue);
            Console.WriteLine("endingValue                       : {0} {1} {2}", formatDKK(0), formatUSD(0), changeInNAVEndingValue);
            Console.WriteLine("timeWeightedReturn (TWR)          : {0} {1} {2}", formatPercentage(0), formatPercentage(0), changeInNAVTimeWeightedReturn);

            Console.WriteLine();
            Console.WriteLine("End Period Positions (CALCULATED compared with OpenPositions' activity report)");
            Console.WriteLine("==================================================================================");
            report.WritePositions();

            Console.WriteLine();
            currentActivityReport.WriteOpenPositions();
            /*
            Console.WriteLine();
            Console.WriteLine("Change in NAV        (from the input activity reports)");
            Console.WriteLine("======================================================");
            currentActivityReport.WriteChangeInNAV();
            */
            Console.WriteLine();
            Console.WriteLine("PRESS ENTER");
            Console.ReadLine();
        }
             

    }

}