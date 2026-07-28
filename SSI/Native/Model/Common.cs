namespace StockSharp.SSI.Native.Model;

sealed class SSIInstrument
{
	public string Symbol { get; init; }
	public string Board { get; init; }
	public string Name { get; init; }
	public int LotSize { get; init; }
	public DateTimeOffset? MaturityDate { get; init; }
	public DateTimeOffset? FirstTradingDate { get; init; }
	public DateTimeOffset? LastTradingDate { get; init; }
	public string UnderlyingSymbol { get; init; }
	public decimal? ExercisePrice { get; init; }
	public decimal? ExecutionRatio { get; init; }
}

sealed class SSICandle
{
	public string Symbol { get; init; }
	public DateTimeOffset Time { get; init; }
	public decimal Open { get; init; }
	public decimal High { get; init; }
	public decimal Low { get; init; }
	public decimal Close { get; init; }
	public decimal Volume { get; init; }
	public decimal Turnover { get; init; }
}

sealed class SSITrade
{
	public string Symbol { get; init; }
	public DateTimeOffset Time { get; init; }
	public decimal Price { get; init; }
	public decimal Volume { get; init; }
	public Sides? Side { get; init; }
	public decimal TotalVolume { get; init; }
}

readonly record struct SSILevel(decimal Price, decimal Volume);

sealed class SSIDepth
{
	public string Symbol { get; init; }
	public DateTimeOffset Time { get; init; }
	public SSILevel[] Bids { get; init; } = [];
	public SSILevel[] Asks { get; init; } = [];
}

sealed class SSIOrder
{
	public string Account { get; init; }
	public string ClientRequestId { get; init; }
	public string OrderId { get; init; }
	public string Symbol { get; init; }
	public Sides Side { get; init; }
	public string OrderType { get; init; }
	public decimal Price { get; init; }
	public decimal AveragePrice { get; init; }
	public decimal Volume { get; init; }
	public decimal FilledVolume { get; init; }
	public decimal CancelledVolume { get; init; }
	public decimal Balance { get; init; }
	public string Status { get; init; }
	public DateTimeOffset Time { get; init; }
	public string Message { get; init; }
}

sealed class SSIOrderMatch
{
	public string Id { get; init; }
	public string Account { get; init; }
	public string OrderId { get; init; }
	public string Symbol { get; init; }
	public Sides Side { get; init; }
	public decimal Price { get; init; }
	public decimal Volume { get; init; }
	public DateTimeOffset Time { get; init; }
}
