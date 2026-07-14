namespace StockSharp.Exmo.Native.Model;

class Ticker
{
	[JsonProperty("buy_price")]
	public double? BuyPrice { get; set; }

	[JsonProperty("sell_price")]
	public double? SellPrice { get; set; }

	[JsonProperty("last_trade")]
	public double? LastTrade { get; set; }

	[JsonProperty("high")]
	public double? High { get; set; }

	[JsonProperty("low")]
	public double? Low { get; set; }

	[JsonProperty("avg")]
	public double? Avg { get; set; }

	[JsonProperty("vol")]
	public double? Vol { get; set; }

	[JsonProperty("vol_curr")]
	public double? VolCurr { get; set; }

	[JsonProperty("updated")]
	[JsonConverter(typeof(JsonDateTimeConverter))]
	public DateTime Updated { get; set; }
}