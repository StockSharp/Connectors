namespace StockSharp.Saxo.Native.Model;

sealed class SaxoQuote
{
	[JsonProperty("Ask")]
	public decimal? Ask { get; set; }

	[JsonProperty("AskSize")]
	public decimal? AskSize { get; set; }

	[JsonProperty("Bid")]
	public decimal? Bid { get; set; }

	[JsonProperty("BidSize")]
	public decimal? BidSize { get; set; }

	[JsonProperty("Mid")]
	public decimal? Mid { get; set; }

	[JsonProperty("DelayedByMinutes")]
	public int? DelayedByMinutes { get; set; }
}

sealed class SaxoPriceInfo
{
	[JsonProperty("High")]
	public decimal? High { get; set; }

	[JsonProperty("Low")]
	public decimal? Low { get; set; }

	[JsonProperty("NetChange")]
	public decimal? NetChange { get; set; }

	[JsonProperty("PercentChange")]
	public decimal? PercentChange { get; set; }
}

sealed class SaxoPriceInfoDetails
{
	[JsonProperty("Open")]
	public decimal? Open { get; set; }

	[JsonProperty("LastClose")]
	public decimal? LastClose { get; set; }

	[JsonProperty("LastTraded")]
	public decimal? LastTraded { get; set; }

	[JsonProperty("LastTradedSize")]
	public decimal? LastTradedSize { get; set; }

	[JsonProperty("Volume")]
	public decimal? Volume { get; set; }

	[JsonProperty("OpenInterest")]
	public decimal? OpenInterest { get; set; }
}

sealed class SaxoMarketDepth
{
	[JsonProperty("Ask")]
	public decimal[] Ask { get; set; }

	[JsonProperty("AskSize")]
	public decimal[] AskSize { get; set; }

	[JsonProperty("AskOrders")]
	public int[] AskOrders { get; set; }

	[JsonProperty("Bid")]
	public decimal[] Bid { get; set; }

	[JsonProperty("BidSize")]
	public decimal[] BidSize { get; set; }

	[JsonProperty("BidOrders")]
	public int[] BidOrders { get; set; }
}

sealed class SaxoInfoPrice
{
	[JsonProperty("Uic")]
	public long Uic { get; set; }

	[JsonProperty("AssetType")]
	public string AssetType { get; set; }

	[JsonProperty("LastUpdated")]
	public DateTime? LastUpdated { get; set; }

	[JsonProperty("Quote")]
	public SaxoQuote Quote { get; set; }

	[JsonProperty("PriceInfo")]
	public SaxoPriceInfo PriceInfo { get; set; }

	[JsonProperty("PriceInfoDetails")]
	public SaxoPriceInfoDetails PriceInfoDetails { get; set; }

	[JsonProperty("MarketDepth")]
	public SaxoMarketDepth MarketDepth { get; set; }
}

sealed class SaxoInfoPriceUpdate : SaxoFeed<SaxoInfoPrice>
{
	[JsonProperty("ReferenceId")]
	public string ReferenceId { get; set; }

	[JsonProperty("Timestamp")]
	public DateTime? Timestamp { get; set; }
}

sealed class SaxoInfoPriceArguments
{
	[JsonProperty("AccountKey")]
	public string AccountKey { get; set; }

	[JsonProperty("Uics")]
	public string Uics { get; set; }

	[JsonProperty("AssetType")]
	public string AssetType { get; set; }

	[JsonProperty("FieldGroups")]
	public string[] FieldGroups { get; set; }
}

sealed class SaxoInfoPriceSubscriptionRequest : SaxoSubscriptionRequest
{
	[JsonProperty("Arguments")]
	public SaxoInfoPriceArguments Arguments { get; set; }
}

sealed class SaxoChartSample
{
	[JsonProperty("Time")]
	public DateTime Time { get; set; }

	[JsonProperty("Open")]
	public decimal? Open { get; set; }

	[JsonProperty("High")]
	public decimal? High { get; set; }

	[JsonProperty("Low")]
	public decimal? Low { get; set; }

	[JsonProperty("Close")]
	public decimal? Close { get; set; }

	[JsonProperty("OpenAsk")]
	public decimal? OpenAsk { get; set; }

	[JsonProperty("HighAsk")]
	public decimal? HighAsk { get; set; }

	[JsonProperty("LowAsk")]
	public decimal? LowAsk { get; set; }

	[JsonProperty("CloseAsk")]
	public decimal? CloseAsk { get; set; }

	[JsonProperty("OpenBid")]
	public decimal? OpenBid { get; set; }

	[JsonProperty("HighBid")]
	public decimal? HighBid { get; set; }

	[JsonProperty("LowBid")]
	public decimal? LowBid { get; set; }

	[JsonProperty("CloseBid")]
	public decimal? CloseBid { get; set; }

	[JsonProperty("Volume")]
	public decimal? Volume { get; set; }

	[JsonProperty("Interest")]
	public decimal? Interest { get; set; }
}

sealed class SaxoChartResponse : SaxoFeed<SaxoChartSample>
{
	[JsonProperty("DataVersion")]
	public long? DataVersion { get; set; }
}

sealed class SaxoChartArguments
{
	[JsonProperty("Uic")]
	public long Uic { get; set; }

	[JsonProperty("AssetType")]
	public string AssetType { get; set; }

	[JsonProperty("Horizon")]
	public int Horizon { get; set; }

	[JsonProperty("Count")]
	public int Count { get; set; }

	[JsonProperty("FieldGroups")]
	public string[] FieldGroups { get; set; }
}

sealed class SaxoChartSubscriptionRequest : SaxoSubscriptionRequest
{
	[JsonProperty("Arguments")]
	public SaxoChartArguments Arguments { get; set; }
}
