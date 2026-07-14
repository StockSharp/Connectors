namespace StockSharp.BingX.Native.Futures.Model;

class Balance
{
	[JsonProperty("asset")]
	public string Asset { get; set; }

	[JsonProperty("balance")]
	public double? BalanceAmount { get; set; }

	[JsonProperty("crossWalletBalance")]
	public double? CrossWalletBalance { get; set; }

	[JsonProperty("crossUnPnl")]
	public double? CrossUnrealizedPnl { get; set; }

	[JsonProperty("availableBalance")]
	public double? AvailableBalance { get; set; }

	[JsonProperty("maxWithdrawAmount")]
	public double? MaxWithdrawAmount { get; set; }

	[JsonProperty("marginAvailable")]
	public bool? MarginAvailable { get; set; }

	[JsonProperty("updateTime")]
	[JsonConverter(typeof(JsonDateTimeConverter))]
	public DateTime? UpdateTime { get; set; }
}
