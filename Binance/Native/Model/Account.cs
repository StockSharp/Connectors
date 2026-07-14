namespace StockSharp.Binance.Native.Model;

class Account : BaseEvent
{
	[JsonProperty("makerCommission")]
	public double? MakerCommissionRate { get; set; }

	[JsonProperty("takerCommission")]
	public double? TakerCommissionRate { get; set; }

	[JsonProperty("buyerCommission")]
	public double? BuyerCommissionRate { get; set; }

	[JsonProperty("sellerCommission")]
	public double? SellerCommissionRate { get; set; }

	[JsonProperty("marginLevel")]
	public double? MarginLevel { get; set; }

	[JsonProperty("canTrade")]
	public bool CanTrade { get; set; }

	[JsonProperty("canWithdraw")]
	public bool CanWithdraw { get; set; }

	[JsonProperty("canDeposit")]
	public bool CanDeposit { get; set; }

	[JsonProperty("updateTime")]
	[JsonConverter(typeof(JsonDateTimeMlsConverter))]
	public DateTime UpdateTime { get; set; }

	[JsonProperty("balances")]
	public Balance[] Balances { get; set; }

	[JsonProperty("userAssets")]
	public Balance[] UserAssets { get; set; }

	[JsonProperty("assets")]
	public BalanceFuturePosition[] Assets { get; set; }

	[JsonProperty("positions")]
	public BalanceFuturePosition[] Positions { get; set; }
}

class Balance
{
	[JsonProperty("asset")]
	public string Asset { get; set; }

	[JsonProperty("free")]
	public double? Free { get; set; }

	[JsonProperty("locked")]
	public double? Locked { get; set; }

	[JsonProperty("borrowed")]
	public double? Borrowed { get; set; }

	[JsonProperty("interest")]
	public double? Interest { get; set; }

	[JsonProperty("netAsset")]
	public double? NetAsset { get; set; }
}

class BalanceFuturePosition
{
	[JsonProperty("isolated")]
	public bool Isolated { get; set; }

	[JsonProperty("leverage")]
	public double? Leverage { get; set; }

	[JsonProperty("initialMargin")]
	public double? InitialMargin { get; set; }

	[JsonProperty("marginBalance")]
	public double? MarginBalance { get; set; }

	[JsonProperty("maintMargin")]
	public double? MaintMargin { get; set; }

	[JsonProperty("maxWithdrawAmount")]
	public double? MaxWithdrawAmount { get; set; }

	[JsonProperty("openOrderInitialMargin")]
	public double? OpenOrderInitialMargin { get; set; }

	[JsonProperty("positionInitialMargin")]
	public double? PositionInitialMargin { get; set; }

	[JsonProperty("asset")]
	public string Asset { get; set; }

	[JsonProperty("symbol")]
	public string Symbol { get; set; }

	[JsonProperty("unrealizedProfit")]
	public double? UnrealizedProfit { get; set; }

	[JsonProperty("walletBalance")]
	public double? WalletBalance { get; set; }

	[JsonProperty("positionAmt")]
	public double? Amount { get; set; }

	[JsonProperty("entryPrice")]
	public double? EntryPrice { get; set; }

	[JsonProperty("markPrice")]
	public double? MarkPrice { get; set; }

	[JsonProperty("positionSide")]
	public string Side { get; set; }

	[JsonProperty("marginType")]
	public string MarginType { get; set; }

	[JsonProperty("liquidationPrice")]
	public double? LiquidationPrice { get; set; }
}

class IsolatedAccount
{
	[JsonProperty("baseAsset")]
	public Balance BaseAsset { get; set; }

	[JsonProperty("quoteAsset")]
	public Balance QuoteAsset { get; set; }

	[JsonProperty("symbol")]
	public string Symbol { get; set; }

	[JsonProperty("isolatedCreated")]
	public bool? IsolatedCreated { get; set; }

	[JsonProperty("enabled")]
	public bool? Enabled { get; set; }

	[JsonProperty("tradeEnabled")]
	public bool? TradeEnabled { get; set; }

	[JsonProperty("marginLevel")]
	public double? MarginLevel { get; set; }

	[JsonProperty("marginLevelStatus")]
	public string MarginLevelStatus { get; set; }
}

class IsolatedAccounts
{
	[JsonProperty("assets")]
	public IsolatedAccount[] Assets { get; set; }

	[JsonProperty("totalAssetOfBtc")]
	public double? TotalAssetOfBtc { get; set; }

	[JsonProperty("totalLiabilityOfBtc")]
	public double? TotalLiabilityOfBtc { get; set; }

	[JsonProperty("totalNetAssetOfBtc")]
	public double? TotalNetAssetOfBtc { get; set; }
}