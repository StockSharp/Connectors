namespace StockSharp.Polymarket.Native.Model;

sealed class PolymarketOrderBook
{
	[JsonProperty("market")]
	public string Market { get; init; }

	[JsonProperty("asset_id")]
	public string AssetId { get; init; }

	[JsonProperty("timestamp")]
	public string Timestamp { get; init; }

	[JsonProperty("hash")]
	public string Hash { get; init; }

	[JsonProperty("bids")]
	public PolymarketPriceLevel[] Bids { get; init; }

	[JsonProperty("asks")]
	public PolymarketPriceLevel[] Asks { get; init; }

	[JsonProperty("min_order_size")]
	public string MinimumOrderSize { get; init; }

	[JsonProperty("tick_size")]
	public string TickSize { get; init; }

	[JsonProperty("neg_risk")]
	public bool IsNegativeRisk { get; init; }

	[JsonProperty("last_trade_price")]
	public string LastTradePrice { get; init; }
}

sealed class PolymarketPriceLevel
{
	[JsonProperty("price")]
	public string Price { get; init; }

	[JsonProperty("size")]
	public string Size { get; init; }
}

sealed class PolymarketBookState
{
	private static readonly IComparer<decimal> _descending =
		Comparer<decimal>.Create(static (left, right) => right.CompareTo(left));

	public SortedDictionary<decimal, decimal> Bids { get; } = new(_descending);
	public SortedDictionary<decimal, decimal> Asks { get; } = [];
	public DateTime Time { get; private set; }

	public void Apply(PolymarketOrderBook book)
	{
		ArgumentNullException.ThrowIfNull(book);
		Bids.Clear();
		Asks.Clear();
		foreach (var level in book.Bids ?? [])
			Apply(Bids, level);
		foreach (var level in book.Asks ?? [])
			Apply(Asks, level);
		Time = book.Timestamp.ParsePolymarketMilliseconds();
	}

	public void Apply(PolymarketPriceChange change, DateTime time)
	{
		ArgumentNullException.ThrowIfNull(change);
		var levels = change.Side == PolymarketSides.Buy ? Bids : Asks;
		var price = change.Price.ParsePolymarketDecimal("book price");
		var size = change.Size.ParsePolymarketDecimal("book size");
		if (price <= 0 || price >= 1 || size < 0)
			throw new InvalidDataException(
				"Polymarket returned an invalid order-book change.");
		if (size == 0)
			levels.Remove(price);
		else
			levels[price] = size;
		Time = time;
	}

	private static void Apply(IDictionary<decimal, decimal> levels,
		PolymarketPriceLevel level)
	{
		if (level is null)
			return;
		var price = level.Price.ParsePolymarketDecimal("book price");
		var size = level.Size.ParsePolymarketDecimal("book size");
		if (price > 0 && size > 0)
			levels[price] = size;
	}
}

class PolymarketMarketSubscription
{
	public long TransactionId { get; init; }
	public string TokenId { get; init; }
}

sealed class PolymarketDepthSubscription : PolymarketMarketSubscription
{
	public int Depth { get; init; }
}

sealed class PolymarketCandle
{
	public DateTime OpenTime { get; init; }
	public decimal Open { get; init; }
	public decimal High { get; init; }
	public decimal Low { get; init; }
	public decimal Close { get; init; }
}
