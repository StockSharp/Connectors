namespace StockSharp.Zaif.Native.Model;

class Trade
{
	[JsonProperty("currency_pair")]
	public string CurrencyPair { get; set; }

	[JsonProperty("tid")]
	public long Id { get; set; }

	[JsonProperty("price")]
	public decimal Price { get; set; }

	[JsonProperty("amount")]
	public decimal Amount { get; set; }

	[JsonProperty("date")]
	[JsonConverter(typeof(JsonDateTimeConverter))]
	public DateTime Time { get; set; }

	[JsonProperty("trade_type")]
	public string Type { get; set; }
}