namespace StockSharp.Mexc.Native.Futures.Model;

class Balance
{
	[JsonProperty("accountAlias")]
	public string AccountAlias { get; set; }

	[JsonProperty("asset")]
	public string Asset { get; set; }

	[JsonProperty("balance")]
	public double? BalanceValue { get; set; }

	[JsonProperty("crossWalletBalance")]
	public double? CrossWalletBalance { get; set; }

	[JsonProperty("crossUnPnl")]
	public double? CrossUnPnl { get; set; }

	[JsonProperty("availableBalance")]
	public double? AvailableBalance { get; set; }

	[JsonProperty("maxWithdrawAmount")]
	public double? MaxWithdrawAmount { get; set; }

	[JsonProperty("marginAvailable")]
	public bool? MarginAvailable { get; set; }

	[JsonProperty("updateTime")]
	[JsonConverter(typeof(JsonDateTimeMlsConverter))]
	public DateTime? UpdateTime { get; set; }
}

class AccountInfo
{
	[JsonProperty("feeTier")]
	public int? FeeTier { get; set; }

	[JsonProperty("canTrade")]
	public bool CanTrade { get; set; }

	[JsonProperty("canDeposit")]
	public bool CanDeposit { get; set; }

	[JsonProperty("canWithdraw")]
	public bool CanWithdraw { get; set; }

	[JsonProperty("updateTime")]
	[JsonConverter(typeof(JsonDateTimeMlsConverter))]
	public DateTime UpdateTime { get; set; }

	[JsonProperty("totalInitialMargin")]
	public double? TotalInitialMargin { get; set; }

	[JsonProperty("totalMaintMargin")]
	public double? TotalMaintMargin { get; set; }

	[JsonProperty("totalWalletBalance")]
	public double? TotalWalletBalance { get; set; }

	[JsonProperty("totalUnrealizedProfit")]
	public double? TotalUnrealizedProfit { get; set; }

	[JsonProperty("totalMarginBalance")]
	public double? TotalMarginBalance { get; set; }

	[JsonProperty("totalPositionInitialMargin")]
	public double? TotalPositionInitialMargin { get; set; }

	[JsonProperty("totalOpenOrderInitialMargin")]
	public double? TotalOpenOrderInitialMargin { get; set; }

	[JsonProperty("totalCrossWalletBalance")]
	public double? TotalCrossWalletBalance { get; set; }

	[JsonProperty("totalCrossUnPnl")]
	public double? TotalCrossUnPnl { get; set; }

	[JsonProperty("availableBalance")]
	public double? AvailableBalance { get; set; }

	[JsonProperty("maxWithdrawAmount")]
	public double? MaxWithdrawAmount { get; set; }

	[JsonProperty("assets")]
	public Balance[] Assets { get; set; }

	[JsonProperty("positions")]
	public Position[] Positions { get; set; }
}