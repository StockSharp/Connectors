namespace StockSharp.Digifinex.Native.Model;

class Trade
{
	[JsonProperty("id")]
	public long? Id { get; set; }

	[JsonProperty("time")]
	[JsonConverter(typeof(JsonDateTimeConverter))]
	public DateTime? Time { get; set; }

	[JsonProperty("date")]
	[JsonConverter(typeof(JsonDateTimeConverter))]
	public DateTime? Date { get; set; }

	[JsonProperty("price")]
	public double Price { get; set; }

	[JsonProperty("amount")]
	public double Amount { get; set; }

	[JsonProperty("type")]
	public string Type { get; set; }
}