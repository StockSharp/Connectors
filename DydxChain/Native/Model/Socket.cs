namespace StockSharp.DydxChain.Native.Model;

[JsonConverter(typeof(StringEnumConverter))]
enum DydxChainSocketRequestTypes
{
	[EnumMember(Value = "subscribe")]
	Subscribe,

	[EnumMember(Value = "unsubscribe")]
	Unsubscribe,

	[EnumMember(Value = "ping")]
	Ping,
}

[JsonConverter(typeof(StringEnumConverter))]
enum DydxChainSocketMessageTypes
{
	[EnumMember(Value = "connected")]
	Connected,

	[EnumMember(Value = "subscribed")]
	Subscribed,

	[EnumMember(Value = "unsubscribed")]
	Unsubscribed,

	[EnumMember(Value = "error")]
	Error,

	[EnumMember(Value = "channel_data")]
	ChannelData,

	[EnumMember(Value = "channel_batch_data")]
	ChannelBatchData,

	[EnumMember(Value = "pong")]
	Pong,
}

[JsonConverter(typeof(StringEnumConverter))]
enum DydxChainSocketChannels
{
	[EnumMember(Value = "v4_subaccounts")]
	Subaccounts,

	[EnumMember(Value = "v4_orderbook")]
	Orderbook,

	[EnumMember(Value = "v4_trades")]
	Trades,

	[EnumMember(Value = "v4_markets")]
	Markets,

	[EnumMember(Value = "v4_candles")]
	Candles,
}

readonly record struct DydxChainSocketSubscriptionKey(
	DydxChainSocketChannels Channel, string Id);

sealed class DydxChainSocketSubscriptionRequest
{
	[JsonProperty("type")]
	public DydxChainSocketRequestTypes Type { get; init; }

	[JsonProperty("channel")]
	public DydxChainSocketChannels Channel { get; init; }

	[JsonProperty("id")]
	public string Id { get; init; }

	[JsonProperty("batched")]
	public bool? IsBatched { get; init; }
}

sealed class DydxChainSocketPingRequest
{
	[JsonProperty("type")]
	public DydxChainSocketRequestTypes Type { get; init; } =
		DydxChainSocketRequestTypes.Ping;

	[JsonProperty("id")]
	public long Id { get; init; }
}

sealed class DydxChainSocketHeader
{
	[JsonProperty("type")]
	public DydxChainSocketMessageTypes Type { get; set; }

	[JsonProperty("connection_id")]
	public string ConnectionId { get; set; }

	[JsonProperty("message_id")]
	public long MessageId { get; set; }

	[JsonProperty("channel")]
	public DydxChainSocketChannels? Channel { get; set; }

	[JsonProperty("id")]
	public string Id { get; set; }

	[JsonProperty("message")]
	public string Message { get; set; }

	[JsonProperty("version")]
	public string Version { get; set; }
}

sealed class DydxChainSocketEnvelope<TContents>
	where TContents : class
{
	[JsonProperty("type")]
	public DydxChainSocketMessageTypes Type { get; set; }

	[JsonProperty("connection_id")]
	public string ConnectionId { get; set; }

	[JsonProperty("message_id")]
	public long MessageId { get; set; }

	[JsonProperty("channel")]
	public DydxChainSocketChannels Channel { get; set; }

	[JsonProperty("id")]
	public string Id { get; set; }

	[JsonProperty("version")]
	public string Version { get; set; }

	[JsonProperty("contents")]
	public TContents Contents { get; set; }
}

sealed class DydxChainSocketBatchEnvelope<TContents>
	where TContents : class
{
	[JsonProperty("type")]
	public DydxChainSocketMessageTypes Type { get; set; }

	[JsonProperty("connection_id")]
	public string ConnectionId { get; set; }

	[JsonProperty("message_id")]
	public long MessageId { get; set; }

	[JsonProperty("channel")]
	public DydxChainSocketChannels Channel { get; set; }

	[JsonProperty("id")]
	public string Id { get; set; }

	[JsonProperty("version")]
	public string Version { get; set; }

	[JsonProperty("contents")]
	public TContents[] Contents { get; set; }
}

sealed class DydxChainMarketUpdate
{
	[JsonProperty("trading")]
	[JsonConverter(typeof(DydxChainTradingMarketCollectionConverter))]
	public DydxChainTradingMarketUpdate[] Trading { get; set; }

	[JsonProperty("oraclePrices")]
	[JsonConverter(typeof(DydxChainOraclePriceCollectionConverter))]
	public DydxChainOraclePriceUpdate[] OraclePrices { get; set; }
}

sealed class DydxChainOrderbookUpdate
{
	[JsonProperty("bids")]
	public DydxChainPriceLevel[] Bids { get; set; }

	[JsonProperty("asks")]
	public DydxChainPriceLevel[] Asks { get; set; }
}

sealed class DydxChainSubaccountUpdate
{
	[JsonProperty("perpetualPositions")]
	public DydxChainPerpetualPosition[] PerpetualPositions { get; set; }

	[JsonProperty("assetPositions")]
	public DydxChainAssetPosition[] AssetPositions { get; set; }

	[JsonProperty("orders")]
	public DydxChainOrder[] Orders { get; set; }

	[JsonProperty("fills")]
	public DydxChainFill[] Fills { get; set; }

	[JsonProperty("blockHeight")]
	public string BlockHeight { get; set; }
}
