using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.IO;
using System.Diagnostics;

namespace InteractiveBrokers
{
    public class QueryResult
    {
        List<Position> openPositions;
        List<Trade> trades;
        List<CorporateAction> corporateActions;
        List<CashTransaction> cashTransactions;
        List<Transfer> transfers;

        public QueryResult(
            List<Position> _openPositions,
            List<Trade> _trades,
            List<CorporateAction> _corporateActions,
            List<CashTransaction> _cashTransactions,
            List<Transfer> _transfers)
        {
            openPositions = _openPositions;
            trades = _trades;
            corporateActions = _corporateActions;
            cashTransactions = _cashTransactions;
            transfers = _transfers;
        }

        public static QueryResult Load(string filePath)
        {
            const string PositionsSectionHeader         = "\"Symbol\",\"ISIN\",\"Quantity\",\"CostBasisPrice\",\"OpenPrice\",\"PositionValue\"";
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

            var openPositions = new List<Position>();
            for (int i = indexPositionsSection + 1; i < indexTradesSection; i++)
            {
                openPositions.Add(Position.Parse(lines[i]));
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

            return new QueryResult(
                openPositions,
                trades,
                corporateActions,
                cashTransactions,
                transfers
            );
        }

        private static string Trim(string s)
        {
            return s.Trim().Trim('"');
        }

        private static string[] SplitRow(string line)
        {
            return line.Split(new string[] { "\",\"" }, StringSplitOptions.None);
        }

        public class Position
        {
            public string symbol;
            public string isin;
            public decimal quantity;
            public decimal costBasisPrice; // per share // reflects IBKR's own cost basis method (often FIFO by default)
            public decimal openPrice; // average price per share
            public decimal value; // current price ??

            static public Position Parse(string line)
            {
                string[] elements = SplitRow(line);

                var position = new Position();

                position.symbol         = Trim(elements[0]);
                position.isin           = Trim(elements[1]);
                position.quantity       = decimal.Parse(Trim(elements[2]));
                position.costBasisPrice = decimal.Parse(Trim(elements[3]));
                position.openPrice      = decimal.Parse(Trim(elements[4]));
                position.value          = decimal.Parse(Trim(elements[5]));

                return position;
            }
        }

        public class Trade
        {
            public string symbol;
            public string isin;
            public string dateTime;
            public string buySell;
            public decimal quantity;
            public decimal tradePrice;
            public string currency;
            public decimal proceeds; // TODO remove? It seems to be: quantity * tradePrice
            public decimal commission; 
            public string commissionCurrency;

            static public Trade Parse(string line)
            {
                string[] elements = SplitRow(line);

                var trade = new Trade();

                trade.symbol        = Trim(elements[0]);
                trade.isin          = Trim(elements[1]);
                trade.dateTime      = Trim(elements[2]);
                trade.buySell       = Trim(elements[3]);
                trade.quantity      = decimal.Parse(Trim(elements[4]));
                trade.tradePrice    = decimal.Parse(Trim(elements[5]));
                trade.currency      = Trim(elements[6]);
                trade.proceeds      = decimal.Parse(Trim(elements[7])); // TODO remove?
                trade.commission    = decimal.Parse(Trim(elements[8]));
                trade.commissionCurrency = Trim(elements[9]);

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
                string[] elements = SplitRow(line);

                var corporateAction = new CorporateAction();

                corporateAction.symbol      = Trim(elements[0]);
                corporateAction.isin        = Trim(elements[1]);
                corporateAction.dateTime    = Trim(elements[2]);
                corporateAction.type        = Trim(elements[3]);
                corporateAction.description = Trim(elements[4]);
                corporateAction.quantity    = decimal.Parse(Trim(elements[5]));

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
                string[] elements = SplitRow(line);

                var cashTransaction = new CashTransaction();

                cashTransaction.symbol          = Trim(elements[0]);
                cashTransaction.isin            = Trim(elements[1]);
                cashTransaction.dateTime        = Trim(elements[2]);
                cashTransaction.type            = Trim(elements[3]);
                cashTransaction.amount          = decimal.Parse(Trim(elements[4]));
                cashTransaction.currency        = Trim(elements[5]);
                cashTransaction.description     = Trim(elements[6]);

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
            public string direction; // IN
            public decimal costBasis;
            public decimal transferPrice;
            public string description;

            static public Transfer Parse(string line)
            {
                string[] elements = SplitRow(line);

                var transfer = new Transfer();

                transfer.symbol         = Trim(elements[0]);
                transfer.isin           = Trim(elements[1]);
                transfer.dateTime       = Trim(elements[2]);
                transfer.type           = Trim(elements[3]);
                transfer.quantity       = decimal.Parse(Trim(elements[4]));
                transfer.direction      = Trim(elements[5]);
                transfer.costBasis      = decimal.Parse(Trim(elements[6]));
                transfer.transferPrice  = decimal.Parse(Trim(elements[7]));
                transfer.description    = Trim(elements[8]);

                return transfer;
            }
        }

    }



}
