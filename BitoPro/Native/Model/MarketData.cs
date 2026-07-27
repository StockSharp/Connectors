namespace StockSharp.BitoPro.Native.Model;

sealed class BitoProSymbol
{
	[JsonProperty("pair")]
	public string Pair { get; set; }

	[JsonProperty("base")]
	public string Base { get; set; }

	[JsonProperty("quote")]
	public string Quote { get; set; }

	[JsonProperty("basePrecision")]
	public int BasePrecision { get; set; }

	[JsonProperty("quotePrecision")]
	public int QuotePrecision { get; set; }

	[JsonProperty("minLimitBaseAmount")]
	public decimal? MinimumAmount { get; set; }

	[JsonProperty("maxLimitBaseAmount")]
	public decimal? MaximumAmount { get; set; }

	[JsonProperty("minMarketBuyQuoteAmount")]
	public decimal? MinimumMarketBuyQuoteAmount { get; set; }

	[JsonProperty("orderOpenLimit")]
	public int OpenOrderLimit { get; set; }

	[JsonProperty("maintain")]
	public bool IsMaintenance { get; set; }

	[JsonProperty("orderBookQuotePrecision")]
	public int OrderBookQuotePrecision { get; set; }

	[JsonProperty("orderBookQuoteScaleLevel")]
	public int OrderBookQuoteScaleLevel { get; set; }

	[JsonProperty("amountPrecision")]
	public int AmountPrecision { get; set; }

	[JsonIgnore]
	public string SecurityCode
		=> BitoProExtensions.CreateSecurityCode(Base, Quote);
}

sealed class BitoProTicker
{
	[JsonProperty("event")]
	public string Event { get; set; }

	[JsonProperty("eventID")]
	public string EventId { get; set; }

	[JsonProperty("pair")]
	public string Pair { get; set; }

	[JsonProperty("lastPrice")]
	public decimal? LastPrice { get; set; }

	[JsonProperty("isBuyer")]
	public bool? IsBuyer { get; set; }

	[JsonProperty("priceChange24hr")]
	public decimal? PriceChange { get; set; }

	[JsonProperty("volume24hr")]
	public decimal? Volume { get; set; }

	[JsonProperty("high24hr")]
	public decimal? HighPrice { get; set; }

	[JsonProperty("low24hr")]
	public decimal? LowPrice { get; set; }

	[JsonProperty("timestamp")]
	public long Timestamp { get; set; }
}

sealed class BitoProOrderBook
{
	[JsonProperty("event")]
	public string Event { get; set; }

	[JsonProperty("eventID")]
	public string EventId { get; set; }

	[JsonProperty("pair")]
	public string Pair { get; set; }

	[JsonProperty("timestamp")]
	public long Timestamp { get; set; }

	[JsonProperty("limit")]
	public int Limit { get; set; }

	[JsonProperty("scale")]
	public int Scale { get; set; }

	[JsonProperty("bids")]
	public BitoProPriceLevel[] Bids { get; set; }

	[JsonProperty("asks")]
	public BitoProPriceLevel[] Asks { get; set; }
}

sealed class BitoProPriceLevel
{
	[JsonProperty("price")]
	public decimal Price { get; set; }

	[JsonProperty("amount")]
	public decimal Amount { get; set; }

	[JsonProperty("count")]
	public int Count { get; set; }

	[JsonProperty("total")]
	public decimal Total { get; set; }
}

sealed class BitoProTrade
{
	[JsonProperty("timestamp")]
	public long Timestamp { get; set; }

	[JsonProperty("price")]
	public decimal Price { get; set; }

	[JsonProperty("amount")]
	public decimal Amount { get; set; }

	[JsonProperty("isBuyer")]
	public bool IsBuyer { get; set; }
}

sealed class BitoProTradePush
{
	[JsonProperty("event")]
	public string Event { get; set; }

	[JsonProperty("eventID")]
	public string EventId { get; set; }

	[JsonProperty("pair")]
	public string Pair { get; set; }

	[JsonProperty("timestamp")]
	public long Timestamp { get; set; }

	[JsonProperty("data")]
	public BitoProTrade[] Data { get; set; }
}

sealed class BitoProCandle
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
