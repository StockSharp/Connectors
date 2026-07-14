namespace StockSharp.CoinEx.Native.Spot.Model;

class Tick
{
	[JsonProperty("deal_id")]
	public long Id { get; set; }

	[JsonProperty("price")]
	public decimal Price { get; set; }

	[JsonProperty("amount")]
	public decimal Amount { get; set; }

	[JsonProperty("created_at")]
	[JsonConverter(typeof(JsonDateTimeMlsConverter))]
	public DateTime Time { get; set; }

	[JsonProperty("side")]
	public string Side { get; set; }
}