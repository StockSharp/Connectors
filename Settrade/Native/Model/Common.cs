namespace StockSharp.Settrade.Native.Model;

enum SettradeStreamKinds
{
	Info,
	BidOffer,
	Candle,
	EquityOrder,
	DerivativeOrder,
}

sealed class SettradeDispatcher
{
	public string Host { get; init; }
	public string Token { get; init; }
}

sealed class SettradeLevel1
{
	public string Symbol { get; init; }
	public decimal? ProjectedOpenPrice { get; init; }
	public decimal? High { get; init; }
	public decimal? Low { get; init; }
	public decimal? Last { get; init; }
	public decimal? Change { get; init; }
	public decimal? TotalVolume { get; init; }
	public decimal? TotalValue { get; init; }
	public int MarketStatus { get; init; }
}

sealed class SettradeOrderBook
{
	public string Symbol { get; init; }
	public SettradeBookLevel[] Bids { get; init; } = [];
	public SettradeBookLevel[] Asks { get; init; } = [];
}

readonly record struct SettradeBookLevel(decimal Price, decimal Volume);

sealed class SettradeCandle
{
	public string Symbol { get; init; }
	public string Interval { get; init; }
	public long Sequence { get; init; }
	public DateTime Time { get; init; }
	public decimal Open { get; init; }
	public decimal High { get; init; }
	public decimal Low { get; init; }
	public decimal Close { get; init; }
	public decimal Volume { get; init; }
	public decimal Turnover { get; init; }
}

sealed class SettradeOrder
{
	public string OrderNo { get; init; }
	public string AccountNo { get; init; }
	public string Symbol { get; init; }
	public string Side { get; init; }
	public string Position { get; init; }
	public string PriceType { get; init; }
	public string Validity { get; init; }
	public string Status { get; init; }
	public decimal Price { get; init; }
	public decimal Volume { get; init; }
	public decimal MatchedVolume { get; init; }
	public decimal BalanceVolume { get; init; }
	public decimal CancelledVolume { get; init; }
	public DateTime Time { get; init; }
	public int Version { get; init; }
	public bool CanCancel { get; init; }
}

sealed class SettradeAccountSnapshot
{
	public JObject Account { get; init; }
	public JObject[] Portfolios { get; init; } = [];
	public JObject[] Orders { get; init; } = [];
	public JObject[] Trades { get; init; } = [];
}
