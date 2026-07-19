namespace StockSharp.BitpandaFusion.Native.Model;

sealed class BitpandaFusionPair
{
	[JsonProperty("pair")]
	public string Pair { get; set; }

	[JsonProperty("baseAsset")]
	public string BaseAsset { get; set; }

	[JsonProperty("quoteAsset")]
	public string QuoteAsset { get; set; }

	[JsonProperty("baseAssetType")]
	[JsonConverter(typeof(StringEnumConverter))]
	public BitpandaFusionPairAssetTypes BaseAssetType { get; set; }

	[JsonProperty("quoteAssetType")]
	[JsonConverter(typeof(StringEnumConverter))]
	public BitpandaFusionPairAssetTypes QuoteAssetType { get; set; }

	[JsonProperty("tickSize")]
	public string TickSize { get; set; }

	[JsonProperty("sizeIncrement")]
	public string SizeIncrement { get; set; }

	[JsonProperty("amountIncrement")]
	public string AmountIncrement { get; set; }

	[JsonProperty("maxOrderSize")]
	public string MaxOrderSize { get; set; }

	[JsonProperty("minOrderAmount")]
	public string MinOrderAmount { get; set; }

	[JsonProperty("maxOrderAmount")]
	public string MaxOrderAmount { get; set; }
}

sealed class BitpandaFusionTicker
{
	[JsonProperty("pair")]
	public string Pair { get; set; }

	[JsonProperty("price")]
	public string Price { get; set; }

	[JsonProperty("high")]
	public string High { get; set; }

	[JsonProperty("low")]
	public string Low { get; set; }

	[JsonProperty("volume")]
	public string Volume { get; set; }
}

sealed class BitpandaFusionOrderBookLevel
{
	[JsonProperty("price")]
	public string Price { get; set; }

	[JsonProperty("quantity")]
	public string Quantity { get; set; }

	[JsonProperty("totalQuantity")]
	public string TotalQuantity { get; set; }
}

sealed class BitpandaFusionOrderBook
{
	[JsonProperty("pair")]
	public string Pair { get; set; }

	[JsonProperty("timestamp")]
	public DateTimeOffset Timestamp { get; set; }

	[JsonProperty("bids")]
	public BitpandaFusionOrderBookLevel[] Bids { get; set; }

	[JsonProperty("asks")]
	public BitpandaFusionOrderBookLevel[] Asks { get; set; }
}

sealed class BitpandaFusionCandle
{
	[JsonProperty("timestamp")]
	public long Timestamp { get; set; }

	[JsonProperty("open")]
	public decimal Open { get; set; }

	[JsonProperty("high")]
	public decimal High { get; set; }

	[JsonProperty("low")]
	public decimal Low { get; set; }

	[JsonProperty("close")]
	public decimal Close { get; set; }

	[JsonProperty("volume")]
	public decimal Volume { get; set; }
}

sealed class BitpandaFusionAsset
{
	[JsonProperty("symbol")]
	public string Symbol { get; set; }

	[JsonProperty("name")]
	public string Name { get; set; }

	[JsonProperty("type")]
	[JsonConverter(typeof(StringEnumConverter))]
	public BitpandaFusionAssetTypes Type { get; set; }
}

sealed class BitpandaFusionCandlesFilter
{
	public string Interval { get; init; }
	public DateTime? From { get; init; }
	public DateTime? To { get; init; }
	public int Limit { get; init; }
}
