namespace StockSharp.Aevo.Native.Model;

sealed class AevoInstrument
{
	[JsonProperty("instrument_id")]
	public string InstrumentId { get; init; }

	[JsonProperty("instrument_name")]
	public string InstrumentName { get; init; }

	[JsonProperty("instrument_type")]
	public AevoInstrumentTypes InstrumentType { get; init; }

	[JsonProperty("underlying_asset")]
	public string UnderlyingAsset { get; init; }

	[JsonProperty("asset")]
	public string Asset { get; init; }

	[JsonProperty("quote_asset")]
	public string QuoteAsset { get; init; }

	[JsonProperty("price_step")]
	public string PriceStep { get; init; }

	[JsonProperty("amount_step")]
	public string AmountStep { get; init; }

	[JsonProperty("min_order_value")]
	public string MinimumOrderValue { get; init; }

	[JsonProperty("max_order_value")]
	public string MaximumOrderValue { get; init; }

	[JsonProperty("max_notional_value")]
	public string MaximumNotionalValue { get; init; }

	[JsonProperty("mark_price")]
	public string MarkPrice { get; init; }

	[JsonProperty("index_price")]
	public string IndexPrice { get; init; }

	[JsonProperty("forward_price")]
	public string ForwardPrice { get; init; }

	[JsonProperty("is_active")]
	public bool IsActive { get; init; }

	[JsonProperty("max_leverage")]
	public string MaximumLeverage { get; init; }

	[JsonProperty("is_rwa")]
	public bool IsRwa { get; init; }

	[JsonProperty("market_type")]
	public string MarketType { get; init; }

	[JsonProperty("option_type")]
	public AevoOptionTypes? OptionType { get; init; }

	[JsonProperty("expiry")]
	public string Expiry { get; init; }

	[JsonProperty("strike")]
	public string Strike { get; init; }

	[JsonProperty("greeks")]
	public AevoGreeks Greeks { get; init; }

	[JsonProperty("best_bid")]
	public AevoPriceLevel BestBid { get; init; }

	[JsonProperty("best_ask")]
	public AevoPriceLevel BestAsk { get; init; }

	[JsonProperty("markets")]
	public AevoMarketStatistics Statistics { get; init; }

	[JsonProperty("funding_rate")]
	public string FundingRate { get; init; }

	[JsonProperty("initial_margin_fraction")]
	public string InitialMarginFraction { get; init; }

	[JsonProperty("maintenance_margin_fraction")]
	public string MaintenanceMarginFraction { get; init; }

	[JsonIgnore]
	public string BaseAsset => UnderlyingAsset ?? Asset;
}

sealed class AevoGreeks
{
	[JsonProperty("delta")]
	public string Delta { get; init; }

	[JsonProperty("gamma")]
	public string Gamma { get; init; }

	[JsonProperty("rho")]
	public string Rho { get; init; }

	[JsonProperty("theta")]
	public string Theta { get; init; }

	[JsonProperty("vega")]
	public string Vega { get; init; }

	[JsonProperty("iv")]
	public string ImpliedVolatility { get; init; }
}

sealed class AevoPriceLevel
{
	[JsonProperty("price")]
	public string Price { get; init; }

	[JsonProperty("amount")]
	public string Amount { get; init; }

	[JsonProperty("filled")]
	public string Filled { get; init; }

	[JsonProperty("delta")]
	public string Delta { get; init; }

	[JsonProperty("gamma")]
	public string Gamma { get; init; }

	[JsonProperty("rho")]
	public string Rho { get; init; }

	[JsonProperty("theta")]
	public string Theta { get; init; }

	[JsonProperty("vega")]
	public string Vega { get; init; }

	[JsonProperty("iv")]
	public string ImpliedVolatility { get; init; }
}

sealed class AevoMarketStatistics
{
	[JsonProperty("daily_volume")]
	public string DailyVolume { get; init; }

	[JsonProperty("daily_volume_contracts")]
	public string DailyVolumeContracts { get; init; }

	[JsonProperty("total_volume")]
	public string TotalVolume { get; init; }

	[JsonProperty("total_volume_contracts")]
	public string TotalVolumeContracts { get; init; }

	[JsonProperty("total_oi")]
	public string TotalOpenInterest { get; init; }
}

sealed class AevoOrderBook
{
	[JsonProperty("type")]
	public string Type { get; init; }

	[JsonProperty("instrument_id")]
	public string InstrumentId { get; init; }

	[JsonProperty("instrument_name")]
	public string InstrumentName { get; init; }

	[JsonProperty("instrument_type")]
	public AevoInstrumentTypes InstrumentType { get; init; }

	[JsonProperty("bids")]
	public string[][] Bids { get; init; }

	[JsonProperty("asks")]
	public string[][] Asks { get; init; }

	[JsonProperty("last_updated")]
	public string LastUpdated { get; init; }

	[JsonProperty("checksum")]
	public string Checksum { get; init; }
}

sealed class AevoTradesResponse
{
	[JsonProperty("count")]
	public string Count { get; init; }

	[JsonProperty("trade_history")]
	public AevoTrade[] Trades { get; init; }
}

sealed class AevoTrade
{
	[JsonProperty("trade_id")]
	public string TradeId { get; init; }

	[JsonProperty("order_id")]
	public string OrderId { get; init; }

	[JsonProperty("trade_type")]
	public string TradeType { get; init; }

	[JsonProperty("account")]
	public string Account { get; init; }

	[JsonProperty("instrument_id")]
	public string InstrumentId { get; init; }

	[JsonProperty("instrument_name")]
	public string InstrumentName { get; init; }

	[JsonProperty("instrument_type")]
	public AevoInstrumentTypes InstrumentType { get; init; }

	[JsonProperty("asset")]
	public string Asset { get; init; }

	[JsonProperty("side")]
	public string Side { get; init; }

	[JsonProperty("price")]
	public string Price { get; init; }

	[JsonProperty("avg_price")]
	public string AveragePrice { get; init; }

	[JsonProperty("amount")]
	public string Amount { get; init; }

	[JsonProperty("filled")]
	public string Filled { get; init; }

	[JsonProperty("created_timestamp")]
	public string CreatedTimestamp { get; init; }

	[JsonProperty("fees")]
	public string Fees { get; init; }

	[JsonProperty("liquidity")]
	public string Liquidity { get; init; }

	[JsonProperty("order_status")]
	public string OrderStatus { get; init; }

	[JsonProperty("trade_status")]
	public string TradeStatus { get; init; }

	[JsonProperty("system_type")]
	public string SystemType { get; init; }
}
