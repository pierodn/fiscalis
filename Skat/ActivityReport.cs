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

        public static string[] SplitRow(string line)
        {
            return line.Split(new string[] { "\",\"" }, StringSplitOptions.None);
        }
    }

    public class Position
    {
        public string symbol;
        public decimal quantity;
        public decimal totalCostUSD; // This is initially coming from the OpenPositions of the previousActivityReport (if any).
        public decimal totalCostDKK; // This should somehow be inserted manually because it depends on the DKK-USD rates, which are generally unknown.
        // public decimal averageCostDKK; // This is gennemsnitsmetoden: averageCostDKK = totalCostDKK / quantity;

        static public Position Parse(string line)
        {
            return Parse(line, false);
        }

        static public Position Parse(string line, bool asktotalCostDKK)
        {
            string[] elements = Utils.SplitRow(line);

            var position = new Position();

            position.symbol = Utils.Trim(elements[0]);
            position.quantity = decimal.Parse(Utils.Trim(elements[2]));

            decimal costBasisPrice = decimal.Parse(Utils.Trim(elements[3])); // This is the averagePricePerShare.
            decimal openPrice = decimal.Parse(Utils.Trim(elements[4])); // TODO maybe not needed as it looks generally the same as the costBasisPrice
            decimal positionValue = decimal.Parse(Utils.Trim(elements[5])); // This is maybe the position value today: todaysPrice * quantity 

            position.totalCostUSD = costBasisPrice * position.quantity;

            bool isTotalCostDKKPresent = elements.Length > 6;
            if (isTotalCostDKKPresent)
            {
                position.totalCostDKK = decimal.Parse(Utils.Trim(elements[6])); 
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
                        Trace.Assert(success, "Error parsing decimal");
                    }
                }
            }

            return position;
        }

        // TODO void ApplyBuy(decimal buyQuantity, decimal tradePrice)
        // TODO decimal ApplySell(decimal sellQuantity, decimal tradePrice)
        
        public override string ToString()
        {
            string symbol = this.symbol.PadRight(8);
            string quantity = this.quantity.ToString().PadRight(8);
            string totalCostUSD = this.totalCostUSD.ToString("F2", CultureInfo.InvariantCulture).PadRight(10);
            string totalCostDKK = this.totalCostDKK.ToString("F2", CultureInfo.InvariantCulture).PadRight(10);
            string USD = "USD".PadRight(8);
            string DKK = "DKK".PadRight(8);

            return string.Format("{0} {1} {2} {3} {4} {5}", symbol, quantity, totalCostUSD, USD, totalCostDKK, DKK);
        }
    }

    public class Trade
    {
        public string symbol;
        public string dateTime;
        public string buySell; // removeable as it is in the sign of quantity
        public decimal quantity;
        public decimal tradePrice;
        public string currency;
        public decimal proceeds; // = quantity * tradePrice
        public decimal commission;
        public string commissionCurrency;

        static public Trade Parse(string line)
        {
            string[] elements = Utils.SplitRow(line);

            var trade = new Trade();

            trade.symbol = Utils.Trim(elements[0]);
            trade.dateTime = Utils.Trim(elements[2]);
            trade.buySell = Utils.Trim(elements[3]);
            trade.quantity = decimal.Parse(Utils.Trim(elements[4]));
            trade.tradePrice = decimal.Parse(Utils.Trim(elements[5]));
            trade.currency = Utils.Trim(elements[6]);
            trade.proceeds = decimal.Parse(Utils.Trim(elements[7]));
            trade.commission = decimal.Parse(Utils.Trim(elements[8]));
            trade.commissionCurrency = Utils.Trim(elements[9]);

            return trade;
        }
    }

    public class CorporateAction
    {
        public string symbol;
        public string dateTime;
        public string type; // "FS" stands for SPLIT. See description
        public string description;
        public decimal quantity;

        static public CorporateAction Parse(string line)
        {
            string[] elements = Utils.SplitRow(line);

            var corporateAction = new CorporateAction();

            corporateAction.symbol = Utils.Trim(elements[0]);
            corporateAction.dateTime = Utils.Trim(elements[2]);
            corporateAction.type = Utils.Trim(elements[3]);
            corporateAction.description = Utils.Trim(elements[4]);
            corporateAction.quantity = decimal.Parse(Utils.Trim(elements[5]));

            return corporateAction;
        }
    }

    public class CashTransaction
    {
        public string symbol;
        public string dateTime;
        public string type; // "Withholding Tax", "Dividends", "Payment In Lieu Of Dividends"
        public decimal amount;
        public string currency;
        public string description;

        static public CashTransaction Parse(string line)
        {
            string[] elements = Utils.SplitRow(line);

            var cashTransaction = new CashTransaction();

            cashTransaction.symbol = Utils.Trim(elements[0]);
            cashTransaction.dateTime = Utils.Trim(elements[2]);
            cashTransaction.type = Utils.Trim(elements[3]);
            cashTransaction.amount = decimal.Parse(Utils.Trim(elements[4]));
            cashTransaction.currency = Utils.Trim(elements[5]);
            cashTransaction.description = Utils.Trim(elements[6]);

            return cashTransaction;
        }
    }

    public class Transfer
    {
        public string symbol;
        public string dateTime;
        public string type; // "ACATS"
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
            transfer.dateTime = Utils.Trim(elements[2]);
            transfer.type = Utils.Trim(elements[3]); // TEMP???
            transfer.quantity = decimal.Parse(Utils.Trim(elements[4]));
            transfer.direction = Utils.Trim(elements[5]); // TEMP
            transfer.costBasis = decimal.Parse(Utils.Trim(elements[6]));
            transfer.transferPrice = decimal.Parse(Utils.Trim(elements[7]));
            transfer.description = Utils.Trim(elements[8]); // TEMP

            return transfer;
        }
    }

    public class ActivityReport
    {
        public List<Position> openPositions;
        public List<Trade> trades;
        public List<CorporateAction> corporateActions;
        public List<CashTransaction> cashTransactions;
        public List<Transfer> transfers;
        // TODO List<DepositWithdrawal> DepositsWithdrawals;

        public ActivityReport(
            List<Position> openPositions,
            List<Trade> trades,
            List<CorporateAction> corporateActions,
            List<CashTransaction> cashTransactions,
            List<Transfer> transfers)
        {
            this.openPositions = openPositions;
            this.trades = trades;
            this.corporateActions = corporateActions;
            this.cashTransactions = cashTransactions;
            this.transfers = transfers;
        }

        public void WriteOpenPositions()
        {
            foreach (Position position in this.openPositions.OrderBy(x => x.symbol))
            {
                Console.WriteLine(position);
            }
        }

        public static List<Position> ExtractPositions(string filePath)
        {
            Console.WriteLine();
            Console.WriteLine("EXTRACTING initial positions from Activity Report: " + filePath);

            ActivityReport report = ActivityReport.Import(filePath, true);
            return report.openPositions;
        }


        public static ActivityReport Import(string filePath)
        {
            return Import(filePath, false);
        }

        public static ActivityReport Import(string filePath, bool askTotalCostDKK)
        {
            if (!askTotalCostDKK)
            {
                Console.WriteLine();
                Console.WriteLine("IMPORTING IBKR Activity Report: " + filePath);
            }

            // TODO maybe the FIFO CostBasisPrice for reference, but can get rid of OpenPrice?
            const string PositionsSectionHeader = "\"Symbol\",\"ISIN\",\"Quantity\",\"CostBasisPrice\",\"OpenPrice\",\"PositionValue\"";
            // TODO can remove Buy/Sell, CurrencyPrimary, IBCommissionCurrency. 
            // TODO Can keep Proceeds maybe for consistency-check?
            const string TradesSectionHeader            = "\"Symbol\",\"ISIN\",\"DateTime\",\"Buy/Sell\",\"Quantity\",\"TradePrice\",\"CurrencyPrimary\",\"Proceeds\",\"IBCommission\",\"IBCommissionCurrency\"";
            const string CorporateActionsSectionHeader  = "\"Symbol\",\"ISIN\",\"Date/Time\",\"Type\",\"Description\",\"Quantity\"";
            const string CashTransactionsSectionHeader  = "\"Symbol\",\"ISIN\",\"Date/Time\",\"Type\",\"Amount\",\"CurrencyPrimary\",\"Description\"";
            const string TransfersSectionHeader         = "\"Symbol\",\"ISIN\",\"DateTime\",\"Type\",\"Quantity\",\"Direction\",\"CostBasis\",\"TransferPrice\",\"Description\"";

            string[] lines = File.ReadAllLines(filePath);

            int indexPositionsSection           = -1;
            int indexTradesSection              = -1;
            int indexCorporateActionsSection    = -1;
            int indexCashTransactionsSection    = -1;
            int indexTransfersSection           = -1;

            for (int i = 0; i < lines.Length; i++)
            {
                if (lines[i].IndexOf(PositionsSectionHeader) == 0)          indexPositionsSection = i;
                if (lines[i].IndexOf(TradesSectionHeader) == 0)             indexTradesSection = i;
                if (lines[i].IndexOf(CorporateActionsSectionHeader) == 0)   indexCorporateActionsSection = i;
                if (lines[i].IndexOf(CashTransactionsSectionHeader) == 0)   indexCashTransactionsSection = i;
                if (lines[i].IndexOf(TransfersSectionHeader) == 0)          indexTransfersSection = i;
            }

            Trace.Assert(indexPositionsSection == 0);
            Trace.Assert(indexTradesSection > indexPositionsSection);
            Trace.Assert(indexCorporateActionsSection > indexTradesSection);
            Trace.Assert(indexCashTransactionsSection > indexCorporateActionsSection);
            Trace.Assert(indexTransfersSection > indexCashTransactionsSection);

            var positions = new List<Position>();
            for (int i = indexPositionsSection + 1; i < indexTradesSection; i++)
            {
                positions.Add(Position.Parse(lines[i], askTotalCostDKK));
            }

            var trades = new List<Trade>();
            for (int i = indexTradesSection + 1; i < indexCorporateActionsSection; i++)
            {
                trades.Add(Trade.Parse(lines[i]));
            }

            var corporateActions = new List<CorporateAction>();
            for (int i = indexCorporateActionsSection + 1; i < indexCashTransactionsSection; i++)
            {
                corporateActions.Add(CorporateAction.Parse(lines[i]));
            }

            var cashTransactions = new List<CashTransaction>();
            for (int i = indexCashTransactionsSection + 1; i < indexTransfersSection; i++)
            {
                cashTransactions.Add(CashTransaction.Parse(lines[i]));
            }

            var transfers = new List<Transfer>();
            for (int i = indexTransfersSection + 1; i < lines.Length; i++)
            {
                transfers.Add(Transfer.Parse(lines[i]));
            }

            return new ActivityReport(
                positions,
                trades,
                corporateActions,
                cashTransactions,
                transfers
            );
        }
    }

}
