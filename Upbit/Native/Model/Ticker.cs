namespace StockSharp.Upbit.Native.Model;

class Ticker : BaseEvent
{
	[JsonProperty("opening_price")]
	public double? OpeningPrice { get; set; }

	[JsonProperty("high_price")]
	public double? HighPrice { get; set; }

	[JsonProperty("low_price")]
	public double? LowPrice { get; set; }

	[JsonProperty("trade_price")]
	public double? TradePrice { get; set; }

	[JsonProperty("prev_closing_price")]
	public double? PrevClosingPrice { get; set; }

	[JsonProperty("acc_trade_price")]
	public double? AccTradePrice { get; set; }

	[JsonProperty("change")]
	public string Change { get; set; }

	[JsonProperty("change_price")]
	public double? ChangePrice { get; set; }

	[JsonProperty("signed_change_price")]
	public double? SignedChangePrice { get; set; }

	[JsonProperty("change_rate")]
	public double? ChangeRate { get; set; }

	[JsonProperty("signed_change_rate")]
	public double? SignedChangeRate { get; set; }

	[JsonProperty("ask_bid")]
	public string AskBid { get; set; }

	[JsonProperty("trade_volume")]
	public double? TradeVolume { get; set; }

	[JsonProperty("acc_trade_volume")]
	public double? AccTradeVolume { get; set; }

	[JsonProperty("trade_date")]
	public string TradeDate { get; set; }

	[JsonProperty("trade_time")]
	public long TradeTime { get; set; }

	[JsonProperty("trade_timestamp")]
	[JsonConverter(typeof(JsonDateTimeMlsConverter))]
	public DateTime? TradeTimestamp { get; set; }

	[JsonProperty("acc_ask_volume")]
	public double? AccAskVolume { get; set; }

	[JsonProperty("acc_bid_volume")]
	public double? AccBidVolume { get; set; }

	[JsonProperty("highest_52_week_price")]
	public double? Highest52WeekPrice { get; set; }

	[JsonProperty("highest_52_week_date")]
	public string Highest52WeekDate { get; set; }

	[JsonProperty("lowest_52_week_price")]
	public double? Lowest52WeekPrice { get; set; }

	[JsonProperty("lowest_52_week_date")]
	public string Lowest52WeekDate { get; set; }

	[JsonProperty("trade_status")]
	public string TradeStatus { get; set; }

	[JsonProperty("market_state")]
	public string MarketState { get; set; }

	[JsonProperty("market_state_for_ios")]
	public string MarketStateForIos { get; set; }

	[JsonProperty("is_trading_suspended")]
	public bool IsTradingSuspended { get; set; }

	[JsonProperty("delisting_date")]
	public string DelistingDate { get; set; }

	[JsonProperty("market_warning")]
	public string MarketWarning { get; set; }

	[JsonProperty("acc_trade_price_24h")]
	public double? AccTradePrice24H { get; set; }

	[JsonProperty("acc_trade_volume_24h")]
	public double? AccTradeVolume24H { get; set; }
}