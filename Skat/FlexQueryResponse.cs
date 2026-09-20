using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.IO;
using System.Diagnostics;

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

    public class OpenPosition
    {
        public string symbol;
        public string isin;
        public decimal quantity;
        //public decimal costBasisPrice; // per unit but it's FIFO (IBKR's default).
        //public decimal openPrice; // average price per share
        public decimal value; // current price ??

        static public OpenPosition Parse(string line)
        {
            string[] elements = Utils.SplitRow(line);

            var openPosition = new OpenPosition();

            openPosition.symbol = Utils.Trim(elements[0]);
            openPosition.isin = Utils.Trim(elements[1]);
            openPosition.quantity = decimal.Parse(Utils.Trim(elements[2]));
            //openPosition.costBasisPrice = decimal.Parse(Utils.Trim(elements[3]));
            //openPosition.openPrice = decimal.Parse(Utils.Trim(elements[4]));
            openPosition.value = decimal.Parse(Utils.Trim(elements[5]));

            return openPosition;
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
        public string currency;
        public decimal proceeds; // TODO remove? It seems to be: quantity * tradePrice
        public decimal commission;
        public string commissionCurrency;

        static public Trade Parse(string line)
        {
            string[] elements = Utils.SplitRow(line);

            var trade = new Trade();

            trade.symbol = Utils.Trim(elements[0]);
            trade.isin = Utils.Trim(elements[1]);
            trade.dateTime = Utils.Trim(elements[2]);
            trade.buySell = Utils.Trim(elements[3]);
            trade.quantity = decimal.Parse(Utils.Trim(elements[4]));
            trade.tradePrice = decimal.Parse(Utils.Trim(elements[5]));
            trade.currency = Utils.Trim(elements[6]);
            trade.proceeds = decimal.Parse(Utils.Trim(elements[7])); // TODO remove?
            trade.commission = decimal.Parse(Utils.Trim(elements[8]));
            trade.commissionCurrency = Utils.Trim(elements[9]);

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
            corporateAction.quantity = decimal.Parse(Utils.Trim(elements[5]));

            return corporateAction;
        }
    }

    public class CashTransaction
    {
        public string symbol;
        public string isin;
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
            cashTransaction.isin = Utils.Trim(elements[1]);
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
        public string isin;
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
            transfer.isin = Utils.Trim(elements[1]);
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

    public class FlexQueryResponse
    {
        public List<OpenPosition> openPositions;
        public List<Trade> trades;
        public List<CorporateAction> corporateActions;
        public List<CashTransaction> cashTransactions;
        public List<Transfer> transfers;

        public FlexQueryResponse(
            List<OpenPosition> openPositions,
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

        public static FlexQueryResponse Import(string filePath)
        {
            // TODO maybe the FIFO CostBasisPrice for reference, but can get rid of OpenPrice, PositionValue.
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

            var openPositions = new List<OpenPosition>();
            for (int i = indexPositionsSection + 1; i < indexTradesSection; i++)
            {
                openPositions.Add(OpenPosition.Parse(lines[i]));
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

            return new FlexQueryResponse(
                openPositions,
                trades,
                corporateActions,
                cashTransactions,
                transfers
            );
        }
    }

}
