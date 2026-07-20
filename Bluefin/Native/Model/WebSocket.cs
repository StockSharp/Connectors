namespace StockSharp.Bluefin.Native.Model;

sealed class BluefinMarketSubscriptionMessage
{
	[JsonProperty("method")]
	public string Method { get; init; }

	[JsonProperty("dataStreams")]
	public BluefinMarketSubscription[] DataStreams { get; init; }
}

sealed class BluefinMarketSubscription
{
	[JsonProperty("symbol")]
	public string Symbol { get; init; }

	[JsonProperty("streams")]
	public string[] Streams { get; init; }
}

sealed class BluefinAccountSubscriptionMessage
{
	[JsonProperty("authToken")]
	public string AuthToken { get; init; }

	[JsonProperty("method")]
	public string Method { get; init; }

	[JsonProperty("dataStreams")]
	public string[] DataStreams { get; init; }
}

sealed class BluefinSocketHeader
{
	[JsonProperty("success")]
	public bool? IsSuccess { get; init; }

	[JsonProperty("message")]
	public string Message { get; init; }

	[JsonProperty("event")]
	public string Event { get; init; }

	[JsonProperty("reason")]
	public string Reason { get; init; }
}

sealed class BluefinMarketStreamMessage
{
	[JsonProperty("event")]
	public string Event { get; init; }

	[JsonProperty("payload")]
	public BluefinMarketStreamPayload Payload { get; init; }
}

sealed class BluefinMarketStreamPayload : BluefinTicker
{
	[JsonProperty("trades")]
	public BluefinTrade[] Trades { get; init; }

	[JsonProperty("startTime")]
	public long StartTime { get; init; }

	[JsonProperty("endTime")]
	public long EndTime { get; init; }

	[JsonProperty("interval")]
	public string Interval { get; init; }

	[JsonProperty("openPriceE9")]
	public string OpenPriceE9 { get; init; }

	[JsonProperty("closePriceE9")]
	public string ClosePriceE9 { get; init; }

	[JsonProperty("highPriceE9")]
	public string HighPriceE9 { get; init; }

	[JsonProperty("lowPriceE9")]
	public string LowPriceE9 { get; init; }

	[JsonProperty("volumeE9")]
	public string VolumeE9 { get; init; }

	[JsonProperty("quoteVolumeE9")]
	public string QuoteVolumeE9 { get; init; }

	[JsonProperty("numTrades")]
	public long NumberOfTrades { get; init; }

	[JsonProperty("bidsE9")]
	public string[][] BidsE9 { get; init; }

	[JsonProperty("asksE9")]
	public string[][] AsksE9 { get; init; }

	[JsonProperty("firstUpdateId")]
	public long FirstUpdateId { get; init; }

	[JsonProperty("lastUpdateId")]
	public long LastUpdateId { get; init; }

	[JsonProperty("orderbookUpdateId")]
	public long OrderBookUpdateId { get; init; }

	[JsonProperty("depthLevel")]
	public string DepthLevel { get; init; }
}

sealed class BluefinAccountStreamMessage
{
	[JsonProperty("event")]
	public string Event { get; init; }

	[JsonProperty("reason")]
	public string Reason { get; init; }

	[JsonProperty("payload")]
	public BluefinAccountStreamPayload Payload { get; init; }
}

sealed class BluefinAccountStreamPayload : BluefinOrder
{
	[JsonProperty("trade")]
	public BluefinTrade Trade { get; init; }

	[JsonProperty("crossEffectiveBalanceE9")]
	public string CrossEffectiveBalanceE9 { get; init; }

	[JsonProperty("crossMarginRequiredE9")]
	public string CrossMarginRequiredE9 { get; init; }

	[JsonProperty("totalOrderMarginRequiredE9")]
	public string TotalOrderMarginRequiredE9 { get; init; }

	[JsonProperty("marginAvailableE9")]
	public string MarginAvailableE9 { get; init; }

	[JsonProperty("totalUnrealizedPnlE9")]
	public string TotalUnrealizedPnlE9 { get; init; }

	[JsonProperty("crossAccountValueE9")]
	public string CrossAccountValueE9 { get; init; }

	[JsonProperty("totalAccountValueE9")]
	public string TotalAccountValueE9 { get; init; }

	[JsonProperty("assets")]
	public BluefinAccountAsset[] Assets { get; init; }

	[JsonProperty("avgEntryPriceE9")]
	public string AverageEntryPriceE9 { get; init; }

	[JsonProperty("clientSetLeverageE9")]
	public string ClientSetLeverageE9 { get; init; }

	[JsonProperty("liquidationPriceE9")]
	public string LiquidationPriceE9 { get; init; }

	[JsonProperty("markPriceE9")]
	public string MarkPriceE9 { get; init; }

	[JsonProperty("notionalValueE9")]
	public string NotionalValueE9 { get; init; }

	[JsonProperty("sizeE9")]
	public string SizeE9 { get; init; }

	[JsonProperty("unrealizedPnlE9")]
	public string UnrealizedPnlE9 { get; init; }

	[JsonProperty("marginRequiredE9")]
	public string MarginRequiredE9 { get; init; }

	[JsonProperty("maintenanceMarginE9")]
	public string MaintenanceMarginE9 { get; init; }

	[JsonProperty("isolatedMarginE9")]
	public string IsolatedMarginE9 { get; init; }

	[JsonProperty("reasonCode")]
	public string ReasonCode { get; init; }

	[JsonProperty("reason")]
	public string Reason { get; init; }

	[JsonProperty("failedCommandType")]
	public string FailedCommandType { get; init; }

	[JsonProperty("failedAtMillis")]
	public long FailedAtMillis { get; init; }
}
