using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.IO;
using System.Globalization;
using System.Diagnostics;
using MyDiagnostics;


namespace InteractiveBrokers
{
    public class Utils
    {
        public static string Trim(string s)
        {
            return s.Trim().Trim('"');
        }

        public static decimal ParseDecimal(string s)
        {
            return decimal.Parse(s.Trim().Trim('"'), CultureInfo.InvariantCulture);
        }

        public static string[] SplitRow(string line)
        {
            return line.Split(new string[] { "\",\"" }, StringSplitOptions.None);
        }

        public static Func<decimal, string> FormatUSD = amount => string.Format("{0,14}", amount.ToString("F2") + " USD");
        public static Func<decimal, string> FormatDKK = amount => string.Format("{0,14}", amount.ToString("F2") + " DKK"); 
    }

    public class ChangeInNAV
    {
        public string fromDate;
        public string toDate;
        public decimal startingValue;
        public decimal markToMarket;
        public decimal depositsWithdrawals;
        public decimal grantActivity;
        public decimal dividends;
        public decimal withholdingTax;
        public decimal interest;
        public decimal commissions;
        public decimal realized;
        public decimal endingValue;
        public decimal timeWeightedReturn;

        static public ChangeInNAV Parse(string line)
        {
            string[] elements = Utils.SplitRow(line);

            var changeInNAV = new ChangeInNAV();

            changeInNAV.fromDate = Utils.Trim(elements[0]);
            changeInNAV.toDate = Utils.Trim(elements[1]);
            changeInNAV.startingValue = Utils.ParseDecimal(elements[2]);
            changeInNAV.markToMarket = Utils.ParseDecimal(elements[3]);
            changeInNAV.depositsWithdrawals = Utils.ParseDecimal(elements[4]);
            changeInNAV.grantActivity = Utils.ParseDecimal(elements[5]);
            changeInNAV.dividends = Utils.ParseDecimal(elements[6]);
            changeInNAV.withholdingTax = Utils.ParseDecimal(elements[7]);
            changeInNAV.interest = Utils.ParseDecimal(elements[8]);
            changeInNAV.commissions = Utils.ParseDecimal(elements[9]);
            changeInNAV.realized = Utils.ParseDecimal(elements[10]);
            changeInNAV.endingValue = Utils.ParseDecimal(elements[11]);
            changeInNAV.timeWeightedReturn = Utils.ParseDecimal(elements[12]);

            return changeInNAV;
        }
    }

    public class RealizedGainLoss
    {
        public string symbol;
        public string isin;
        public decimal totalRealizedPnl;

        static public RealizedGainLoss Parse(string line)
        {
            string[] elements = Utils.SplitRow(line);

            var realizedGainLoss = new RealizedGainLoss();
            realizedGainLoss.symbol = Utils.Trim(elements[0]);
            realizedGainLoss.isin = Utils.Trim(elements[1]);
            realizedGainLoss.totalRealizedPnl = Utils.ParseDecimal(elements[2]);

            return realizedGainLoss;
        }
    }

    public class CashReport
    {
        public string fromDate;
        public string toDate;
        public decimal startingCash;
        public decimal netTradesSales;
        public decimal netTradesPurchases;
        public decimal endingCash;

        static public CashReport Parse(string line)
        {
            string[] elements = Utils.SplitRow(line);

            var cashReport = new CashReport();

            cashReport.fromDate = Utils.Trim(elements[0]);
            cashReport.toDate = Utils.Trim(elements[1]);
            cashReport.startingCash = Utils.ParseDecimal(elements[2]);
            cashReport.netTradesSales = Utils.ParseDecimal(elements[3]);
            cashReport.netTradesPurchases = Utils.ParseDecimal(elements[4]);
            cashReport.endingCash = Utils.ParseDecimal(elements[5]);

            return cashReport;
        }
    }

    public class Position
    {
        public string symbol;
        public string isin;
        public decimal quantity;
        public decimal totalCostUSD; // This is initially coming from the OpenPositions of the previousActivityReport (if any).
        public decimal totalCostDKK; // This should somehow be inserted manually because it depends on the DKK-USD rates, which are generally unknown.
        public decimal positionValue;

        static public Position Parse(string line)
        {
            return Parse(line, false);
        }

        static public Position Parse(string line, bool asktotalCostDKK)
        {
            string[] elements = Utils.SplitRow(line);

            var position = new Position();

            position.symbol = Utils.Trim(elements[0]);
            position.isin = Utils.Trim(elements[1]);
            position.quantity = Utils.ParseDecimal(elements[2]);

            decimal costBasisPrice = Utils.ParseDecimal(elements[3]); // This is the averagePricePerShare.

            position.totalCostUSD = costBasisPrice * position.quantity;
            position.positionValue = Utils.ParseDecimal(elements[4]); // This is the end-year position value today (endYearPrice * quantity) 

            bool isTotalCostDKKPresent = elements.Length > 5;
            if (isTotalCostDKKPresent)
            {
                position.totalCostDKK = Utils.ParseDecimal(elements[5]); 
            } 
            else if (asktotalCostDKK)
            {
                Console.Write("==> Enter the totalCostDkk for the {0} shares of {1}: ", position.quantity, position.symbol);
                string input = Console.ReadLine();
                if (input.Length > 0)
                {
                    Boolean success = decimal.TryParse(input, out position.totalCostDKK);
                    if (!success)
                    {
                        throw new InvalidOperationException("Error parsing decimal");

                    }
                }
            }

            return position;
        }

        static public void WritePositions(Dictionary<string, Position> positions)
        {
            WritePositions(positions.Values.ToList());
        }

        static public void WritePositions(List<Position> positions)
        {
            decimal totalCostUSD = 0;
            decimal totalCostDKK = 0;
            decimal totalValue = 0;

            Console.WriteLine("================================ TotalCost ==== TotalCost ==== Value ===================");
            foreach (Position position in positions.OrderBy(p => p.symbol))
            {
                Console.WriteLine(position);
                totalCostUSD += position.totalCostUSD;
                totalCostDKK += position.totalCostDKK;
                totalValue += position.positionValue;
            }
            Console.WriteLine("{0} {1} {2}".PadLeft(42), Utils.FormatUSD(totalCostUSD), Utils.FormatDKK(totalCostDKK), Utils.FormatUSD(totalValue));
        }

        public override string ToString()
        {
            string symbol = this.symbol.PadRight(8);
            string isin = this.isin.PadRight(14);
            string quantity = this.quantity.ToString().PadRight(6);
            string totalCostUSD = Utils.FormatUSD(this.totalCostUSD);
            string totalCostDKK = Utils.FormatDKK(this.totalCostDKK);
            string positionValueUSD = Utils.FormatUSD(this.positionValue);

            return string.Format("{0} {1} {2} {3} {4} {5}", isin, symbol, quantity, totalCostUSD, totalCostDKK, positionValueUSD);
        }
    }

    public class Trade
    {
        public string symbol;
        public string isin;
        public string dateTime;
        public string buySell; // removeable as it is in the sign of quantity
        public decimal quantity;
        public decimal tradePrice;
        public decimal proceeds; // = quantity * tradePrice
        public decimal commission;

        static public Trade Parse(string line)
        {
            string[] elements = Utils.SplitRow(line);

            var trade = new Trade();

            trade.symbol = Utils.Trim(elements[0]);
            trade.isin = Utils.Trim(elements[1]);
            trade.dateTime = Utils.Trim(elements[2]);
            trade.buySell = Utils.Trim(elements[3]);
            trade.quantity = Utils.ParseDecimal(elements[4]);
            trade.tradePrice = Utils.ParseDecimal(elements[5]);
            trade.proceeds = Utils.ParseDecimal(elements[6]);
            trade.commission = Utils.ParseDecimal(elements[7]);

            return trade;
        }
    }

    public class CorporateAction
    {
        public string symbol;
        public string isin;
        public string dateTime;
        public string type; // "FS" stands for SPLIT. See description
        public string description;
        public decimal quantity;

        static public CorporateAction Parse(string line)
        {
            string[] elements = Utils.SplitRow(line);

            var corporateAction = new CorporateAction();

            corporateAction.symbol = Utils.Trim(elements[0]);
            corporateAction.isin = Utils.Trim(elements[1]);
            corporateAction.dateTime = Utils.Trim(elements[2]);
            corporateAction.type = Utils.Trim(elements[3]);
            corporateAction.description = Utils.Trim(elements[4]);
            corporateAction.quantity = Utils.ParseDecimal(elements[5]);

            return corporateAction;
        }
    }

    public class GrantActivity
    {
        public string symbol;
        public string isin;
        public string vestingDate;
        public decimal quantity;
        public decimal price;
        public decimal value;

        static public GrantActivity Parse(string line)
        {
            string[] elements = Utils.SplitRow(line);

            var grantActivity = new GrantActivity();

            grantActivity.symbol = Utils.Trim(elements[0]);
            grantActivity.isin = Utils.Trim(elements[1]);
            grantActivity.vestingDate = Utils.Trim(elements[2]);
            grantActivity.quantity = Utils.ParseDecimal(elements[3]);
            grantActivity.price = Utils.ParseDecimal(elements[4]);
            grantActivity.value = Utils.ParseDecimal(elements[5]);

            return grantActivity;
        }
    }

    public class CashTransaction
    {
        public string symbol;
        public string isin;
        public string dateTime;
        public string type; // "Withholding Tax", "Dividends", "Payment In Lieu Of Dividends", "Deposits/Withdrawals", "Broker Interest Received"
        public decimal amount;
        public string currency;
        public string description;

        static public CashTransaction Parse(string line)
        {
            string[] elements = Utils.SplitRow(line);

            var cashTransaction = new CashTransaction();

            cashTransaction.symbol = Utils.Trim(elements[0]);
            cashTransaction.isin = Utils.Trim(elements[1]);
            cashTransaction.dateTime = Utils.Trim(elements[2]);
            cashTransaction.type = Utils.Trim(elements[3]);
            cashTransaction.amount = Utils.ParseDecimal(elements[4]);
            cashTransaction.currency = Utils.Trim(elements[5]);
            cashTransaction.description = Utils.Trim(elements[6]);

            return cashTransaction;
        }
    }

    public class Transfer
    {
        public string symbol;
        public string isin;
        public string dateTime;
        public string type;
        public decimal quantity;
        public string direction; // IN // TEMP
        public decimal costBasis;
        public decimal transferPrice;
        public string description; // TEMP

        static public Transfer Parse(string line)
        {
            string[] elements = Utils.SplitRow(line);

            var transfer = new Transfer();

            transfer.symbol = Utils.Trim(elements[0]);
            transfer.isin = Utils.Trim(elements[1]);
            transfer.dateTime = Utils.Trim(elements[2]);
            transfer.type = Utils.Trim(elements[3]); // TEMP???
            transfer.quantity = Utils.ParseDecimal(elements[4]);
            transfer.direction = Utils.Trim(elements[5]); // TEMP
            transfer.costBasis = Utils.ParseDecimal(elements[6]);
            transfer.transferPrice = Utils.ParseDecimal(elements[7]);
            transfer.description = Utils.Trim(elements[8]); // TEMP

            return transfer;
        }
    }

    public class ActivityReport
    {
        public ChangeInNAV changeInNAV;
        public List<RealizedGainLoss> realizedGainLosses;
        public CashReport cashReport;
        public List<Position> openPositions;
        public List<Trade> trades;
        public List<CorporateAction> corporateActions;
        public List<GrantActivity> grants;
        public List<CashTransaction> cashTransactions;
        public List<Transfer> transfers;

        public ActivityReport(
            ChangeInNAV changeInNAV,
            List<RealizedGainLoss> realizedGainLosses,
            CashReport cashReport,
            List<Position> openPositions,
            List<Trade> trades,
            List<CorporateAction> corporateActions,
            List<GrantActivity> grants,
            List<CashTransaction> cashTransactions,
            List<Transfer> transfers)
        {
            this.changeInNAV = changeInNAV;
            this.realizedGainLosses = realizedGainLosses;
            this.cashReport = cashReport;
            this.openPositions = openPositions;
            this.trades = trades;
            this.corporateActions = corporateActions;
            this.grants = grants;
            this.cashTransactions = cashTransactions;
            this.transfers = transfers;
        }

        public void WriteChangeInNAV()
        {
            Console.WriteLine("fromDate             : {0}", changeInNAV.fromDate);
            Console.WriteLine("toDate               : {0}", changeInNAV.toDate);
            Console.WriteLine("startingValue        : {0:F2} USD", changeInNAV.startingValue);
            Console.WriteLine("markToMarket         : {0:F2} USD", changeInNAV.markToMarket);
            Console.WriteLine("depositsWithdrawals  : {0:F2} USD", changeInNAV.depositsWithdrawals);
            Console.WriteLine("grantActivity        : {0:F2} USD", changeInNAV.grantActivity);
            Console.WriteLine("dividends            : {0:F2} USD", changeInNAV.dividends);
            Console.WriteLine("withholdingTax       : {0:F2} USD", changeInNAV.withholdingTax);
            Console.WriteLine("interest             : {0:F2} USD", changeInNAV.interest);
            Console.WriteLine("commissions          : {0:F2} USD", changeInNAV.commissions);
            Console.WriteLine("realized             : {0:F2} USD", changeInNAV.realized);
            Console.WriteLine("endingValue          : {0:F2} USD", changeInNAV.endingValue);
            Console.WriteLine("timeWeightedReturn   : {0:F2} %", changeInNAV.timeWeightedReturn);
        }

        public void WriteOpenPositions()
        {
            Position.WritePositions(openPositions);
        }

        public decimal GetOpenPositionsValue()
        {
            decimal totalValue = 0;
            foreach (Position position in this.openPositions)
            {
                totalValue += position.positionValue;
            }
            return totalValue;
        }

        public Dictionary<string, Position> GetOpenPositions()
        {
            return this.openPositions.ToDictionary(p => p.isin);
        }


        public static List<Position> ExtractPositions(string filePath)
        {
            Console.WriteLine("EXTRACTING StartPeriodPositions (from previous activity report): " + filePath);

            ActivityReport report = ActivityReport.Import(filePath, true);
            return report.openPositions;
        }

        public decimal GetTotalRealizedGainLoss()
        {
            return realizedGainLosses.ToDictionary(r => r.isin)[""].totalRealizedPnl;
        }


        public static ActivityReport Import(string filePath)
        {
            return Import(filePath, false);
        }

        public static ActivityReport Import(string filePath, bool askTotalCostDKK)
        {
            if (!askTotalCostDKK)
            {
                Console.WriteLine("IMPORTING current activity report: " + filePath);
            }

            // IBKR Activity Report section headers
            const string ChangeInNAVSectionHeader           = "\"FromDate\",\"ToDate\",\"StartingValue\",\"Mtm\",\"DepositsWithdrawals\",\"GrantActivity\",\"Dividends\",\"WithholdingTax\",\"Interest\",\"Commissions\",\"Realized\",\"EndingValue\",\"TWR\"";
            const string RealizedPerfInBaseSectionHeader    = "\"Symbol\",\"ISIN\",\"TotalRealizedPnl\"";
            const string CashReportSectionHeader            = "\"FromDate\",\"ToDate\",\"StartingCash\",\"NetTradesSales\",\"NetTradesPurchases\",\"EndingCash\"";
            const string PositionsSectionHeader             = "\"Symbol\",\"ISIN\",\"Quantity\",\"CostBasisPrice\",\"PositionValue\"";
            const string TradesSectionHeader                = "\"Symbol\",\"ISIN\",\"DateTime\",\"Buy/Sell\",\"Quantity\",\"TradePrice\",\"Proceeds\",\"IBCommission\"";
            const string CorporateActionsSectionHeader      = "\"Symbol\",\"ISIN\",\"Date/Time\",\"Type\",\"Description\",\"Quantity\"";
            const string GrantActivityHeader                = "\"Symbol\",\"ISIN\",\"VestingDate\",\"Quantity\",\"Price\",\"Value\"";
            const string CashTransactionsSectionHeader      = "\"Symbol\",\"ISIN\",\"Date/Time\",\"Type\",\"Amount\",\"CurrencyPrimary\",\"Description\"";
            const string TransfersSectionHeader             = "\"Symbol\",\"ISIN\",\"DateTime\",\"Type\",\"Quantity\",\"Direction\",\"CostBasis\",\"TransferPrice\",\"Description\"";

            string[] lines = File.ReadAllLines(filePath);

            int changeInNAVSectionIndex         = -1;
            int realizedPerfInBaseSectionIndex  = -1;
            int cashReportSectionIndex          = -1;
            int positionsSectionIndex           = -1;
            int tradesSectionIndex              = -1;
            int corporateActionsSectionIndex    = -1;
            int grantActivitySectionIndex       = -1;
            int cashTransactionsSectionIndex    = -1;
            int transfersSectionIndex           = -1;

            for (int i = 0; i < lines.Length; i++)
            {
                if (lines[i].IndexOf(ChangeInNAVSectionHeader) == 0)        changeInNAVSectionIndex = i;
                if (lines[i].IndexOf(RealizedPerfInBaseSectionHeader) == 0) realizedPerfInBaseSectionIndex = i;
                if (lines[i].IndexOf(CashReportSectionHeader) == 0)         cashReportSectionIndex = i;
                if (lines[i].IndexOf(PositionsSectionHeader) == 0)          positionsSectionIndex = i;
                if (lines[i].IndexOf(TradesSectionHeader) == 0)             tradesSectionIndex = i;
                if (lines[i].IndexOf(CorporateActionsSectionHeader) == 0)   corporateActionsSectionIndex = i;
                if (lines[i].IndexOf(GrantActivityHeader) == 0)             grantActivitySectionIndex = i;
                if (lines[i].IndexOf(CashTransactionsSectionHeader) == 0)   cashTransactionsSectionIndex = i;
                if (lines[i].IndexOf(TransfersSectionHeader) == 0)          transfersSectionIndex = i;
            }

            Precondition.ASSERT(changeInNAVSectionIndex == 0);
            Precondition.ASSERT(realizedPerfInBaseSectionIndex > changeInNAVSectionIndex);
            Precondition.ASSERT(cashReportSectionIndex > realizedPerfInBaseSectionIndex);
            Precondition.ASSERT(positionsSectionIndex > cashReportSectionIndex);
            Precondition.ASSERT(tradesSectionIndex > positionsSectionIndex);
            Precondition.ASSERT(corporateActionsSectionIndex > tradesSectionIndex);
            Precondition.ASSERT(grantActivitySectionIndex > corporateActionsSectionIndex);
            Precondition.ASSERT(cashTransactionsSectionIndex > grantActivitySectionIndex);
            Precondition.ASSERT(transfersSectionIndex > cashTransactionsSectionIndex);

            var changeInNAV = ChangeInNAV.Parse(lines[changeInNAVSectionIndex+1]);

            var realizedGainLosses = new List<RealizedGainLoss>();
            for (int i = realizedPerfInBaseSectionIndex + 1; i < cashReportSectionIndex; i++)
            {
                realizedGainLosses.Add(RealizedGainLoss.Parse(lines[i]));
            }

            var cashReport = CashReport.Parse(lines[cashReportSectionIndex + 1]);

            var positions = new List<Position>();
            for (int i = positionsSectionIndex + 1; i < tradesSectionIndex; i++)
            {
                positions.Add(Position.Parse(lines[i], askTotalCostDKK));
            }

            var trades = new List<Trade>();
            for (int i = tradesSectionIndex + 1; i < corporateActionsSectionIndex; i++)
            {
                trades.Add(Trade.Parse(lines[i]));
            }

            var corporateActions = new List<CorporateAction>();
            for (int i = corporateActionsSectionIndex + 1; i < grantActivitySectionIndex; i++)
            {
                corporateActions.Add(CorporateAction.Parse(lines[i]));
            }

            var grants = new List<GrantActivity>();
            for (int i = grantActivitySectionIndex + 1; i < cashTransactionsSectionIndex; i++)
            {
                grants.Add(GrantActivity.Parse(lines[i]));
            }

            var cashTransactions = new List<CashTransaction>();
            for (int i = cashTransactionsSectionIndex + 1; i < transfersSectionIndex; i++)
            {
                cashTransactions.Add(CashTransaction.Parse(lines[i]));
            }

            var transfers = new List<Transfer>();
            for (int i = transfersSectionIndex + 1; i < lines.Length; i++)
            {
                transfers.Add(Transfer.Parse(lines[i]));
            }

            return new ActivityReport(
                changeInNAV,
                realizedGainLosses,
                cashReport,
                positions,
                trades,
                corporateActions,
                grants,
                cashTransactions,
                transfers
            );
        }
    }

}
