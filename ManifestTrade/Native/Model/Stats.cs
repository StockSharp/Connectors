namespace StockSharp.ManifestTrade.Native.Model;

sealed class ManifestTradeTicker
{
	[JsonProperty("ticker_id")]
	public string TickerId { get; init; }

	[JsonProperty("base_currency")]
	public string BaseMint { get; init; }

	[JsonProperty("target_currency")]
	public string QuoteMint { get; init; }

	[JsonProperty("last_price")]
	public decimal LastPrice { get; init; }

	[JsonProperty("base_volume")]
	public decimal BaseVolume { get; init; }

	[JsonProperty("target_volume")]
	public decimal QuoteVolume { get; init; }

	[JsonProperty("pool_id")]
	public string MarketAddress { get; init; }

	[JsonProperty("liquidity_in_usd")]
	public decimal LiquidityUsd { get; init; }
}

sealed class ManifestTradeStatsException : InvalidOperationException
{
	public ManifestTradeStatsException(HttpStatusCode statusCode,
		string message)
		: base(message)
		=> StatusCode = statusCode;

	public HttpStatusCode StatusCode { get; }
}
