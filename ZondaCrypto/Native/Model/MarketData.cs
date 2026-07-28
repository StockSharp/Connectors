namespace StockSharp.ZondaCrypto.Native.Model;

sealed class ZondaCryptoMarket
{
	public string Code { get; init; }

	public string BaseCurrency { get; init; }

	public string QuoteCurrency { get; init; }

	public int AmountPrecision { get; init; }

	public int PricePrecision { get; init; }

	public int RatePrecision { get; init; }

	public decimal MinimumBaseAmount { get; init; }

	public decimal MinimumQuoteAmount { get; init; }

	public string SecurityCode
		=> ZondaCryptoExtensions.CreateSecurityCode(
			BaseCurrency, QuoteCurrency);
}

sealed class ZondaCryptoTicker
{
	public ZondaCryptoMarket Market { get; init; }

	public DateTime Time { get; init; }

	public decimal? BidPrice { get; init; }

	public decimal? AskPrice { get; init; }

	public decimal? LastPrice { get; init; }

	public decimal? PreviousPrice { get; init; }
}

sealed class ZondaCryptoQuote
{
	public decimal Price { get; init; }

	public decimal Volume { get; init; }

	public int OrderCount { get; init; }
}

sealed class ZondaCryptoOrderBook
{
	public ZondaCryptoQuote[] Bids { get; init; } = [];

	public ZondaCryptoQuote[] Asks { get; init; } = [];

	public DateTime Time { get; init; }

	public long Sequence { get; init; }
}

sealed class ZondaCryptoTrade
{
	public string Id { get; init; }

	public string MarketCode { get; init; }

	public DateTime Time { get; init; }

	public decimal Volume { get; init; }

	public decimal Price { get; init; }

	public Sides Side { get; init; }
}

sealed class ZondaCryptoBookChange
{
	public string MarketCode { get; init; }

	public Sides Side { get; init; }

	public decimal Price { get; init; }

	public decimal Volume { get; init; }

	public bool IsRemove { get; init; }
}
