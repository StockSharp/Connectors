namespace StockSharp.TradeOgre.Native.Model;

class Trade
{
	[JsonProperty("date")]
	[JsonConverter(typeof(JsonDateTimeConverter))]
	public DateTime Time { get; set; }

	[JsonProperty("type")]
	public string Type { get; set; }

	[JsonProperty("price")]
	public double Price { get; set; }

	[JsonProperty("quantity")]
	public double Quantity { get; set; }
}