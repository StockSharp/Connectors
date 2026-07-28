namespace StockSharp.Buda.Native.Model;

sealed class BudaMarket
{
	public string Id { get; init; }

	public string Name { get; init; }

	public string BaseCurrency { get; init; }

	public string QuoteCurrency { get; init; }

	public decimal MinimumOrderAmount { get; init; }

	public string SecurityCode
		=> BudaExtensions.CreateSecurityCode(
			BaseCurrency, QuoteCurrency);
}

sealed class BudaTicker
{
	public string MarketId { get; init; }

	public decimal? LastPrice { get; init; }

	public decimal? BidPrice { get; init; }

	public decimal? AskPrice { get; init; }

	public decimal? Volume { get; init; }

	public decimal? PriceVariation24h { get; init; }
}

sealed class BudaQuote
{
	public decimal Price { get; init; }

	public decimal Volume { get; init; }
}

sealed class BudaOrderBook
{
	public BudaQuote[] Bids { get; init; } = [];

	public BudaQuote[] Asks { get; init; } = [];
}

sealed class BudaTrade
{
	public string Id { get; init; }

	public string MarketId { get; init; }

	public DateTime Time { get; init; }

	public decimal Volume { get; init; }

	public decimal Price { get; init; }

	public Sides Side { get; init; }
}
