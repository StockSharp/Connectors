namespace StockSharp.BingX.Native.Futures.Model;

class MarkPrice
{
	[JsonProperty("symbol")]
	public string Symbol { get; set; }

	[JsonProperty("markPrice")]
	public double? Value { get; set; }

	[JsonProperty("indexPrice")]
	public double? IndexPrice { get; set; }

	[JsonProperty("estimatedSettlePrice")]
	public double? EstimatedSettlePrice { get; set; }

	[JsonProperty("lastFundingRate")]
	public double? LastFundingRate { get; set; }

	[JsonProperty("interestRate")]
	public double? InterestRate { get; set; }

	[JsonProperty("nextFundingTime")]
	[JsonConverter(typeof(JsonDateTimeConverter))]
	public DateTime? NextFundingTime { get; set; }

	[JsonProperty("time")]
	[JsonConverter(typeof(JsonDateTimeConverter))]
	public DateTime Time { get; set; }
}
