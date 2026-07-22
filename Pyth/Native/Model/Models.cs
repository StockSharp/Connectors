namespace StockSharp.Pyth.Native.Model;

sealed class PythSymbol
{
	[JsonProperty("pyth_lazer_id")]
	public uint Id { get; set; }

	[JsonProperty("name")]
	public string Name { get; set; }

	[JsonProperty("symbol")]
	public string Symbol { get; set; }

	[JsonProperty("description")]
	public string Description { get; set; }

	[JsonProperty("asset_type")]
	public PythAssetTypes AssetType { get; set; }

	[JsonProperty("instrument_type")]
	public PythInstrumentTypes InstrumentType { get; set; }

	[JsonProperty("exponent")]
	public short Exponent { get; set; }

	[JsonProperty("min_publishers")]
	public ushort MinimumPublishers { get; set; }

	[JsonProperty("min_channel")]
	public PythChannels MinimumChannel { get; set; }

	[JsonProperty("state")]
	public PythSymbolStates State { get; set; }

	[JsonProperty("schedule")]
	public string Schedule { get; set; }

	[JsonProperty("hermes_id")]
	public string HermesId { get; set; }

	[JsonProperty("nasdaq_symbol")]
	public string NasdaqSymbol { get; set; }

	[JsonProperty("quote_currency")]
	public string QuoteCurrency { get; set; }

	[JsonProperty("symbol_chain_id")]
	public string SymbolChainId { get; set; }

	[JsonProperty("expiration_time")]
	public string ExpirationTime { get; set; }

	[JsonProperty("asset_sector")]
	public string AssetSector { get; set; }

	[JsonProperty("asset_subclass")]
	public string AssetSubclass { get; set; }

	[JsonProperty("groups")]
	public string[] Groups { get; set; }

	public string Key => Id.ToString(CultureInfo.InvariantCulture);
}

abstract class PythSubscriptionParameters
{
	[JsonProperty("priceFeedIds")]
	public uint[] PriceFeedIds { get; init; }

	[JsonProperty("properties")]
	public PythProperties[] Properties { get; init; }

	[JsonProperty("formats")]
	public PythFormats[] Formats { get; init; }

	[JsonProperty("channel")]
	public PythChannels Channel { get; init; }

	[JsonProperty("parsed")]
	public bool IsParsed { get; init; }
}

sealed class PythLatestPriceRequest : PythSubscriptionParameters
{
}

sealed class PythSubscribeRequest : PythSubscriptionParameters
{
	[JsonProperty("type")]
	public PythMessageTypes Type { get; init; } = PythMessageTypes.Subscribe;

	[JsonProperty("subscriptionId")]
	public long SubscriptionId { get; init; }

	[JsonProperty("deliveryFormat")]
	public PythDeliveryFormats DeliveryFormat { get; init; } =
		PythDeliveryFormats.Json;

	[JsonProperty("ignoreInvalidFeeds")]
	public bool IsIgnoreInvalidFeeds { get; init; }
}

sealed class PythUnsubscribeRequest
{
	[JsonProperty("type")]
	public PythMessageTypes Type { get; init; } = PythMessageTypes.Unsubscribe;

	[JsonProperty("subscriptionId")]
	public long SubscriptionId { get; init; }
}

class PythUpdate
{
	[JsonProperty("parsed")]
	public PythParsedPayload Parsed { get; set; }
}

sealed class PythSocketMessage : PythUpdate
{
	[JsonProperty("type")]
	public PythMessageTypes Type { get; set; }

	[JsonProperty("subscriptionId")]
	public long? SubscriptionId { get; set; }

	[JsonProperty("error")]
	public string Error { get; set; }

	[JsonProperty("subscribedFeedIds")]
	public uint[] SubscribedFeedIds { get; set; }
}

sealed class PythParsedPayload
{
	[JsonProperty("timestampUs")]
	public string TimestampMicroseconds { get; set; }

	[JsonProperty("priceFeeds")]
	public PythParsedFeed[] PriceFeeds { get; set; }
}

sealed class PythParsedFeed
{
	[JsonProperty("priceFeedId")]
	public uint PriceFeedId { get; set; }

	[JsonProperty("price")]
	public string Price { get; set; }

	[JsonProperty("bestBidPrice")]
	public string BestBidPrice { get; set; }

	[JsonProperty("bestAskPrice")]
	public string BestAskPrice { get; set; }

	[JsonProperty("publisherCount")]
	public ushort? PublisherCount { get; set; }

	[JsonProperty("exponent")]
	public short? Exponent { get; set; }

	[JsonProperty("marketSession")]
	public PythMarketSessions MarketSession { get; set; }

	[JsonProperty("feedUpdateTimestamp")]
	public long? FeedUpdateTimestamp { get; set; }
}

sealed class PythHistoryResponse
{
	[JsonProperty("s")]
	public PythHistoryStatuses Status { get; set; }

	[JsonProperty("errmsg")]
	public string ErrorMessage { get; set; }

	[JsonProperty("t")]
	public long[] Times { get; set; }

	[JsonProperty("o")]
	public decimal[] Opens { get; set; }

	[JsonProperty("h")]
	public decimal[] Highs { get; set; }

	[JsonProperty("l")]
	public decimal[] Lows { get; set; }

	[JsonProperty("c")]
	public decimal[] Closes { get; set; }

	[JsonProperty("v")]
	public long[] Volumes { get; set; }
}

sealed class PythHistoryCandle
{
	public DateTime OpenTime { get; init; }
	public decimal OpenPrice { get; init; }
	public decimal HighPrice { get; init; }
	public decimal LowPrice { get; init; }
	public decimal ClosePrice { get; init; }
	public decimal Volume { get; init; }
}

sealed class PythErrorResponse
{
	[JsonProperty("error")]
	public string Error { get; set; }

	[JsonProperty("message")]
	public string Message { get; set; }

	[JsonProperty("detail")]
	public string Detail { get; set; }
}
