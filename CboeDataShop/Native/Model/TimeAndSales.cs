namespace StockSharp.CboeDataShop.Native.Model;

[JsonConverter(typeof(StringEnumConverter))]
enum CboeTradeLocations
{
	[EnumMember(Value = "Below Bid")]
	BelowBid,

	[EnumMember(Value = "On Bid")]
	OnBid,

	[EnumMember(Value = "Mid Market")]
	MidMarket,

	[EnumMember(Value = "On Ask")]
	OnAsk,

	[EnumMember(Value = "Above Ask")]
	AboveAsk,

	[EnumMember(Value = "Crossed Market")]
	CrossedMarket,

	[EnumMember(Value = "No Market")]
	NoMarket,
}

enum CboeCancelStates
{
	None,
	CanceledTrade,
	CancellationMessage,
}

abstract class CboeTrade
{
	[JsonProperty("security")]
	public string Security { get; set; }

	[JsonProperty("condition_id")]
	public byte? ConditionId { get; set; }

	[JsonProperty("exchange_id")]
	public byte? ExchangeId { get; set; }

	[JsonProperty("exchange_seq_no")]
	public long? ExchangeSequenceNumber { get; set; }

	[JsonProperty("cancel_flag")]
	public CboeCancelStates CancelState { get; set; }

	[JsonProperty("timestamp")]
	public string Timestamp { get; set; }

	[JsonProperty("seq_no")]
	public long SequenceNumber { get; set; }

	public abstract decimal? Price { get; }
	public abstract long? Size { get; }
	public abstract CboeTradeLocations? TradeLocation { get; }
}

sealed class CboeUnderlyingTrade : CboeTrade
{
	[JsonProperty("underlying_ask")]
	public decimal? UnderlyingAsk { get; set; }

	[JsonProperty("underlying_ask_size")]
	public long? UnderlyingAskSize { get; set; }

	[JsonProperty("underlying_bid")]
	public decimal? UnderlyingBid { get; set; }

	[JsonProperty("underlying_bid_size")]
	public long? UnderlyingBidSize { get; set; }

	[JsonProperty("underlying_trade_price")]
	public decimal? UnderlyingTradePrice { get; set; }

	[JsonProperty("underlying_trade_size")]
	public long? UnderlyingTradeSize { get; set; }

	[JsonProperty("underlying_trade_at")]
	public CboeTradeLocations? UnderlyingTradeLocation { get; set; }

	public override decimal? Price => UnderlyingTradePrice;
	public override long? Size => UnderlyingTradeSize;
	public override CboeTradeLocations? TradeLocation => UnderlyingTradeLocation;
}

sealed class CboeOptionTrade : CboeTrade
{
	[JsonProperty("root")]
	public string Root { get; set; }

	[JsonProperty("expiry")]
	public string Expiry { get; set; }

	[JsonProperty("strike")]
	public decimal? Strike { get; set; }

	[JsonProperty("option_type")]
	public CboeOptionTypes? OptionType { get; set; }

	[JsonProperty("option_ask")]
	public decimal? OptionAsk { get; set; }

	[JsonProperty("option_ask_size")]
	public long? OptionAskSize { get; set; }

	[JsonProperty("option_bid")]
	public decimal? OptionBid { get; set; }

	[JsonProperty("option_bid_size")]
	public long? OptionBidSize { get; set; }

	[JsonProperty("option_trade_price")]
	public decimal? OptionTradePrice { get; set; }

	[JsonProperty("option_trade_size")]
	public long? OptionTradeSize { get; set; }

	[JsonProperty("option_trade_at")]
	public CboeTradeLocations? OptionTradeLocation { get; set; }

	[JsonProperty("underlying_bid")]
	public decimal? UnderlyingBid { get; set; }

	[JsonProperty("underlying_ask")]
	public decimal? UnderlyingAsk { get; set; }

	public override decimal? Price => OptionTradePrice;
	public override long? Size => OptionTradeSize;
	public override CboeTradeLocations? TradeLocation => OptionTradeLocation;
}
