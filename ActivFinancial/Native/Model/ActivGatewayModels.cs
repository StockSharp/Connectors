namespace StockSharp.ActivFinancial.Native.Model;

internal static class ActivGatewayProtocol
{
	public const int Version = 1;
	public const int MaxMessageLength = 16 * 1024 * 1024;
}

internal enum ActivGatewayCommands
{
	Connect = 1,
	Disconnect = 2,
	Lookup = 3,
	Snapshot = 4,
	HistoryTicks = 5,
	HistoryBars = 6,
	Subscribe = 7,
	Unsubscribe = 8,
}

internal enum ActivGatewayMessageKinds
{
	Response = 1,
	Record = 2,
	SubscriptionFinished = 3,
	Error = 4,
	Log = 5,
}

internal enum ActivGatewayDataKinds
{
	Level1 = 1,
	Ticks = 2,
}

internal sealed class ActivGatewayRequest
{
	[JsonProperty("version")]
	public int Version { get; set; } = ActivGatewayProtocol.Version;

	[JsonProperty("request_id")]
	public long RequestId { get; set; }

	[JsonProperty("command")]
	public ActivGatewayCommands Command { get; set; }

	[JsonProperty("host")]
	public string Host { get; set; }

	[JsonProperty("user")]
	public string User { get; set; }

	[JsonProperty("password")]
	public string Password { get; set; }

	[JsonProperty("data_source_id")]
	public int? DataSourceId { get; set; }

	[JsonProperty("symbology_id")]
	public int? SymbologyId { get; set; }

	[JsonProperty("symbol")]
	public string Symbol { get; set; }

	[JsonProperty("query")]
	public string Query { get; set; }

	[JsonProperty("skip")]
	public int? Skip { get; set; }

	[JsonProperty("limit")]
	public int? Limit { get; set; }

	[JsonProperty("subscription_id")]
	public long? SubscriptionId { get; set; }

	[JsonProperty("data_kind")]
	public ActivGatewayDataKinds? DataKind { get; set; }

	[JsonProperty("from_utc")]
	public long? FromUtc { get; set; }

	[JsonProperty("to_utc")]
	public long? ToUtc { get; set; }

	[JsonProperty("count")]
	public int? Count { get; set; }

	[JsonProperty("time_frame_minutes")]
	public int? TimeFrameMinutes { get; set; }

	[JsonProperty("fallback_time_zone")]
	public string FallbackTimeZone { get; set; }
}

internal sealed class ActivGatewayMessage
{
	[JsonProperty("version")]
	public int Version { get; set; }

	[JsonProperty("kind")]
	public ActivGatewayMessageKinds Kind { get; set; }

	[JsonProperty("request_id")]
	public long RequestId { get; set; }

	[JsonProperty("subscription_id")]
	public long? SubscriptionId { get; set; }

	[JsonProperty("gateway_version")]
	public string GatewayVersion { get; set; }

	[JsonProperty("one_api_version")]
	public string OneApiVersion { get; set; }

	[JsonProperty("records")]
	public ActivGatewayRecord[] Records { get; set; }

	[JsonProperty("record")]
	public ActivGatewayRecord Record { get; set; }

	[JsonProperty("error")]
	public ActivGatewayError Error { get; set; }

	[JsonProperty("log_level")]
	public int? LogLevel { get; set; }

	[JsonProperty("log_message")]
	public string LogMessage { get; set; }
}

internal sealed class ActivGatewayError
{
	[JsonProperty("code")]
	public string Code { get; set; }

	[JsonProperty("message")]
	public string Message { get; set; }
}

internal sealed class ActivGatewayTimestamp
{
	[JsonProperty("year")]
	public int? Year { get; set; }

	[JsonProperty("month")]
	public int? Month { get; set; }

	[JsonProperty("day")]
	public int? Day { get; set; }

	[JsonProperty("hour")]
	public int? Hour { get; set; }

	[JsonProperty("minute")]
	public int? Minute { get; set; }

	[JsonProperty("second")]
	public int? Second { get; set; }

	[JsonProperty("fraction_ticks")]
	public int FractionTicks { get; set; }

	[JsonProperty("is_date_only")]
	public bool IsDateOnly { get; set; }

	[JsonProperty("is_time_only")]
	public bool IsTimeOnly { get; set; }
}

internal sealed class ActivGatewayRecord
{
	[JsonProperty("symbol")]
	public string Symbol { get; set; }

	[JsonProperty("data_source_id")]
	public int DataSourceId { get; set; }

	[JsonProperty("symbology_id")]
	public int SymbologyId { get; set; }

	[JsonProperty("permission_id")]
	public int? PermissionId { get; set; }

	[JsonProperty("update_id")]
	public long? UpdateId { get; set; }

	[JsonProperty("event_type")]
	public int? EventType { get; set; }

	[JsonProperty("tick_type")]
	public int? TickType { get; set; }

	[JsonProperty("is_refresh")]
	public bool IsRefresh { get; set; }

	[JsonProperty("time_zone")]
	public string TimeZone { get; set; }

	[JsonProperty("date_time")]
	public ActivGatewayTimestamp DateTime { get; set; }

	[JsonProperty("last_update_date_time")]
	public ActivGatewayTimestamp LastUpdateDateTime { get; set; }

	[JsonProperty("trade_date")]
	public ActivGatewayTimestamp TradeDate { get; set; }

	[JsonProperty("trade_time")]
	public ActivGatewayTimestamp TradeTime { get; set; }

	[JsonProperty("bid_time")]
	public ActivGatewayTimestamp BidTime { get; set; }

	[JsonProperty("ask_time")]
	public ActivGatewayTimestamp AskTime { get; set; }

	[JsonProperty("name")]
	public string Name { get; set; }

	[JsonProperty("description")]
	public string Description { get; set; }

	[JsonProperty("entity_type")]
	public int? EntityType { get; set; }

	[JsonProperty("currency")]
	public string Currency { get; set; }

	[JsonProperty("exchange")]
	public string Exchange { get; set; }

	[JsonProperty("mic")]
	public string Mic { get; set; }

	[JsonProperty("country_code")]
	public string CountryCode { get; set; }

	[JsonProperty("isin")]
	public string Isin { get; set; }

	[JsonProperty("cusip")]
	public string Cusip { get; set; }

	[JsonProperty("sedol")]
	public string Sedol { get; set; }

	[JsonProperty("expiration")]
	public ActivGatewayTimestamp Expiration { get; set; }

	[JsonProperty("strike_price")]
	public decimal? StrikePrice { get; set; }

	[JsonProperty("option_type")]
	public string OptionType { get; set; }

	[JsonProperty("underlying_symbol")]
	public string UnderlyingSymbol { get; set; }

	[JsonProperty("minimum_tick")]
	public decimal? MinimumTick { get; set; }

	[JsonProperty("contract_size")]
	public decimal? ContractSize { get; set; }

	[JsonProperty("lot_size")]
	public decimal? LotSize { get; set; }

	[JsonProperty("bid_price")]
	public decimal? BidPrice { get; set; }

	[JsonProperty("bid_size")]
	public decimal? BidSize { get; set; }

	[JsonProperty("ask_price")]
	public decimal? AskPrice { get; set; }

	[JsonProperty("ask_size")]
	public decimal? AskSize { get; set; }

	[JsonProperty("trade_price")]
	public decimal? TradePrice { get; set; }

	[JsonProperty("trade_size")]
	public decimal? TradeSize { get; set; }

	[JsonProperty("trade_id")]
	public string TradeId { get; set; }

	[JsonProperty("tick_price")]
	public decimal? TickPrice { get; set; }

	[JsonProperty("tick_size")]
	public decimal? TickSize { get; set; }

	[JsonProperty("tick_condition")]
	public string TickCondition { get; set; }

	[JsonProperty("open_price")]
	public decimal? OpenPrice { get; set; }

	[JsonProperty("high_price")]
	public decimal? HighPrice { get; set; }

	[JsonProperty("low_price")]
	public decimal? LowPrice { get; set; }

	[JsonProperty("close_price")]
	public decimal? ClosePrice { get; set; }

	[JsonProperty("previous_close")]
	public decimal? PreviousClose { get; set; }

	[JsonProperty("settlement_price")]
	public decimal? SettlementPrice { get; set; }

	[JsonProperty("cumulative_volume")]
	public decimal? CumulativeVolume { get; set; }

	[JsonProperty("open_interest")]
	public decimal? OpenInterest { get; set; }

	[JsonProperty("tick_count")]
	public long? TickCount { get; set; }

	[JsonProperty("trading_status")]
	public string TradingStatus { get; set; }

	[JsonProperty("net_change")]
	public decimal? NetChange { get; set; }

	[JsonProperty("percent_change")]
	public decimal? PercentChange { get; set; }
}
