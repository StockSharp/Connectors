namespace StockSharp.Zaif.Native.Model;

class Order
{
	[JsonProperty("timestamp")]
	[JsonConverter(typeof(JsonDateTimeConverter))]
	public DateTime Time { get; set; }

	[JsonProperty("action")]
	public string Type { get; set; }

	[JsonProperty("price")]
	public decimal Price { get; set; }

	[JsonProperty("amount")]
	public decimal Amount { get; set; }

	[JsonProperty("currency_pair")]
	public string CurrencyPair { get; set; }
}