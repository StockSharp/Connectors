namespace StockSharp.Bitbank.Native.Model;

class Trade
{
	[JsonProperty("transaction_id")]
	public long Id { get; set; }

	[JsonProperty("side")]
	public string Side { get; set; }

	[JsonProperty("price")]
	public double Price { get; set; }

	[JsonProperty("amount")]
	public double Amount { get; set; }

	[JsonProperty("executed_at")]
	[JsonConverter(typeof(JsonDateTimeMlsConverter))]
	public DateTime ExecutedAt { get; set; }
}