namespace StockSharp.Drift.Native.Model;

sealed class DriftDataSubscribeRequest
{
	[JsonProperty("type")]
	public string Type { get; init; } = "subscribe";

	[JsonProperty("channelType")]
	public DriftDataChannels ChannelType { get; init; }

	[JsonProperty("symbol", NullValueHandling = NullValueHandling.Ignore)]
	public string Symbol { get; init; }

	[JsonProperty("resolution", NullValueHandling = NullValueHandling.Ignore)]
	public string Resolution { get; init; }
}

sealed class DriftDataSocketHeader
{
	[JsonProperty("type")]
	public string Type { get; init; }

	[JsonProperty("channelType")]
	public string ChannelType { get; init; }

	[JsonProperty("channel")]
	public string Channel { get; init; }

	[JsonProperty("message")]
	public string Message { get; init; }
}

sealed class DriftMarketsSocketMessage
{
	[JsonProperty("type")]
	public string Type { get; init; }

	[JsonProperty("channelType")]
	public string ChannelType { get; init; }

	[JsonProperty("data")]
	public DriftMarket[] Data { get; init; }
}

sealed class DriftCandleSocketMessage
{
	[JsonProperty("type")]
	public string Type { get; init; }

	[JsonProperty("channelType")]
	public string ChannelType { get; init; }

	[JsonProperty("symbol")]
	public string Symbol { get; init; }

	[JsonProperty("resolution")]
	public string Resolution { get; init; }

	[JsonProperty("candle")]
	public DriftCandle Candle { get; init; }
}

sealed class DriftDlobSubscribeRequest
{
	[JsonProperty("type")]
	public string Type { get; init; }

	[JsonProperty("market")]
	public string Market { get; init; }

	[JsonProperty("marketType")]
	public DriftMarketTypes MarketType { get; init; }

	[JsonProperty("channel")]
	public DriftDlobChannels Channel { get; init; }

	[JsonProperty("grouping", NullValueHandling = NullValueHandling.Ignore)]
	public string Grouping { get; init; }
}

sealed class DriftDlobSocketEnvelope
{
	[JsonProperty("channel")]
	public string Channel { get; init; }

	[JsonProperty("data")]
	public string Data { get; init; }

	[JsonProperty("message")]
	public string Message { get; init; }

	[JsonProperty("error")]
	public string Error { get; init; }
}
