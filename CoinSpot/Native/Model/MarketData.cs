namespace StockSharp.CoinSpot.Native.Model;

sealed class CoinSpotMarket
{
	public CoinSpotMarket(
		string nativeSymbol,
		decimal? bidPrice,
		decimal? askPrice,
		decimal? lastPrice)
	{
		NativeSymbol = nativeSymbol.ThrowIfEmpty(
			nameof(nativeSymbol)).Trim().ToLowerInvariant();
		(BaseUnit, QuoteUnit) = NativeSymbol.ToCoinSpotCurrencies();
		Ticker = new()
		{
			BidPrice = bidPrice,
			AskPrice = askPrice,
			LastPrice = lastPrice,
		};
	}

	public string NativeSymbol { get; }

	public string Id => NativeSymbol;

	public string BaseUnit { get; }

	public string QuoteUnit { get; }

	public string SecurityCode
		=> CoinSpotExtensions.CreateSecurityCode(BaseUnit, QuoteUnit);

	public string Name => SecurityCode;

	public CoinSpotTicker Ticker { get; }

	public decimal? PriceStep => null;

	public decimal? VolumeStep => null;

	public decimal? MinimumOrderValue => null;
}

sealed class CoinSpotTicker
{
	public decimal? BidPrice { get; set; }

	public decimal? AskPrice { get; set; }

	public decimal? LastPrice { get; set; }

	public long Timestamp { get; set; }
}

sealed class CoinSpotQuote
{
	public decimal Price { get; init; }

	public decimal Volume { get; init; }
}

sealed class CoinSpotDepth
{
	public string Market { get; init; }

	public CoinSpotQuote[] Bids { get; init; } = [];

	public CoinSpotQuote[] Asks { get; init; } = [];

	public DateTime? Time { get; init; }
}

sealed class CoinSpotTrade
{
	public string Id { get; init; }

	public string Market { get; init; }

	public decimal Price { get; init; }

	public decimal Volume { get; init; }

	public DateTime Time { get; init; }

	public Sides Side { get; init; }
}

