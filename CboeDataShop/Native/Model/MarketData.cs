namespace StockSharp.CboeDataShop.Native.Model;

[JsonConverter(typeof(StringEnumConverter))]
enum CboeOptionTypes
{
	[EnumMember(Value = "C")]
	Call,

	[EnumMember(Value = "P")]
	Put,
}

[JsonConverter(typeof(StringEnumConverter))]
enum CboeUnderlyingIndicators
{
	[EnumMember(Value = "green")]
	Green,

	[EnumMember(Value = "yellow")]
	Yellow,

	[EnumMember(Value = "red")]
	Red,
}

class CboeUnderlyingSnapshot
{
	[JsonProperty("timestamp")]
	public string Timestamp { get; set; }

	[JsonProperty("symbol")]
	public string Symbol { get; set; }

	[JsonProperty("implied_underlying_bid")]
	public decimal? ImpliedUnderlyingBid { get; set; }

	[JsonProperty("implied_underlying_ask")]
	public decimal? ImpliedUnderlyingAsk { get; set; }

	[JsonProperty("implied_underlying_bid_size")]
	public long? ImpliedUnderlyingBidSize { get; set; }

	[JsonProperty("implied_underlying_ask_size")]
	public long? ImpliedUnderlyingAskSize { get; set; }

	[JsonProperty("implied_underlying_mid")]
	public decimal? ImpliedUnderlyingMid { get; set; }

	[JsonProperty("implied_underlying_indicator")]
	public CboeUnderlyingIndicators? ImpliedUnderlyingIndicator { get; set; }

	[JsonProperty("underlying_mid")]
	public decimal? UnderlyingMid { get; set; }

	[JsonProperty("underlying_last_trade_price")]
	public decimal? UnderlyingLastTradePrice { get; set; }

	[JsonProperty("underlying_last_trade_size")]
	public long? UnderlyingLastTradeSize { get; set; }

	[JsonProperty("underlying_bid")]
	public decimal? UnderlyingBid { get; set; }

	[JsonProperty("underlying_ask")]
	public decimal? UnderlyingAsk { get; set; }

	[JsonProperty("underlying_bid_size")]
	public long? UnderlyingBidSize { get; set; }

	[JsonProperty("underlying_ask_size")]
	public long? UnderlyingAskSize { get; set; }

	[JsonProperty("underlying_open")]
	public decimal? UnderlyingOpen { get; set; }

	[JsonProperty("underlying_high")]
	public decimal? UnderlyingHigh { get; set; }

	[JsonProperty("underlying_low")]
	public decimal? UnderlyingLow { get; set; }

	[JsonProperty("underlying_close")]
	public decimal? UnderlyingClose { get; set; }

	[JsonProperty("underlying_prev_day_close")]
	public decimal? UnderlyingPreviousClose { get; set; }

	[JsonProperty("underlying_volume")]
	public long? UnderlyingVolume { get; set; }

	[JsonProperty("iv30")]
	public decimal? Iv30 { get; set; }

	[JsonProperty("iv30_change")]
	public decimal? Iv30Change { get; set; }

	[JsonProperty("iv30_change_percent")]
	public decimal? Iv30ChangePercent { get; set; }

	[JsonProperty("seq_no")]
	public long? SequenceNumber { get; set; }
}

sealed class CboeOptionQuoteResponse : CboeUnderlyingSnapshot
{
	[JsonProperty("options")]
	public CboeOptionQuote[] Options { get; set; }
}

sealed class CboeOptionQuote
{
	[JsonProperty("root")]
	public string Root { get; set; }

	[JsonProperty("expiry")]
	public string Expiry { get; set; }

	[JsonProperty("strike")]
	public decimal? Strike { get; set; }

	[JsonProperty("option_type")]
	public CboeOptionTypes? OptionType { get; set; }

	[JsonProperty("timestamp")]
	public string Timestamp { get; set; }

	[JsonProperty("option")]
	public string Option { get; set; }

	[JsonProperty("option_mid")]
	public decimal? OptionMid { get; set; }

	[JsonProperty("option_trade_count")]
	public long? OptionTradeCount { get; set; }

	[JsonProperty("option_bid")]
	public decimal? OptionBid { get; set; }

	[JsonProperty("option_bid_size")]
	public long? OptionBidSize { get; set; }

	[JsonProperty("option_ask")]
	public decimal? OptionAsk { get; set; }

	[JsonProperty("option_ask_size")]
	public long? OptionAskSize { get; set; }

	[JsonProperty("option_open")]
	public decimal? OptionOpen { get; set; }

	[JsonProperty("option_high")]
	public decimal? OptionHigh { get; set; }

	[JsonProperty("option_low")]
	public decimal? OptionLow { get; set; }

	[JsonProperty("option_close")]
	public decimal? OptionClose { get; set; }

	[JsonProperty("option_last_trade_price")]
	public decimal? OptionLastTradePrice { get; set; }

	[JsonProperty("cboe_theo")]
	public decimal? CboeTheoreticalPrice { get; set; }

	[JsonProperty("option_prev_day_close")]
	public decimal? OptionPreviousClose { get; set; }

	[JsonProperty("iv")]
	public decimal? ImpliedVolatility { get; set; }

	[JsonProperty("mid_iv")]
	public decimal? MidImpliedVolatility { get; set; }

	[JsonProperty("open_interest")]
	public long? OpenInterest { get; set; }

	[JsonProperty("option_volume")]
	public long? OptionVolume { get; set; }

	[JsonProperty("delta")]
	public decimal? Delta { get; set; }

	[JsonProperty("gamma")]
	public decimal? Gamma { get; set; }

	[JsonProperty("vega")]
	public decimal? Vega { get; set; }

	[JsonProperty("theta")]
	public decimal? Theta { get; set; }

	[JsonProperty("rho")]
	public decimal? Rho { get; set; }

	public bool HasOhlc => OptionOpen != null && OptionHigh != null &&
		OptionLow != null && OptionClose != null;
}
