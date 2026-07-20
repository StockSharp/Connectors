namespace StockSharp.Aevo.Native.Model;

sealed class AevoAccount
{
	[JsonProperty("account")]
	public string Account { get; init; }

	[JsonProperty("equity")]
	public string Equity { get; init; }

	[JsonProperty("available_balance")]
	public string AvailableBalance { get; init; }

	[JsonProperty("available_margin")]
	public string AvailableMargin { get; init; }

	[JsonProperty("balance")]
	public string Balance { get; init; }

	[JsonProperty("initial_margin")]
	public string InitialMargin { get; init; }

	[JsonProperty("maintenance_margin")]
	public string MaintenanceMargin { get; init; }

	[JsonProperty("collaterals")]
	public AevoCollateral[] Collaterals { get; init; }

	[JsonProperty("signing_keys")]
	public AevoSigningKey[] SigningKeys { get; init; }

	[JsonProperty("positions")]
	public AevoPosition[] Positions { get; init; }
}

sealed class AevoSigningKey
{
	[JsonProperty("signing_key")]
	public string SigningKey { get; init; }

	[JsonProperty("expiry")]
	public string Expiry { get; init; }

	[JsonProperty("created_timestamp")]
	public string CreatedTimestamp { get; init; }
}

sealed class AevoCollateral
{
	[JsonProperty("collateral_asset")]
	public string Asset { get; init; }

	[JsonProperty("collateral_value")]
	public string Value { get; init; }

	[JsonProperty("margin_value")]
	public string MarginValue { get; init; }

	[JsonProperty("balance")]
	public string Balance { get; init; }

	[JsonProperty("available_balance")]
	public string AvailableBalance { get; init; }

	[JsonProperty("pending_withdrawals")]
	public string PendingWithdrawals { get; init; }

	[JsonProperty("unrealized_pnl")]
	public string UnrealizedPnl { get; init; }
}

sealed class AevoPositionsResponse
{
	[JsonProperty("account")]
	public string Account { get; init; }

	[JsonProperty("positions")]
	public AevoPosition[] Positions { get; init; }
}

sealed class AevoPosition
{
	[JsonProperty("instrument_id")]
	public string InstrumentId { get; init; }

	[JsonProperty("instrument_name")]
	public string InstrumentName { get; init; }

	[JsonProperty("instrument_type")]
	public AevoInstrumentTypes InstrumentType { get; init; }

	[JsonProperty("asset")]
	public string Asset { get; init; }

	[JsonProperty("amount")]
	public string Amount { get; init; }

	[JsonProperty("side")]
	public string Side { get; init; }

	[JsonProperty("mark_price")]
	public string MarkPrice { get; init; }

	[JsonProperty("avg_entry_price")]
	public string AverageEntryPrice { get; init; }

	[JsonProperty("unrealized_pnl")]
	public string UnrealizedPnl { get; init; }

	[JsonProperty("initial_margin")]
	public string InitialMargin { get; init; }

	[JsonProperty("maintenance_margin")]
	public string MaintenanceMargin { get; init; }

	[JsonProperty("liquidation_price")]
	public string LiquidationPrice { get; init; }

	[JsonProperty("leverage")]
	public string Leverage { get; init; }

	[JsonProperty("option")]
	public AevoPositionOption Option { get; init; }

	[JsonProperty("system_type")]
	public string SystemType { get; init; }
}

sealed class AevoPositionOption
{
	[JsonProperty("strike")]
	public string Strike { get; init; }

	[JsonProperty("option_type")]
	public AevoOptionTypes OptionType { get; init; }

	[JsonProperty("expiry")]
	public string Expiry { get; init; }

	[JsonProperty("iv")]
	public string ImpliedVolatility { get; init; }

	[JsonProperty("delta")]
	public string Delta { get; init; }

	[JsonProperty("theta")]
	public string Theta { get; init; }

	[JsonProperty("rho")]
	public string Rho { get; init; }

	[JsonProperty("vega")]
	public string Vega { get; init; }
}

sealed class AevoPortfolio
{
	[JsonProperty("balance")]
	public string Balance { get; init; }

	[JsonProperty("pnl")]
	public string Pnl { get; init; }

	[JsonProperty("realized_pnl")]
	public string RealizedPnl { get; init; }

	[JsonProperty("user_margin")]
	public AevoUserMargin UserMargin { get; init; }
}

sealed class AevoUserMargin
{
	[JsonProperty("used")]
	public string Used { get; init; }

	[JsonProperty("balance")]
	public string Balance { get; init; }
}

sealed class AevoOrder
{
	[JsonProperty("order_id")]
	public string OrderId { get; init; }

	[JsonProperty("account")]
	public string Account { get; init; }

	[JsonProperty("instrument_id")]
	public string InstrumentId { get; init; }

	[JsonProperty("instrument_name")]
	public string InstrumentName { get; init; }

	[JsonProperty("instrument_type")]
	public AevoInstrumentTypes InstrumentType { get; init; }

	[JsonProperty("order_type")]
	public string OrderType { get; init; }

	[JsonProperty("side")]
	public string Side { get; init; }

	[JsonProperty("amount")]
	public string Amount { get; init; }

	[JsonProperty("price")]
	public string Price { get; init; }

	[JsonProperty("avg_price")]
	public string AveragePrice { get; init; }

	[JsonProperty("filled")]
	public string Filled { get; init; }

	[JsonProperty("order_status")]
	public string Status { get; init; }

	[JsonProperty("post_only")]
	public bool IsPostOnly { get; init; }

	[JsonProperty("reduce_only")]
	public bool IsReduceOnly { get; init; }

	[JsonProperty("mmp")]
	public bool IsMmp { get; init; }

	[JsonProperty("created_timestamp")]
	public string CreatedTimestamp { get; init; }

	[JsonProperty("timestamp")]
	public string Timestamp { get; init; }

	[JsonProperty("time_in_force")]
	public string TimeInForce { get; init; }

	[JsonProperty("error")]
	public string Error { get; init; }

	[JsonProperty("stop")]
	public string Stop { get; init; }

	[JsonProperty("trigger")]
	public string Trigger { get; init; }
}

sealed class AevoOrderHistoryResponse
{
	[JsonProperty("count")]
	public string Count { get; init; }

	[JsonProperty("order_history")]
	public AevoOrder[] Orders { get; init; }
}

sealed class AevoPrivateTradesResponse
{
	[JsonProperty("count")]
	public string Count { get; init; }

	[JsonProperty("trade_history")]
	public AevoTrade[] Trades { get; init; }
}

sealed class AevoOrderRequest
{
	[JsonProperty("instrument")]
	public long Instrument { get; init; }

	[JsonProperty("maker")]
	public string Maker { get; init; }

	[JsonProperty("is_buy")]
	public bool IsBuy { get; init; }

	[JsonProperty("amount")]
	public string Amount { get; init; }

	[JsonProperty("limit_price")]
	public string LimitPrice { get; init; }

	[JsonProperty("salt")]
	public string Salt { get; init; }

	[JsonProperty("signature")]
	public string Signature { get; init; }

	[JsonProperty("timestamp")]
	public long Timestamp { get; init; }

	[JsonProperty("post_only")]
	public bool IsPostOnly { get; init; }

	[JsonProperty("reduce_only")]
	public bool IsReduceOnly { get; init; }

	[JsonProperty("time_in_force")]
	public string TimeInForce { get; init; }

	[JsonProperty("mmp")]
	public bool IsMmp { get; init; }
}

sealed class AevoCancelAllRequest
{
	[JsonProperty("asset", NullValueHandling = NullValueHandling.Ignore)]
	public string Asset { get; init; }

	[JsonProperty("instrument_type", NullValueHandling = NullValueHandling.Ignore)]
	public AevoInstrumentTypes? InstrumentType { get; init; }
}

sealed class AevoCancelOrderResponse
{
	[JsonProperty("order_id")]
	public string OrderId { get; init; }
}

sealed class AevoCancelAllResponse
{
	[JsonProperty("success")]
	public bool IsSuccess { get; init; }

	[JsonProperty("order_ids")]
	public string[] OrderIds { get; init; }
}
