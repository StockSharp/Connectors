namespace StockSharp.Samco.Native;

sealed class SamcoInstrument
{
	public string Exchange { get; set; }
	public string ExchangeSegment { get; set; }
	public string SymbolCode { get; set; }
	public string TradingSymbol { get; set; }
	public string Name { get; set; }
	public string LastPrice { get; set; }
	public string Instrument { get; set; }
	public string LotSize { get; set; }
	public string StrikePrice { get; set; }
	public string ExpiryDate { get; set; }
	public string TickSize { get; set; }

	public decimal Lot => LotSize.ToSamcoDecimal() ?? 1;
	public decimal Tick => TickSize.ToSamcoDecimal() ?? 0;
	public decimal? Strike => StrikePrice.ToSamcoDecimal();
}

readonly record struct SamcoInstrumentRef(
	string Exchange,
	string SymbolCode,
	string TradingSymbol,
	string Name,
	decimal LotSize,
	string Instrument)
{
	public string Key => $"{Exchange}:{SymbolCode}";

	public string OrderSymbol =>
		Instrument.EqualsIgnoreCase("EQ") ||
		Instrument.EqualsIgnoreCase("BE") ||
		Instrument.EqualsIgnoreCase("SM")
			? Name.IsEmpty(TradingSymbol).IsEmpty(SymbolCode)
			: TradingSymbol.IsEmpty(Name).IsEmpty(SymbolCode);
}

readonly record struct SamcoDepthLevel(
	decimal Price,
	decimal Volume,
	int Orders);

sealed class SamcoFeed
{
	public string SymbolCode { get; init; }
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
	public SamcoDepthLevel[] Bids { get; init; }
	public SamcoDepthLevel[] Asks { get; init; }
}

sealed class SamcoCandle
{
	public DateTimeOffset Time { get; init; }
	public decimal Open { get; init; }
	public decimal High { get; init; }
	public decimal Low { get; init; }
	public decimal Close { get; init; }
	public decimal Volume { get; init; }
}

sealed class SamcoOrder
{
	public string OrderId { get; init; }
	public string ExchangeOrderId { get; init; }
	public string Exchange { get; init; }
	public string SymbolCode { get; init; }
	public string Symbol { get; init; }
	public Sides Side { get; init; }
	public string OrderType { get; init; }
	public string Product { get; init; }
	public string Validity { get; init; }
	public decimal Price { get; init; }
	public decimal TriggerPrice { get; init; }
	public decimal Volume { get; init; }
	public decimal FilledVolume { get; init; }
	public decimal Balance { get; init; }
	public decimal AveragePrice { get; init; }
	public string Status { get; init; }
	public string Text { get; init; }
	public DateTimeOffset Time { get; init; }
}

sealed class SamcoTrade
{
	public string Id { get; init; }
	public string OrderId { get; init; }
	public string Exchange { get; init; }
	public string SymbolCode { get; init; }
	public string Symbol { get; init; }
	public Sides Side { get; init; }
	public decimal Price { get; init; }
	public decimal Volume { get; init; }
	public DateTimeOffset Time { get; init; }
}
