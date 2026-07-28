namespace StockSharp.Coinmetro.Native.Model;

sealed class CoinmetroAsset
{
	public string Symbol { get; init; }

	public string Name { get; init; }

	public int Digits { get; init; }

	public int BookDigits { get; init; }

	public decimal MinimumQuantity { get; init; }
}

sealed class CoinmetroMarketSpec
{
	public string Pair { get; init; }

	public int Precision { get; init; }

	public bool IsMarginSupported { get; init; }
}

sealed class CoinmetroMarket
{
	public string Pair { get; init; }

	public string BaseCurrency { get; init; }

	public string QuoteCurrency { get; init; }

	public int PricePrecision { get; init; }

	public int AmountPrecision { get; init; }

	public int BookAmountPrecision { get; init; }

	public decimal MinimumAmount { get; init; }

	public bool IsMarginSupported { get; init; }

	public string SecurityCode
		=> CoinmetroExtensions.CreateSecurityCode(
			BaseCurrency, QuoteCurrency);
}

sealed class CoinmetroTicker
{
	public string Pair { get; init; }

	public string BaseCurrency { get; init; }

	public string QuoteCurrency { get; init; }

	public DateTime Time { get; init; }

	public long Sequence { get; init; }

	public decimal Price { get; init; }

	public decimal Volume { get; init; }

	public decimal? Ask { get; init; }

	public decimal? Bid { get; init; }
}

sealed class CoinmetroQuote
{
	public decimal Price { get; init; }

	public decimal Volume { get; init; }
}

sealed class CoinmetroBook
{
	public string Pair { get; init; }

	public long Sequence { get; init; }

	public int Checksum { get; init; }

	public CoinmetroQuote[] Bids { get; init; } = [];

	public CoinmetroQuote[] Asks { get; init; } = [];
}

sealed class CoinmetroTrade
{
	public string Id { get; init; }

	public string Pair { get; init; }

	public DateTime Time { get; init; }

	public decimal Price { get; init; }

	public decimal Volume { get; init; }

	public Sides? Side { get; init; }
}

sealed class CoinmetroCandle
{
	public string Pair { get; init; }

	public TimeSpan TimeFrame { get; init; }

	public DateTime OpenTime { get; init; }

	public decimal Open { get; init; }

	public decimal High { get; init; }

	public decimal Low { get; init; }

	public decimal Close { get; init; }

	public decimal Volume { get; init; }
}

sealed class CoinmetroBookUpdate
{
	public string Pair { get; init; }

	public long Sequence { get; init; }

	public int Checksum { get; init; }

	public CoinmetroQuote[] Bids { get; init; } = [];

	public CoinmetroQuote[] Asks { get; init; } = [];
}
