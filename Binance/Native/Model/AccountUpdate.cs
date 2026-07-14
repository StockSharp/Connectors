namespace StockSharp.Binance.Native.Model;

class BalanceUpdate
{
	[JsonProperty("a")]
	public string Asset { get; set; }

	[JsonProperty("f")]
	public double Free { get; set; }

	[JsonProperty("l")]
	public double Locked { get; set; }
}

class BalanceFuturesBalance
{
	[JsonProperty("a")]
	public string Asset { get; set; }

	[JsonProperty("wb")]
	public double Balance { get; set; }

	[JsonProperty("cw")]
	public double? CrossBalance { get; set; }
}

class BalanceFuturesPosition
{
	[JsonProperty("s")]
	public string Symbol { get; set; }

	[JsonProperty("pa")]
	public double Amount { get; set; }

	[JsonProperty("ep")]
	public double? EntryPrice { get; set; }

	[JsonProperty("cr")]
	public double? RealizedPnL { get; set; }

	[JsonProperty("up")]
	public double? UnrealizedPnL { get; set; }

	[JsonProperty("mt")]
	public string MarginType { get; set; }

	[JsonProperty("iw")]
	public double? IsolatedWallet { get; set; }
}

class BalanceFuturesData
{
	[JsonProperty("B")]
	public BalanceFuturesBalance[] Balances { get; set; }

	[JsonProperty("P")]
	public BalanceFuturesPosition[] Positions { get; set; }
}

class AccountUpdate : BaseEvent
{
	[JsonProperty("m")]
	public double? MakerCommissionRate { get; set; }

	[JsonProperty("t")]
	public double? TakerCommissionRate { get; set; }

	[JsonProperty("b")]
	public double? BuyerCommissionRate { get; set; }

	[JsonProperty("s")]
	public double? SellerCommissionRate { get; set; }

	[JsonProperty("T")]
	public bool CanTrade { get; set; }

	[JsonProperty("W")]
	public bool CanWithdraw { get; set; }

	[JsonProperty("D")]
	public bool CanDeposit { get; set; }

	[JsonProperty("u")]
	[JsonConverter(typeof(JsonDateTimeMlsConverter))]
	public DateTime? LastAccountUpdate { get; set; }

	[JsonProperty("B")]
	public BalanceUpdate[] Balances { get; set; }

	[JsonProperty("a")]
	public BalanceFuturesData FuturesData { get; set; }
}