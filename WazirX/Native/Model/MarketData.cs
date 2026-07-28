namespace StockSharp.WazirX.Native.Model;

sealed class WazirXMarket
{
	public string Symbol { get; init; }

	public string BaseAsset { get; init; }

	public string QuoteAsset { get; init; }

	public int BasePrecision { get; init; }

	public int QuotePrecision { get; init; }

	public decimal PriceStep { get; init; }

	public decimal MinimumPrice { get; init; }

	public decimal VolumeStep { get; init; }

	public decimal MinimumVolume { get; init; }

	public decimal MaximumVolume { get; init; }

	public bool IsActive { get; init; }

	public bool SupportsStopLimit { get; init; }
}

sealed class WazirXTicker
{
	public string Symbol { get; init; }

	public DateTime Time { get; init; }

	public decimal OpenPrice { get; init; }

	public decimal HighPrice { get; init; }

	public decimal LowPrice { get; init; }

	public decimal LastPrice { get; init; }

	public decimal Volume { get; init; }

	public decimal BidPrice { get; init; }

	public decimal AskPrice { get; init; }
}

sealed class WazirXQuote
{
	public decimal Price { get; init; }

	public decimal Volume { get; init; }

	public Sides Side { get; init; }
}

sealed class WazirXBook
{
	public string Symbol { get; init; }

	public DateTime Time { get; init; }

	public bool IsSnapshot { get; init; }

	public WazirXQuote[] Bids { get; init; } = [];

	public WazirXQuote[] Asks { get; init; } = [];
}

sealed class WazirXTrade
{
	public long Id { get; init; }

	public string Symbol { get; init; }

	public DateTime Time { get; init; }

	public decimal Price { get; init; }

	public decimal Volume { get; init; }

	public Sides Side { get; init; }
}

sealed class WazirXCandle
{
	public string Symbol { get; init; }

	public TimeSpan TimeFrame { get; init; }

	public DateTime OpenTime { get; init; }

	public DateTime CloseTime { get; init; }

	public decimal Open { get; init; }

	public decimal High { get; init; }

	public decimal Low { get; init; }

	public decimal Close { get; init; }

	public decimal Volume { get; init; }
}
