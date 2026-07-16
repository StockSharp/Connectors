namespace StockSharp.KabuStation.Native.Model;

internal sealed class KabuStationClosePosition
{
	[JsonProperty("HoldID")]
	public string HoldId { get; set; }

	[JsonProperty("Qty")]
	public long Quantity { get; set; }
}

internal sealed class KabuStationStockStopOrder
{
	[JsonProperty("TriggerSec")]
	public int TriggerSecurity { get; set; } = 1;
	[JsonProperty("TriggerPrice")]
	public decimal TriggerPrice { get; set; }
	[JsonProperty("UnderOver")]
	public int Comparison { get; set; }
	[JsonProperty("AfterHitOrderType")]
	public int AfterHitOrderType { get; set; }
	[JsonProperty("AfterHitPrice")]
	public decimal AfterHitPrice { get; set; }
}

internal sealed class KabuStationDerivativeStopOrder
{
	[JsonProperty("TriggerPrice")]
	public decimal TriggerPrice { get; set; }
	[JsonProperty("UnderOver")]
	public int Comparison { get; set; }
	[JsonProperty("AfterHitOrderType")]
	public int AfterHitOrderType { get; set; }
	[JsonProperty("AfterHitPrice")]
	public decimal AfterHitPrice { get; set; }
}

internal sealed class KabuStationStockOrderRequest
{
	[JsonProperty("Symbol")]
	public string Symbol { get; set; }
	[JsonProperty("Exchange")]
	public int Exchange { get; set; }
	[JsonProperty("SecurityType")]
	public int SecurityType { get; set; } = 1;
	[JsonProperty("Side")]
	public string Side { get; set; }
	[JsonProperty("CashMargin")]
	public int CashMargin { get; set; }
	[JsonProperty("MarginTradeType", NullValueHandling = NullValueHandling.Ignore)]
	public int? MarginTradeType { get; set; }
	[JsonProperty("DelivType")]
	public int DeliveryType { get; set; }
	[JsonProperty("FundType", NullValueHandling = NullValueHandling.Ignore)]
	public string FundType { get; set; }
	[JsonProperty("AccountType")]
	public int AccountType { get; set; }
	[JsonProperty("Qty")]
	public long Quantity { get; set; }
	[JsonProperty("ClosePositionOrder", NullValueHandling = NullValueHandling.Ignore)]
	public int? ClosePositionOrder { get; set; }
	[JsonProperty("ClosePositions", NullValueHandling = NullValueHandling.Ignore)]
	public KabuStationClosePosition[] ClosePositions { get; set; }
	[JsonProperty("FrontOrderType")]
	public int FrontOrderType { get; set; }
	[JsonProperty("Price")]
	public decimal Price { get; set; }
	[JsonProperty("ExpireDay")]
	public int ExpireDay { get; set; }
	[JsonProperty("ReverseLimitOrder", NullValueHandling = NullValueHandling.Ignore)]
	public KabuStationStockStopOrder StopOrder { get; set; }
}

internal sealed class KabuStationDerivativeOrderRequest
{
	[JsonProperty("Symbol")]
	public string Symbol { get; set; }
	[JsonProperty("Exchange")]
	public int Exchange { get; set; }
	[JsonProperty("TradeType")]
	public int TradeType { get; set; }
	[JsonProperty("TimeInForce")]
	public int TimeInForce { get; set; }
	[JsonProperty("Side")]
	public string Side { get; set; }
	[JsonProperty("Qty")]
	public long Quantity { get; set; }
	[JsonProperty("ClosePositionOrder", NullValueHandling = NullValueHandling.Ignore)]
	public int? ClosePositionOrder { get; set; }
	[JsonProperty("ClosePositions", NullValueHandling = NullValueHandling.Ignore)]
	public KabuStationClosePosition[] ClosePositions { get; set; }
	[JsonProperty("FrontOrderType")]
	public int FrontOrderType { get; set; }
	[JsonProperty("Price")]
	public decimal Price { get; set; }
	[JsonProperty("ExpireDay")]
	public int ExpireDay { get; set; }
	[JsonProperty("ReverseLimitOrder", NullValueHandling = NullValueHandling.Ignore)]
	public KabuStationDerivativeStopOrder StopOrder { get; set; }
}

internal sealed class KabuStationCancelOrderRequest
{
	[JsonProperty("OrderId")]
	public string OrderId { get; set; }
}

internal sealed class KabuStationOrderDetail
{
	[JsonProperty("SeqNum")]
	public int SequenceNumber { get; set; }
	[JsonProperty("ID")]
	public string Id { get; set; }
	[JsonProperty("RecType")]
	public int RecordType { get; set; }
	[JsonProperty("ExchangeID")]
	public string ExchangeId { get; set; }
	[JsonProperty("State")]
	public int State { get; set; }
	[JsonProperty("TransactTime")]
	public string TransactionTime { get; set; }
	[JsonProperty("OrdType")]
	public int? OrderType { get; set; }
	[JsonProperty("Price")]
	public decimal? Price { get; set; }
	[JsonProperty("Qty")]
	public decimal? Quantity { get; set; }
	[JsonProperty("ExecutionID")]
	public string ExecutionId { get; set; }
	[JsonProperty("ExecutionDay")]
	public int? ExecutionDay { get; set; }
	[JsonProperty("DelivDay")]
	public int? DeliveryDay { get; set; }
	[JsonProperty("Commission")]
	public decimal? Commission { get; set; }
	[JsonProperty("CommissionTax")]
	public decimal? CommissionTax { get; set; }
}

internal sealed class KabuStationOrder
{
	[JsonIgnore]
	public SecurityTypes SecurityType { get; set; }

	[JsonProperty("ID")]
	public string Id { get; set; }
	[JsonProperty("State")]
	public int State { get; set; }
	[JsonProperty("OrderState")]
	public int OrderState { get; set; }
	[JsonProperty("OrdType")]
	public int? NativeOrderType { get; set; }
	[JsonProperty("RecvTime")]
	public string ReceivedTime { get; set; }
	[JsonProperty("Symbol")]
	public string Symbol { get; set; }
	[JsonProperty("SymbolName")]
	public string SymbolName { get; set; }
	[JsonProperty("Exchange")]
	public int Exchange { get; set; }
	[JsonProperty("ExchangeName")]
	public string ExchangeName { get; set; }
	[JsonProperty("TimeInForce")]
	public int? TimeInForce { get; set; }
	[JsonProperty("Price")]
	public decimal? Price { get; set; }
	[JsonProperty("OrderQty")]
	public decimal? OrderQuantity { get; set; }
	[JsonProperty("CumQty")]
	public decimal? CumulativeQuantity { get; set; }
	[JsonProperty("Side")]
	public string Side { get; set; }
	[JsonProperty("CashMargin")]
	public int? CashMargin { get; set; }
	[JsonProperty("AccountType")]
	public int? AccountType { get; set; }
	[JsonProperty("DelivType")]
	public int? DeliveryType { get; set; }
	[JsonProperty("ExpireDay")]
	public int? ExpireDay { get; set; }
	[JsonProperty("MarginTradeType")]
	public int? MarginTradeType { get; set; }
	[JsonProperty("MarginPremium")]
	public decimal? MarginPremium { get; set; }
	[JsonProperty("Details")]
	public KabuStationOrderDetail[] Details { get; set; }
}

internal sealed class KabuStationPosition
{
	[JsonProperty("ExecutionID")]
	public string ExecutionId { get; set; }
	[JsonProperty("AccountType")]
	public int? AccountType { get; set; }
	[JsonProperty("Symbol")]
	public string Symbol { get; set; }
	[JsonProperty("SymbolName")]
	public string SymbolName { get; set; }
	[JsonProperty("Exchange")]
	public int Exchange { get; set; }
	[JsonProperty("ExchangeName")]
	public string ExchangeName { get; set; }
	[JsonProperty("SecurityType")]
	public int NativeSecurityType { get; set; }
	[JsonProperty("ExecutionDay")]
	public int? ExecutionDay { get; set; }
	[JsonProperty("Price")]
	public decimal? Price { get; set; }
	[JsonProperty("LeavesQty")]
	public decimal? LeavesQuantity { get; set; }
	[JsonProperty("HoldQty")]
	public decimal? HoldQuantity { get; set; }
	[JsonProperty("Side")]
	public string Side { get; set; }
	[JsonProperty("Expenses")]
	public decimal? Expenses { get; set; }
	[JsonProperty("Commission")]
	public decimal? Commission { get; set; }
	[JsonProperty("CommissionTax")]
	public decimal? CommissionTax { get; set; }
	[JsonProperty("ExpireDay")]
	public int? ExpireDay { get; set; }
	[JsonProperty("MarginTradeType")]
	public int? MarginTradeType { get; set; }
	[JsonProperty("CurrentPrice")]
	public decimal? CurrentPrice { get; set; }
	[JsonProperty("Valuation")]
	public decimal? Valuation { get; set; }
	[JsonProperty("ProfitLoss")]
	public decimal? ProfitLoss { get; set; }
	[JsonProperty("ProfitLossRate")]
	public decimal? ProfitLossRate { get; set; }
}

internal sealed class KabuStationCashWallet
{
	[JsonProperty("StockAccountWallet")]
	public decimal? StockAccountWallet { get; set; }
	[JsonProperty("AuKCStockAccountWallet")]
	public decimal? AuMoneyConnectWallet { get; set; }
	[JsonProperty("AuJbnStockAccountWallet")]
	public decimal? AuJibunBankWallet { get; set; }
}

internal sealed class KabuStationMarginWallet
{
	[JsonProperty("MarginAccountWallet")]
	public decimal? MarginAccountWallet { get; set; }
	[JsonProperty("DepositkeepRate")]
	public decimal? DepositKeepRate { get; set; }
	[JsonProperty("ConsignmentDepositRate")]
	public decimal? ConsignmentDepositRate { get; set; }
	[JsonProperty("CashOfConsignmentDepositRate")]
	public decimal? CashConsignmentDepositRate { get; set; }
	[JsonProperty("MaximumSellOpenAmountPerSymbol")]
	public decimal? MaximumSellOpenAmountPerSymbol { get; set; }
	[JsonProperty("MaximumBuyOpenAmountPerSymbol")]
	public decimal? MaximumBuyOpenAmountPerSymbol { get; set; }
}

internal sealed class KabuStationFutureWallet
{
	[JsonProperty("FutureTradeLimit")]
	public decimal? TradeLimit { get; set; }
	[JsonProperty("MarginRequirement")]
	public decimal? MarginRequirement { get; set; }
	[JsonProperty("MarginRequirementSell")]
	public decimal? SellMarginRequirement { get; set; }
}

internal sealed class KabuStationOptionWallet
{
	[JsonProperty("OptionBuyTradeLimit")]
	public decimal? BuyTradeLimit { get; set; }
	[JsonProperty("OptionSellTradeLimit")]
	public decimal? SellTradeLimit { get; set; }
	[JsonProperty("MarginRequirement")]
	public decimal? MarginRequirement { get; set; }
}
