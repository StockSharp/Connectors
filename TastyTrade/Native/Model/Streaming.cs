namespace StockSharp.TastyTrade.Native.Model;

[JsonConverter(typeof(StringEnumConverter))]
enum TastyAccountActions
{
	[EnumMember(Value = "connect")]
	Connect,
	[EnumMember(Value = "heartbeat")]
	Heartbeat,
}

[JsonConverter(typeof(StringEnumConverter))]
enum TastyAccountEventTypes
{
	[EnumMember(Value = "Order")]
	Order,
	[EnumMember(Value = "CurrentPosition")]
	CurrentPosition,
	[EnumMember(Value = "AccountBalance")]
	AccountBalance,
}

sealed class TastyAccountRequest
{
	[JsonProperty("action")]
	public TastyAccountActions Action { get; set; }

	[JsonProperty("value", NullValueHandling = NullValueHandling.Ignore)]
	public string[] Value { get; set; }

	[JsonProperty("auth-token")]
	public string AuthToken { get; set; }

	[JsonProperty("request-id")]
	public long RequestId { get; set; }

	[JsonProperty("source")]
	public string Source { get; set; }
}

sealed class TastyAccountHeader
{
	[JsonProperty("status")]
	public string Status { get; set; }

	[JsonProperty("type")]
	public TastyAccountEventTypes? Type { get; set; }

	[JsonProperty("message")]
	public string Message { get; set; }

	[JsonProperty("results")]
	public TastyAccountHeader[] Results { get; set; }
}

sealed class TastyAccountEvent<T>
{
	[JsonProperty("type")]
	public TastyAccountEventTypes Type { get; set; }

	[JsonProperty("data")]
	public T Data { get; set; }

	[JsonProperty("timestamp")]
	public long Timestamp { get; set; }
}

sealed class TastyAccountEvents<T>
{
	[JsonProperty("results")]
	public TastyAccountEvent<T>[] Results { get; set; }
}

[JsonConverter(typeof(StringEnumConverter))]
enum DxMessageTypes
{
	SETUP,
	AUTH,
	AUTH_STATE,
	CHANNEL_REQUEST,
	CHANNEL_OPENED,
	FEED_SETUP,
	FEED_CONFIG,
	FEED_SUBSCRIPTION,
	FEED_DATA,
	KEEPALIVE,
	ERROR,
}

[JsonConverter(typeof(StringEnumConverter))]
enum DxEventTypes
{
	Quote,
	Trade,
	Candle,
	Summary,
}

sealed class DxMessageHeader
{
	[JsonProperty("type")]
	public DxMessageTypes Type { get; set; }

	[JsonProperty("channel")]
	public int Channel { get; set; }

	[JsonProperty("state")]
	public string State { get; set; }

	[JsonProperty("error")]
	public string Error { get; set; }

	[JsonProperty("message")]
	public string Message { get; set; }
}

sealed class DxSetupRequest
{
	[JsonProperty("type")]
	public DxMessageTypes Type { get; set; }

	[JsonProperty("channel")]
	public int Channel { get; set; }

	[JsonProperty("version")]
	public string Version { get; set; }

	[JsonProperty("keepaliveTimeout")]
	public int KeepAliveTimeout { get; set; }

	[JsonProperty("acceptKeepaliveTimeout")]
	public int AcceptKeepAliveTimeout { get; set; }
}

sealed class DxAuthRequest
{
	[JsonProperty("type")]
	public DxMessageTypes Type { get; set; }

	[JsonProperty("channel")]
	public int Channel { get; set; }

	[JsonProperty("token")]
	public string Token { get; set; }
}

sealed class DxChannelRequest
{
	[JsonProperty("type")]
	public DxMessageTypes Type { get; set; }

	[JsonProperty("channel")]
	public int Channel { get; set; }

	[JsonProperty("service")]
	public string Service { get; set; }

	[JsonProperty("parameters")]
	public DxChannelParameters Parameters { get; set; }
}

sealed class DxChannelParameters
{
	[JsonProperty("contract")]
	public string Contract { get; set; }
}

sealed class DxFeedSetup
{
	[JsonProperty("type")]
	public DxMessageTypes Type { get; set; }

	[JsonProperty("channel")]
	public int Channel { get; set; }

	[JsonProperty("acceptAggregationPeriod")]
	public int AcceptAggregationPeriod { get; set; }

	[JsonProperty("acceptDataFormat")]
	public string AcceptDataFormat { get; set; }

	[JsonProperty("acceptEventFields")]
	public DxEventFields AcceptEventFields { get; set; }
}

sealed class DxEventFields
{
	[JsonProperty("Quote")]
	public string[] Quote { get; set; }

	[JsonProperty("Trade")]
	public string[] Trade { get; set; }

	[JsonProperty("Candle")]
	public string[] Candle { get; set; }

	[JsonProperty("Summary")]
	public string[] Summary { get; set; }
}

sealed class DxFeedSubscription
{
	[JsonProperty("type")]
	public DxMessageTypes Type { get; set; }

	[JsonProperty("channel")]
	public int Channel { get; set; }

	[JsonProperty("add", NullValueHandling = NullValueHandling.Ignore)]
	public DxSubscription[] Add { get; set; }

	[JsonProperty("remove", NullValueHandling = NullValueHandling.Ignore)]
	public DxSubscription[] Remove { get; set; }
}

sealed class DxSubscription
{
	[JsonProperty("type")]
	public DxEventTypes Type { get; set; }

	[JsonProperty("symbol")]
	public string Symbol { get; set; }

	[JsonProperty("fromTime", NullValueHandling = NullValueHandling.Ignore)]
	public long? FromTime { get; set; }
}

sealed class DxFeedData
{
	[JsonProperty("data")]
	public DxEvent[] Data { get; set; }
}

sealed class DxEvent
{
	[JsonProperty("eventType")]
	public DxEventTypes EventType { get; set; }

	[JsonProperty("eventSymbol")]
	public string EventSymbol { get; set; }

	[JsonProperty("eventFlags")]
	public int EventFlags { get; set; }

	[JsonProperty("time")]
	public long? Time { get; set; }

	[JsonProperty("bidTime")]
	public long? BidTime { get; set; }

	[JsonProperty("askTime")]
	public long? AskTime { get; set; }

	[JsonProperty("bidPrice")]
	public decimal? BidPrice { get; set; }

	[JsonProperty("askPrice")]
	public decimal? AskPrice { get; set; }

	[JsonProperty("bidSize")]
	public decimal? BidSize { get; set; }

	[JsonProperty("askSize")]
	public decimal? AskSize { get; set; }

	[JsonProperty("price")]
	public decimal? Price { get; set; }

	[JsonProperty("size")]
	public decimal? Size { get; set; }

	[JsonProperty("dayVolume")]
	public decimal? DayVolume { get; set; }

	[JsonProperty("openPrice")]
	public decimal? OpenPrice { get; set; }

	[JsonProperty("highPrice")]
	public decimal? HighPrice { get; set; }

	[JsonProperty("lowPrice")]
	public decimal? LowPrice { get; set; }

	[JsonProperty("prevDayClosePrice")]
	public decimal? PreviousClosePrice { get; set; }

	[JsonProperty("open")]
	public decimal? Open { get; set; }

	[JsonProperty("high")]
	public decimal? High { get; set; }

	[JsonProperty("low")]
	public decimal? Low { get; set; }

	[JsonProperty("close")]
	public decimal? Close { get; set; }

	[JsonProperty("volume")]
	public decimal? Volume { get; set; }

	[JsonProperty("openInterest")]
	public decimal? OpenInterest { get; set; }
}
