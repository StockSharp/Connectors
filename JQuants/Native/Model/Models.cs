namespace StockSharp.JQuants.Native;

enum JQuantsInstrumentKinds
{
	Equity,
	Future,
	Option,
}

sealed class JQuantsInstrument
{
	public string Code { get; init; }
	public string Name { get; init; }
	public string EnglishName { get; init; }
	public string Market { get; init; }
	public string MarketName { get; init; }
	public string Sector { get; init; }
	public string SectorName { get; init; }
	public JQuantsInstrumentKinds Kind { get; init; }
	public string ProductCategory { get; init; }
	public string Underlying { get; init; }
	public decimal? Strike { get; init; }
	public OptionTypes? OptionType { get; init; }
	public DateTime? Expiry { get; init; }

	public string NativeId => Kind switch
	{
		JQuantsInstrumentKinds.Future => $"F:{Code}",
		JQuantsInstrumentKinds.Option => $"O:{Code}",
		_ => $"E:{Code}",
	};
}

sealed class JQuantsBar
{
	public string Code { get; init; }
	public DateTimeOffset Time { get; init; }
	public decimal Open { get; init; }
	public decimal High { get; init; }
	public decimal Low { get; init; }
	public decimal Close { get; init; }
	public decimal Volume { get; init; }
	public decimal? OpenInterest { get; init; }
}

sealed class JQuantsTrade
{
	public string Code { get; init; }
	public string Id { get; init; }
	public DateTimeOffset Time { get; init; }
	public decimal Price { get; init; }
	public decimal Volume { get; init; }
}
