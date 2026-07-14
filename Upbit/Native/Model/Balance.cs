namespace StockSharp.Upbit.Native.Model;

class Balance
{
	[JsonProperty("currency")]
	public string Currency { get; set; }

	[JsonProperty("balance")]
	public double Value { get; set; }

	[JsonProperty("locked")]
	public double? Locked { get; set; }

	[JsonProperty("avg_buy_price")]
	public double? AvgBuyPrice { get; set; }

	[JsonProperty("avg_buy_price_modified")]
	public bool AvgBuyPriceModified { get; set; }

	[JsonProperty("unit_currency")]
	public string UnitCurrency { get; set; }
}