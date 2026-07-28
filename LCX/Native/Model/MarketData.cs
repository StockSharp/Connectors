namespace StockSharp.LCX.Native.Model;

sealed class LcxMarket
{
	public string Id { get; init; }

	public string Symbol { get; init; }

	public string BaseCurrency { get; init; }

	public string QuoteCurrency { get; init; }

	public int PricePrecision { get; init; }

	public int AmountPrecision { get; init; }

	public decimal MinimumAmount { get; init; }

	public decimal MaximumAmount { get; init; }

	public bool IsActive { get; init; }
}

sealed class LcxTicker
{
	public string Symbol { get; init; }

	public DateTime Time { get; init; }

	public decimal LastPrice { get; init; }

	public decimal Bid { get; init; }

	public decimal Ask { get; init; }

	public decimal High { get; init; }

	public decimal Low { get; init; }

	public decimal Volume { get; init; }

	public decimal Change { get; init; }
}

sealed class LcxQuote
{
	public decimal Price { get; init; }

	public decimal Volume { get; init; }

	public Sides Side { get; init; }
}

sealed class LcxBook
{
	public string Symbol { get; init; }

	public bool IsSnapshot { get; init; }

	public LcxQuote[] Bids { get; init; } = [];

	public LcxQuote[] Asks { get; init; } = [];
}

sealed class LcxPublicTrade
{
	public string Id { get; init; }

	public string Symbol { get; init; }

	public DateTime Time { get; init; }

	public decimal Price { get; init; }

	public decimal Volume { get; init; }

	public Sides Side { get; init; }
}

sealed class LcxCandle
{
	public string Symbol { get; init; }

	public TimeSpan TimeFrame { get; init; }

	public DateTime OpenTime { get; init; }

	public decimal Open { get; init; }

	public decimal High { get; init; }

	public decimal Low { get; init; }

	public decimal Close { get; init; }

	public decimal Volume { get; init; }
}
