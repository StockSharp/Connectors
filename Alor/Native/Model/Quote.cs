namespace StockSharp.Alor.Native.Model;

class Quote
{
	[JsonProperty("symbol")]
	public string Symbol { get; set; }

	[JsonProperty("exchange")]
	public string Exchange { get; set; }

	[JsonProperty("description")]
	public string Description { get; set; }

	[JsonProperty("ask")]
	public double? Ask { get; set; }

	[JsonProperty("bid")]
	public double? Bid { get; set; }

	[JsonProperty("prev_close_price")]
	public double? PrevClosePrice { get; set; }

	[JsonProperty("last_price")]
	public double? LastPrice { get; set; }

	[JsonProperty("last_price_timestamp")]
	[JsonConverter(typeof(JsonDateTimeMlsConverter))]
	public DateTime? LastPriceTimestamp { get; set; }

	[JsonProperty("change")]
	public double? Change { get; set; }

	[JsonProperty("change_percent")]
	public double? ChangePercent { get; set; }

	[JsonProperty("high_price")]
	public double? HighPrice { get; set; }

	[JsonProperty("low_price")]
	public double? LowPrice { get; set; }

	[JsonProperty("accruedInt")]
	public double? AccruedInt { get; set; }

	[JsonProperty("accrued_interest")]
	public double? AccruedInterest { get; set; }

	[JsonProperty("volume")]
	public double? Volume { get; set; }

	[JsonProperty("open_price")]
	public double? OpenPrice { get; set; }

	[JsonProperty("yield")]
	public double? Yield { get; set; }

	[JsonProperty("lotsize")]
	public double? LotSize { get; set; }

	[JsonProperty("lotvalue")]
	public double? LotValue { get; set; }

	[JsonProperty("facevalue")]
	public double? Facevalue { get; set; }

	[JsonProperty("type")]
	public string Type { get; set; }

	[JsonProperty("total_bid_vol")]
	public double? TotalBidVol { get; set; }

	[JsonProperty("total_ask_vol")]
	public double? TotalAskVol { get; set; }
}