namespace StockSharp.AltCoinTrader.Native.Model;

sealed class AltCoinTraderMarket
{
	[JsonProperty("symbol")]
	public string Symbol { get; set; }

	[JsonProperty("base")]
	public string Base { get; set; }

	[JsonProperty("quote")]
	public string Quote { get; set; }

	[JsonProperty("status")]
	public string Status { get; set; }

	[JsonProperty("min_order_value")]
	public decimal? MinimumOrderValue { get; set; }

	[JsonProperty("price_precision")]
	public int PricePrecision { get; set; }

	[JsonProperty("quantity_precision")]
	public int QuantityPrecision { get; set; }

	[JsonIgnore]
	public string SecurityCode => Symbol?.Trim().ToUpperInvariant();

	[JsonIgnore]
	public decimal? PriceStep
		=> AltCoinTraderExtensions.GetStep(PricePrecision);

	[JsonIgnore]
	public decimal? QuantityStep
		=> AltCoinTraderExtensions.GetStep(QuantityPrecision);

	[JsonIgnore]
	public bool IsActive => Status.EqualsIgnoreCase("active");
}

sealed class AltCoinTraderTicker
{
	[JsonProperty("symbol")]
	public string Symbol { get; set; }

	[JsonProperty("last")]
	public decimal? LastPrice { get; set; }

	[JsonProperty("open")]
	public decimal? OpenPrice { get; set; }

	[JsonProperty("high")]
	public decimal? HighPrice { get; set; }

	[JsonProperty("low")]
	public decimal? LowPrice { get; set; }

	[JsonProperty("change")]
	public decimal? PriceChange { get; set; }

	[JsonProperty("change_pct")]
	public decimal? PriceChangePercent { get; set; }

	[JsonProperty("volume")]
	public decimal? Volume { get; set; }

	[JsonProperty("quote_volume")]
	public decimal? QuoteVolume { get; set; }

	[JsonProperty("bidPrice")]
	public decimal? BidPrice { get; set; }

	[JsonProperty("askPrice")]
	public decimal? AskPrice { get; set; }

	[JsonProperty("timestamp")]
	public long Timestamp { get; set; }
}

sealed class AltCoinTraderOrderBook
{
	[JsonProperty("symbol")]
	public string Symbol { get; set; }

	[JsonProperty("bids")]
	public decimal[][] Bids { get; set; }

	[JsonProperty("asks")]
	public decimal[][] Asks { get; set; }

	[JsonProperty("timestamp")]
	public long Timestamp { get; set; }
}

sealed class AltCoinTraderTrade
{
	[JsonProperty("trade_id")]
	public string TradeId { get; set; }

	[JsonProperty("market")]
	public string Market { get; set; }

	[JsonProperty("price")]
	public decimal Price { get; set; }

	[JsonProperty("quantity")]
	public decimal Quantity { get; set; }

	[JsonProperty("side")]
	public string Side { get; set; }

	[JsonProperty("timestamp")]
	public long Timestamp { get; set; }
}
