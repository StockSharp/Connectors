namespace StockSharp.Bithumb.Native.Model;

sealed class Balance
{
	[JsonProperty("currency")]
	public string Currency { get; set; }

	[JsonProperty("balance")]
	public string Value { get; set; }

	[JsonProperty("locked")]
	public string Locked { get; set; }

	[JsonProperty("avg_buy_price")]
	public string AveragePrice { get; set; }

	[JsonProperty("unit_currency")]
	public string UnitCurrency { get; set; }
}
