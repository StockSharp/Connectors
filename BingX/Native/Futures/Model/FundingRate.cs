namespace StockSharp.BingX.Native.Futures.Model;

class FundingRate
{
	[JsonProperty("symbol")]
	public string Symbol { get; set; }

	[JsonProperty("fundingRate")]
	public double? FundingRateValue { get; set; }

	[JsonProperty("fundingTime")]
	[JsonConverter(typeof(JsonDateTimeConverter))]
	public DateTime FundingTime { get; set; }
}
