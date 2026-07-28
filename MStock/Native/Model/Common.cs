namespace StockSharp.MStock.Native;

sealed class MStockInstrument
{
	[JsonProperty("token")]
	public string Token { get; set; }

	[JsonProperty("symbol")]
	public string Symbol { get; set; }

	[JsonProperty("name")]
	public string TradingSymbol { get; set; }

	[JsonProperty("expiry")]
	public string Expiry { get; set; }

	[JsonProperty("strike")]
	public string Strike { get; set; }

	[JsonProperty("lotsize")]
	public string LotSize { get; set; }

	[JsonProperty("instrumenttype")]
	public string InstrumentType { get; set; }

	[JsonProperty("exch_seg")]
	public string Exchange { get; set; }

	[JsonProperty("tick_size")]
	public string TickSize { get; set; }

	public decimal Lot => LotSize.ToMStockDecimal() ?? 1;

	public decimal Tick => TickSize.ToMStockDecimal() ?? 0;

	public decimal? StrikePrice => Strike.ToMStockDecimal();
}

readonly record struct MStockInstrumentRef(
	string Exchange,
	string Token,
	string TradingSymbol,
	string Symbol,
	decimal LotSize)
{
	public string Key => $"{Exchange}:{Token}";
}

readonly record struct MStockDepthLevel(
	decimal Price,
	decimal Volume,
	int Orders);

sealed class MStockFeed
{
	public int Mode { get; init; }
	public string Exchange { get; init; }
	public string Token { get; init; }
	public long Sequence { get; init; }
	public DateTimeOffset Time { get; init; }
	public DateTimeOffset LastTradeTime { get; init; }
	public decimal LastPrice { get; init; }
	public decimal LastVolume { get; init; }
	public decimal AveragePrice { get; init; }
	public decimal Volume { get; init; }
	public decimal TotalBidVolume { get; init; }
	public decimal TotalAskVolume { get; init; }
	public decimal Open { get; init; }
	public decimal High { get; init; }
	public decimal Low { get; init; }
	public decimal Close { get; init; }
	public decimal OpenInterest { get; init; }
	public decimal OpenInterestChange { get; init; }
	public decimal UpperLimit { get; init; }
	public decimal LowerLimit { get; init; }
	public decimal YearHigh { get; init; }
	public decimal YearLow { get; init; }
	public MStockDepthLevel[] Bids { get; init; }
	public MStockDepthLevel[] Asks { get; init; }
}

sealed class MStockCandle
{
	public DateTimeOffset Time { get; init; }
	public decimal Open { get; init; }
	public decimal High { get; init; }
	public decimal Low { get; init; }
	public decimal Close { get; init; }
	public decimal Volume { get; init; }
}

sealed class MStockOrder
{
	public string OrderId { get; init; }
	public string ExchangeOrderId { get; init; }
	public string Exchange { get; init; }
	public string Token { get; init; }
	public string Symbol { get; init; }
	public Sides Side { get; init; }
	public string OrderType { get; init; }
	public string Product { get; init; }
	public string Variety { get; init; }
	public string Duration { get; init; }
	public decimal Price { get; init; }
	public decimal TriggerPrice { get; init; }
	public decimal Volume { get; init; }
	public decimal FilledVolume { get; init; }
	public decimal Balance { get; init; }
	public decimal AveragePrice { get; init; }
	public string Status { get; init; }
	public string Text { get; init; }
	public string Tag { get; init; }
	public DateTimeOffset Time { get; init; }
}

sealed class MStockTrade
{
	public string Id { get; init; }
	public string OrderId { get; init; }
	public string Exchange { get; init; }
	public string Token { get; init; }
	public string Symbol { get; init; }
	public Sides Side { get; init; }
	public decimal Price { get; init; }
	public decimal Volume { get; init; }
	public DateTimeOffset Time { get; init; }
}
