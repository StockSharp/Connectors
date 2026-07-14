namespace StockSharp.BingX.Native.Futures.Model;

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
	[JsonConverter(typeof(JsonDateTimeConverter))]
	public DateTime? UpdateTime { get; set; }

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
	public double? TotalCrossUnrealizedPnl { get; set; }

	[JsonProperty("availableBalance")]
	public double? AvailableBalance { get; set; }

	[JsonProperty("maxWithdrawAmount")]
	public double? MaxWithdrawAmount { get; set; }

	[JsonProperty("assets")]
	public Balance[] Assets { get; set; }

	[JsonProperty("positions")]
	public Position[] Positions { get; set; }
}