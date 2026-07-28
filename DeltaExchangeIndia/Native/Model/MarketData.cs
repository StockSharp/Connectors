namespace StockSharp.DeltaExchangeIndia.Native.Model;

sealed class DeltaProduct
{
	public int Id { get; init; }

	public string Symbol { get; init; }

	public string Description { get; init; }

	public string ContractType { get; init; }

	public string State { get; init; }

	public string TradingStatus { get; init; }

	public string UnderlyingAsset { get; init; }

	public string QuotingAsset { get; init; }

	public string SettlingAsset { get; init; }

	public decimal PriceStep { get; init; }

	public decimal ContractValue { get; init; }

	public decimal? Strike { get; init; }

	public DateTime? Expiry { get; init; }

	public bool IsActive
		=> State.EqualsIgnoreCase("live") &&
			TradingStatus.EqualsIgnoreCase("operational");

	public SecurityTypes SecurityType
		=> ContractType.ContainsIgnoreCase("option")
			? SecurityTypes.Option
			: SecurityTypes.Future;

	public OptionTypes? OptionType
		=> ContractType.EqualsIgnoreCase("call_options")
			? OptionTypes.Call
			: ContractType.EqualsIgnoreCase("put_options")
				? OptionTypes.Put
				: null;
}

sealed class DeltaTicker
{
	public string Symbol { get; init; }

	public DateTime Time { get; init; }

	public decimal? Open { get; init; }

	public decimal? High { get; init; }

	public decimal? Low { get; init; }

	public decimal? Last { get; init; }

	public decimal? MarkPrice { get; init; }

	public decimal? SpotPrice { get; init; }

	public decimal? BestBid { get; init; }

	public decimal? BestAsk { get; init; }

	public decimal? BidVolume { get; init; }

	public decimal? AskVolume { get; init; }

	public decimal? Volume { get; init; }

	public decimal? OpenInterest { get; init; }

	public decimal? FundingRate { get; init; }
}

sealed class DeltaQuote
{
	public decimal Price { get; init; }

	public decimal Volume { get; init; }
}

sealed class DeltaBook
{
	public string Symbol { get; init; }

	public DateTime Time { get; init; }

	public DeltaQuote[] Bids { get; init; } = [];

	public DeltaQuote[] Asks { get; init; } = [];
}

sealed class DeltaTrade
{
	public string Id { get; init; }

	public string Symbol { get; init; }

	public DateTime Time { get; init; }

	public decimal Price { get; init; }

	public decimal Volume { get; init; }

	public Sides Side { get; init; }
}

sealed class DeltaCandle
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
