namespace StockSharp.IIFL.Native;

sealed class IIFLInstrument
{
	[JsonProperty("formattedInstrumentName")]
	public string Name { get; set; }

	[JsonProperty("instrumentType")]
	public string InstrumentType { get; set; }

	[JsonProperty("underlyingInstrumentName")]
	public string UnderlyingName { get; set; }

	[JsonProperty("underlyingInstrumentSymbol")]
	public string UnderlyingSymbol { get; set; }

	[JsonProperty("lotSize")]
	public string LotSize { get; set; }

	[JsonProperty("instrumentId")]
	public string InstrumentId { get; set; }

	[JsonProperty("tickSize")]
	public string TickSize { get; set; }

	[JsonProperty("optionType")]
	public string OptionType { get; set; }

	[JsonProperty("series")]
	public string Series { get; set; }

	[JsonProperty("exchange")]
	public string Exchange { get; set; }

	[JsonProperty("expiry")]
	public string Expiry { get; set; }

	[JsonProperty("tradingSymbol")]
	public string TradingSymbol { get; set; }

	[JsonProperty("strikePrice")]
	public string StrikePrice { get; set; }

	public decimal Lot => LotSize.ToIIFLDecimal() ?? 1;

	public decimal Tick => TickSize.ToIIFLDecimal() ?? 0;

	public decimal? Strike => StrikePrice.ToIIFLDecimal();
}

readonly record struct IIFLInstrumentRef(
	string Exchange,
	string InstrumentId,
	string Symbol,
	string BoardCode,
	decimal LotSize)
{
	public string Topic => $"{Exchange.ToLowerInvariant()}/{InstrumentId}";
}

readonly record struct IIFLDepthLevel(
	decimal Price,
	decimal Volume,
	int Orders);

sealed class IIFLMarketFeed
{
	public decimal LastPrice { get; init; }
	public decimal LastVolume { get; init; }
	public decimal Volume { get; init; }
	public decimal High { get; init; }
	public decimal Low { get; init; }
	public decimal Open { get; init; }
	public decimal Close { get; init; }
	public decimal AveragePrice { get; init; }
	public decimal BestBidPrice { get; init; }
	public decimal BestBidVolume { get; init; }
	public decimal BestAskPrice { get; init; }
	public decimal BestAskVolume { get; init; }
	public decimal TotalBidVolume { get; init; }
	public decimal TotalAskVolume { get; init; }
	public DateTimeOffset Time { get; init; }
	public IIFLDepthLevel[] Bids { get; init; }
	public IIFLDepthLevel[] Asks { get; init; }
}

readonly record struct IIFLOpenInterest(
	decimal Current,
	decimal High,
	decimal Low,
	decimal Previous);

sealed class IIFLOrder
{
	public string OrderId { get; init; }
	public string ExchangeOrderId { get; init; }
	public string InstrumentId { get; init; }
	public string Symbol { get; init; }
	public string Exchange { get; init; }
	public Sides Side { get; init; }
	public string Product { get; init; }
	public string Complexity { get; init; }
	public string Type { get; init; }
	public decimal Price { get; init; }
	public decimal AveragePrice { get; init; }
	public decimal TriggerPrice { get; init; }
	public decimal Volume { get; init; }
	public decimal FilledVolume { get; init; }
	public decimal Balance { get; init; }
	public string Status { get; init; }
	public DateTimeOffset Time { get; init; }
	public string Error { get; init; }
	public string Tag { get; init; }
}

sealed class IIFLTrade
{
	public string Id { get; init; }
	public string OrderId { get; init; }
	public string InstrumentId { get; init; }
	public string Symbol { get; init; }
	public string Exchange { get; init; }
	public Sides Side { get; init; }
	public decimal Price { get; init; }
	public decimal Volume { get; init; }
	public DateTimeOffset Time { get; init; }
}
