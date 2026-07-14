namespace StockSharp.Huobi.Native.Spot.Model;

class OwnTrade
{
	[JsonProperty("id")]
	public long Id { get; set; }

	[JsonProperty("order-id")]
	public long OrderId { get; set; }

	[JsonProperty("match-id")]
	public long MatchId { get; set; }

	[JsonProperty("symbol")]
	public string Symbol { get; set; }

	[JsonProperty("type")]
	public string Type { get; set; }

	[JsonProperty("source")]
	public string Source { get; set; }

	[JsonProperty("price")]
	public double Price { get; set; }

	[JsonProperty("filled-amount")]
	public double FilledAmount { get; set; }

	[JsonProperty("filled-fees")]
	public double? FilledFees { get; set; }

	[JsonProperty("created-at")]
	[JsonConverter(typeof(JsonDateTimeMlsConverter))]
	public DateTime CreatedAt { get; set; }
}