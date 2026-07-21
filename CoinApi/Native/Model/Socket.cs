namespace StockSharp.CoinApi.Native.Model;

sealed class CoinApiSocketRequest
{
	[JsonProperty("type")]
	public CoinApiSocketRequestTypes Type { get; set; }

	[JsonProperty("apikey", NullValueHandling = NullValueHandling.Ignore)]
	public string ApiKey { get; set; }

	[JsonProperty("heartbeat")]
	public bool IsHeartbeat { get; set; }

	[JsonProperty("subscribe_data_type")]
	public CoinApiSocketDataTypes[] DataTypes { get; set; }

	[JsonProperty("subscribe_filter_symbol_id")]
	public string[] SymbolIds { get; set; }

	[JsonProperty("subscribe_filter_period_id",
		NullValueHandling = NullValueHandling.Ignore)]
	public CoinApiPeriodIds[] PeriodIds { get; set; }
}

sealed class CoinApiSocketMessage
{
	[JsonProperty("type")]
	public CoinApiSocketMessageTypes Type { get; set; }

	[JsonProperty("symbol_id")]
	public string SymbolId { get; set; }

	[JsonProperty("sequence")]
	public long? Sequence { get; set; }

	[JsonProperty("time_exchange")]
	public string ExchangeTime { get; set; }

	[JsonProperty("time_coinapi")]
	public string CoinApiTime { get; set; }

	[JsonProperty("uuid")]
	public string Uuid { get; set; }

	[JsonProperty("price")]
	public decimal? Price { get; set; }

	[JsonProperty("size")]
	public decimal? Size { get; set; }

	[JsonProperty("taker_side")]
	public CoinApiTakerSides TakerSide { get; set; }

	[JsonProperty("ask_price")]
	public decimal? AskPrice { get; set; }

	[JsonProperty("ask_size")]
	public decimal? AskSize { get; set; }

	[JsonProperty("bid_price")]
	public decimal? BidPrice { get; set; }

	[JsonProperty("bid_size")]
	public decimal? BidSize { get; set; }

	[JsonProperty("is_snapshot")]
	public bool? IsSnapshot { get; set; }

	[JsonProperty("asks")]
	public CoinApiBookLevel[] Asks { get; set; }

	[JsonProperty("bids")]
	public CoinApiBookLevel[] Bids { get; set; }

	[JsonProperty("period_id")]
	public CoinApiPeriodIds PeriodId { get; set; }

	[JsonProperty("time_period_start")]
	public string PeriodStartTime { get; set; }

	[JsonProperty("time_period_end")]
	public string PeriodEndTime { get; set; }

	[JsonProperty("time_open")]
	public string OpenTime { get; set; }

	[JsonProperty("time_close")]
	public string CloseTime { get; set; }

	[JsonProperty("price_open")]
	public decimal? OpenPrice { get; set; }

	[JsonProperty("price_high")]
	public decimal? HighPrice { get; set; }

	[JsonProperty("price_low")]
	public decimal? LowPrice { get; set; }

	[JsonProperty("price_close")]
	public decimal? ClosePrice { get; set; }

	[JsonProperty("volume_traded")]
	public decimal? TradedVolume { get; set; }

	[JsonProperty("trades_count")]
	public long? TradesCount { get; set; }

	[JsonProperty("message")]
	public string Message { get; set; }

	[JsonProperty("within_seconds")]
	public int? WithinSeconds { get; set; }

	[JsonProperty("before_time")]
	public string BeforeTime { get; set; }
}
