namespace StockSharp.Bitget.Native.Futures.Model;

class Ticker
{
	[JsonProperty("instId")]
	public string Symbol { get; set; }

	[JsonProperty("lastPr")]
	public double? Last { get; set; }

	[JsonProperty("bestAsk")]
	public double? BestAsk { get; set; }

	[JsonProperty("bestBid")]
	public double? BestBid { get; set; }

	[JsonProperty("bidPr")]
	public double? BidPr { get; set; }

	[JsonProperty("askPr")]
	public double? AskPr { get; set; }

	[JsonProperty("bidSz")]
	public double? BidSz { get; set; }

	[JsonProperty("askSz")]
	public double? AskSz { get; set; }

	[JsonProperty("high24h")]
	public double? High24h { get; set; }

	[JsonProperty("low24h")]
	public double? Low24h { get; set; }

	[JsonProperty("ts")]
	[JsonConverter(typeof(JsonDateTimeMlsConverter))]
	public DateTime Timestamp { get; set; }

	[JsonProperty("change")]
	public double? Change { get; set; }

	[JsonProperty("change24h")]
	public double? Change24h { get; set; }

	[JsonProperty("baseVolume")]
	public double? BaseVolume { get; set; }

	[JsonProperty("quoteVolume")]
	public double? QuoteVolume { get; set; }

	[JsonProperty("usdtVolume")]
	public double? UsdtVolume { get; set; }

	[JsonProperty("openUtc")]
	public double? OpenUtc { get; set; }

	[JsonProperty("chgUtc")]
	public double? ChgUtc { get; set; }

	[JsonProperty("indexPrice")]
	public double? IndexPrice { get; set; }

	[JsonProperty("fundingRate")]
	public double? FundingRate { get; set; }

	[JsonProperty("holdingAmount")]
	public double? HoldingAmount { get; set; }

	[JsonProperty("openInterest")]
	public double? OpenInterest { get; set; }
}
