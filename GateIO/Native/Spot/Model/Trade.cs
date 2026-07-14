namespace StockSharp.GateIO.Native.Spot.Model;

class Trade
{
	[JsonProperty("id")]
	public long Id { get; set; }

	[JsonProperty("create_time_ms")]
	[JsonConverter(typeof(JsonDateTimeMlsConverter))]
	public DateTime CreateTime { get; set; }

	[JsonProperty("side")]
	public string Side { get; set; }

	[JsonProperty("currency_pair")]
	public string CurrencyPair { get; set; }

	[JsonProperty("amount")]
	public double? Amount { get; set; }

	[JsonProperty("price")]
	public double? Price { get; set; }

	[JsonProperty("range")]
	public string Range { get; set; }
}