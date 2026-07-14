namespace StockSharp.Zaif.Native.Model;

class OwnTrade
{
	[JsonProperty("currency_pair")]
	public string CurrencyPair { get; set; }

	[JsonProperty("action")]
	public string Action { get; set; }

	[JsonProperty("your_action")]
	public string OwnAction { get; set; }

	[JsonProperty("price")]
	public double? Price { get; set; }

	[JsonProperty("amount")]
	public double? Amount { get; set; }

	[JsonProperty("bonus")]
	public double? Bonus { get; set; }

	[JsonProperty("timestamp")]
	[JsonConverter(typeof(JsonDateTimeConverter))]
	public DateTime Timestamp { get; set; }
}