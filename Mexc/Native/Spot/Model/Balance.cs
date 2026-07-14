namespace StockSharp.Mexc.Native.Spot.Model;

class Balance
{
	[JsonProperty("asset")]
	public string Asset { get; set; }

	[JsonProperty("free")]
	public double? Free { get; set; }

	[JsonProperty("locked")]
	public double? Locked { get; set; }
}

class AccountInfo
{
	[JsonProperty("makerCommission")]
	public double? MakerCommission { get; set; }

	[JsonProperty("takerCommission")]
	public double? TakerCommission { get; set; }

	[JsonProperty("buyerCommission")]
	public double? BuyerCommission { get; set; }

	[JsonProperty("sellerCommission")]
	public double? SellerCommission { get; set; }

	[JsonProperty("canTrade")]
	public bool CanTrade { get; set; }

	[JsonProperty("canWithdraw")]
	public bool CanWithdraw { get; set; }

	[JsonProperty("canDeposit")]
	public bool CanDeposit { get; set; }

	[JsonProperty("updateTime")]
	[JsonConverter(typeof(JsonDateTimeMlsConverter))]
	public DateTime UpdateTime { get; set; }

	[JsonProperty("accountType")]
	public string AccountType { get; set; }

	[JsonProperty("balances")]
	public Balance[] Balances { get; set; }

	[JsonProperty("permissions")]
	public string[] Permissions { get; set; }
}