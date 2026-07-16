namespace StockSharp.DxFeed.Native.Model;

internal static class DxLinkMessageTypes
{
	public const string Setup = "SETUP";
	public const string Auth = "AUTH";
	public const string AuthState = "AUTH_STATE";
	public const string KeepAlive = "KEEPALIVE";
	public const string Error = "ERROR";
	public const string ChannelRequest = "CHANNEL_REQUEST";
	public const string ChannelOpened = "CHANNEL_OPENED";
	public const string ChannelClosed = "CHANNEL_CLOSED";
	public const string ChannelCancel = "CHANNEL_CANCEL";
	public const string FeedSetup = "FEED_SETUP";
	public const string FeedConfig = "FEED_CONFIG";
	public const string FeedSubscription = "FEED_SUBSCRIPTION";
	public const string FeedData = "FEED_DATA";
	public const string DomSetup = "DOM_SETUP";
	public const string DomConfig = "DOM_CONFIG";
	public const string DomSnapshot = "DOM_SNAPSHOT";
}

internal static class DxFeedEventTypes
{
	public const string Quote = "Quote";
	public const string Profile = "Profile";
	public const string Trade = "Trade";
	public const string TradeEth = "TradeETH";
	public const string Candle = "Candle";
	public const string Summary = "Summary";
	public const string TimeAndSale = "TimeAndSale";
	public const string Greeks = "Greeks";
	public const string TheoPrice = "TheoPrice";
	public const string Underlying = "Underlying";
	public const string OptionSale = "OptionSale";
	public const string Series = "Series";
	public const string Order = "Order";
	public const string SpreadOrder = "SpreadOrder";
	public const string AnalyticOrder = "AnalyticOrder";
	public const string Configuration = "Configuration";
	public const string Message = "Message";
}

internal static class DxIndexedEventFlags
{
	public const int TransactionPending = 1 << 0;
	public const int RemoveEvent = 1 << 1;
	public const int SnapshotBegin = 1 << 2;
	public const int SnapshotEnd = 1 << 3;
	public const int SnapshotSnip = 1 << 4;

	public static bool IsRemoved(this int flags)
		=> (flags & RemoveEvent) != 0;

	public static bool IsSnapshotComplete(this int flags)
		=> (flags & (SnapshotEnd | SnapshotSnip)) != 0;
}

internal readonly record struct DxFeedSubscriptionKey(string EventType, string Symbol, string Source);

internal sealed class DxLinkHeader
{
	[JsonProperty("type")]
	public string Type { get; set; }

	[JsonProperty("channel")]
	public int Channel { get; set; }

	[JsonProperty("service")]
	public string Service { get; set; }

	[JsonProperty("state")]
	public string State { get; set; }

	[JsonProperty("error")]
	public string Error { get; set; }

	[JsonProperty("message")]
	public string Message { get; set; }

	[JsonProperty("keepaliveTimeout")]
	public double? KeepAliveTimeout { get; set; }
}

internal sealed class DxSetupMessage
{
	[JsonProperty("type")]
	public string Type { get; set; } = DxLinkMessageTypes.Setup;

	[JsonProperty("channel")]
	public int Channel { get; set; }

	[JsonProperty("version")]
	public string Version { get; set; }

	[JsonProperty("keepaliveTimeout")]
	public double KeepAliveTimeout { get; set; }

	[JsonProperty("acceptKeepaliveTimeout")]
	public double AcceptKeepAliveTimeout { get; set; }
}

internal sealed class DxAuthMessage
{
	[JsonProperty("type")]
	public string Type { get; set; } = DxLinkMessageTypes.Auth;

	[JsonProperty("channel")]
	public int Channel { get; set; }

	[JsonProperty("token")]
	public string Token { get; set; }
}

internal sealed class DxKeepAliveMessage
{
	[JsonProperty("type")]
	public string Type { get; set; } = DxLinkMessageTypes.KeepAlive;

	[JsonProperty("channel")]
	public int Channel { get; set; }
}

internal sealed class DxChannelRequest
{
	[JsonProperty("type")]
	public string Type { get; set; } = DxLinkMessageTypes.ChannelRequest;

	[JsonProperty("channel")]
	public int Channel { get; set; }

	[JsonProperty("service")]
	public string Service { get; set; }

	[JsonProperty("parameters")]
	public DxChannelParameters Parameters { get; set; }
}

internal sealed class DxChannelParameters
{
	[JsonProperty("contract", NullValueHandling = NullValueHandling.Ignore)]
	public string Contract { get; set; }

	[JsonProperty("symbol", NullValueHandling = NullValueHandling.Ignore)]
	public string Symbol { get; set; }

	[JsonProperty("sources", NullValueHandling = NullValueHandling.Ignore)]
	public string[] Sources { get; set; }
}

internal sealed class DxChannelCancel
{
	[JsonProperty("type")]
	public string Type { get; set; } = DxLinkMessageTypes.ChannelCancel;

	[JsonProperty("channel")]
	public int Channel { get; set; }
}

internal sealed class DxFeedSetup
{
	[JsonProperty("type")]
	public string Type { get; set; } = DxLinkMessageTypes.FeedSetup;

	[JsonProperty("channel")]
	public int Channel { get; set; }

	[JsonProperty("acceptAggregationPeriod")]
	public double AcceptAggregationPeriod { get; set; }

	[JsonProperty("acceptDataFormat")]
	public string AcceptDataFormat { get; set; } = "FULL";
}

internal sealed class DxFeedSubscriptionMessage
{
	[JsonProperty("type")]
	public string Type { get; set; } = DxLinkMessageTypes.FeedSubscription;

	[JsonProperty("channel")]
	public int Channel { get; set; }

	[JsonProperty("add", NullValueHandling = NullValueHandling.Ignore)]
	public DxFeedSubscription[] Add { get; set; }

	[JsonProperty("remove", NullValueHandling = NullValueHandling.Ignore)]
	public DxFeedSubscription[] Remove { get; set; }

	[JsonProperty("reset", NullValueHandling = NullValueHandling.Ignore)]
	public bool? IsReset { get; set; }
}

internal sealed class DxFeedSubscription
{
	[JsonProperty("type")]
	public string EventType { get; set; }

	[JsonProperty("symbol")]
	public string Symbol { get; set; }

	[JsonProperty("source", NullValueHandling = NullValueHandling.Ignore)]
	public string Source { get; set; }

	[JsonProperty("fromTime", NullValueHandling = NullValueHandling.Ignore)]
	public long? FromTime { get; set; }
}

internal sealed class DxFeedDataMessage
{
	[JsonProperty("data")]
	public DxFeedEvent[] Data { get; set; }
}

internal sealed class DxFeedConfigMessage
{
	[JsonProperty("dataFormat")]
	public string DataFormat { get; set; }
}

internal sealed class DxDomSetup
{
	[JsonProperty("type")]
	public string Type { get; set; } = DxLinkMessageTypes.DomSetup;

	[JsonProperty("channel")]
	public int Channel { get; set; }

	[JsonProperty("acceptAggregationPeriod")]
	public double AcceptAggregationPeriod { get; set; }

	[JsonProperty("acceptDepthLimit")]
	public int AcceptDepthLimit { get; set; }

	[JsonProperty("acceptOrderFields")]
	public string[] AcceptOrderFields { get; set; }

	[JsonProperty("acceptDataFormat")]
	public string AcceptDataFormat { get; set; } = "FULL";
}

internal sealed class DxDomSnapshot
{
	[JsonProperty("channel")]
	public int Channel { get; set; }

	[JsonProperty("time")]
	public long Time { get; set; }

	[JsonProperty("bids")]
	public DxDomLevel[] Bids { get; set; }

	[JsonProperty("asks")]
	public DxDomLevel[] Asks { get; set; }
}

internal sealed class DxDomConfigMessage
{
	[JsonProperty("dataFormat")]
	public string DataFormat { get; set; }

	[JsonProperty("depthLimit")]
	public int DepthLimit { get; set; }
}

internal sealed class DxDomLevel
{
	[JsonProperty("price")]
	[JsonConverter(typeof(DxJsonDoubleConverter))]
	public decimal? Price { get; set; }

	[JsonProperty("size")]
	[JsonConverter(typeof(DxJsonDoubleConverter))]
	public decimal? Size { get; set; }

	[JsonProperty("count")]
	public int? Count { get; set; }
}
