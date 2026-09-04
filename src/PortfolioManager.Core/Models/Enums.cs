namespace PortfolioManager.Core.Models;

public enum AccountType { Margin, TFSA, RRSP, RESP }

public enum Currency { CAD, USD }

public enum Exchange { US, CA }

public enum AssetType { Stock, CallOption, PutOption }

public enum PositionDirection { Long, Short }

public enum TransactionAction { Buy, Sell, WriteOption, CloseOption, ExpireOption, Dividend, CashDeposit, CashWithdrawal }
