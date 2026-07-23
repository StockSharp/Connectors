namespace StockSharp.MetaApi.Native.Model;

abstract class MetaApiRequest
{
	public string Type { get; init; }
	public string RequestId { get; set; }
	public string AccountId { get; set; }
	public string Application { get; set; }
	public int? InstanceIndex { get; set; }
}

sealed class MetaApiSubscribeRequest : MetaApiRequest
{
	public MetaApiSubscribeRequest()
	{
		Type = "subscribe";
	}

	public string SessionId { get; set; }
}

sealed class MetaApiSynchronizeRequest : MetaApiRequest
{
	public MetaApiSynchronizeRequest()
	{
		Type = "synchronize";
	}

	public int Version { get; set; } = 2;
	public DateTime StartingHistoryOrderTime { get; set; }
	public DateTime StartingDealTime { get; set; }
	public string Host { get; set; }
}

sealed class MetaApiMarketDataRequest : MetaApiRequest
{
	public MetaApiMarketDataRequest(string type)
	{
		Type = type.ThrowIfEmpty(nameof(type));
	}

	public string Symbol { get; set; }
	public MetaApiMarketDataSubscription[] Subscriptions { get; set; }
}

sealed class MetaApiRefreshMarketDataRequest : MetaApiRequest
{
	public MetaApiRefreshMarketDataRequest()
	{
		Type = "refreshMarketDataSubscriptions";
	}

	public MetaApiMarketDataRefreshItem[] Subscriptions { get; set; }
}

sealed class MetaApiMarketDataRefreshItem
{
	public string Symbol { get; set; }
	public MetaApiMarketDataSubscription[] Subscriptions { get; set; }
}

sealed class MetaApiResponse
{
	public string RequestId { get; set; }
}

sealed class MetaApiProcessingError
{
	public string RequestId { get; set; }
	public string Error { get; set; }
	public string Message { get; set; }
	public MetaApiRateLimitMetadata Metadata { get; set; }
}

sealed class MetaApiSynchronizationPacket
{
	public string Type { get; set; }
	public string AccountId { get; set; }
	public int? InstanceIndex { get; set; }
	public string Host { get; set; }
	public string SessionId { get; set; }
	public string SynchronizationId { get; set; }
	public long? SequenceNumber { get; set; }
	public long? SequenceTimestamp { get; set; }
	public bool? Connected { get; set; }

	public MetaApiAccountInformation AccountInformation { get; set; }
	public MetaApiSymbolSpecification[] Specifications { get; set; }
	public string[] RemovedSymbols { get; set; }
	public MetaApiOrder[] Orders { get; set; }
	public MetaApiPosition[] Positions { get; set; }
	public MetaApiDeal[] Deals { get; set; }

	public MetaApiSymbolPrice[] Prices { get; set; }
	public MetaApiTick[] Ticks { get; set; }
	public MetaApiBook[] Books { get; set; }
	public MetaApiCandle[] Candles { get; set; }
	public decimal? Equity { get; set; }
	public decimal? Margin { get; set; }
	public decimal? FreeMargin { get; set; }
	public decimal? MarginLevel { get; set; }

	public MetaApiPosition[] UpdatedPositions { get; set; }
	public string[] RemovedPositionIds { get; set; }
	public MetaApiOrder[] UpdatedOrders { get; set; }
	public MetaApiOrder[] HistoryOrders { get; set; }
	public string[] CompletedOrderIds { get; set; }
}

sealed class MetaApiSocketEvent
{
	public MetaApiSocketEvent(string name)
	{
		Name = name.ThrowIfEmpty(nameof(name));
	}

	public string Name { get; }
	public MetaApiResponse Response { get; set; }
	public MetaApiProcessingError ProcessingError { get; set; }
	public MetaApiSynchronizationPacket Synchronization { get; set; }
}
