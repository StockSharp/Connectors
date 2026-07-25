namespace StockSharp.Coincheck.Native.Model;

[JsonConverter(typeof(JArrayToObjectConverter))]
class Trade
{
	// the stream sends [timestamp, id, pair, rate, amount, order_type, taker_id, maker_id, ...]
	// and JArrayToObjectConverter maps the elements by declaration order, so the leading
	// unix time must be declared even though it is only converted later
	[JsonProperty("timestamp")]
	public long Time { get; set; }

	[JsonProperty("id")]
	public long Id { get; set; }

	[JsonProperty("pair")]
	public string Currency { get; set; }

	[JsonProperty("rate")]
	public decimal Price { get; set; }

	[JsonProperty("amount")]
	public decimal Amount { get; set; }

	[JsonProperty("order_type")]
	public string Type { get; set; }
}

class HttpTrade
{
	[JsonProperty("id")]
	public long Id { get; set; }

	[JsonProperty("pair")]
	public string Currency { get; set; }

	[JsonProperty("rate")]
	public decimal Price { get; set; }

	[JsonProperty("amount")]
	public decimal Amount { get; set; }

	[JsonProperty("order_type")]
	public string Type { get; set; }

	[JsonProperty("created_at")]
	public string CreatedAt { get; set; }
}