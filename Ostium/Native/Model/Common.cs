namespace StockSharp.Ostium.Native.Model;

/// <summary>Ostium environments.</summary>
[DataContract]
public enum OstiumEnvironments
{
	/// <summary>Arbitrum One.</summary>
	[EnumMember]
	[Display(Name = "Mainnet")]
	Mainnet,

	/// <summary>Arbitrum Sepolia.</summary>
	[EnumMember]
	[Display(Name = "Testnet")]
	Testnet,
}

enum OstiumOpenOrderTypes
{
	Market,
	Limit,
	Stop,
}

sealed class OstiumNetwork
{
	public long ChainId { get; init; }
	public string Name { get; init; }
	public string RpcEndpoint { get; init; }
	public string SubgraphEndpoint { get; init; }
	public string UsdcAddress { get; init; }
	public string TradingAddress { get; init; }
	public string TradingStorageAddress { get; init; }
}

sealed class OstiumMarket
{
	public int PairIndex { get; init; }
	public string RawFrom { get; init; }
	public string RawTo { get; init; }
	public string BaseAsset { get; init; }
	public string QuoteAsset { get; init; }
	public string Symbol { get; init; }
	public string ApiPair { get; init; }
	public string PricePair { get; init; }
	public string Category { get; init; }
	public decimal MaximumLeverage { get; init; }
	public decimal OvernightMaximumLeverage { get; init; }
	public decimal TakerFeePercent { get; init; }
	public decimal MaximumOpenInterest { get; init; }
	public decimal LongOpenInterest { get; init; }
	public decimal ShortOpenInterest { get; init; }
	public decimal PriceStep { get; init; }
	public decimal VolumeStep { get; init; }
}

sealed class OstiumPrice
{
	[JsonProperty("feed_id")]
	public string FeedId { get; init; }

	[JsonProperty("pair")]
	public string Pair { get; init; }

	[JsonProperty("from")]
	public string From { get; init; }

	[JsonProperty("to")]
	public string To { get; init; }

	[JsonProperty("bid")]
	public decimal Bid { get; init; }

	[JsonProperty("mid")]
	public decimal Mid { get; init; }

	[JsonProperty("ask")]
	public decimal Ask { get; init; }

	[JsonProperty("isMarketOpen")]
	public bool IsMarketOpen { get; init; }

	[JsonProperty("isDayTradingClosed")]
	public bool IsDayTradingClosed { get; init; }

	[JsonProperty("secondsToToggleIsDayTradingClosed")]
	public int SecondsToToggleIsDayTradingClosed { get; init; }

	[JsonProperty("timestampSeconds")]
	public long TimestampSeconds { get; init; }

	[JsonProperty("schedule")]
	public OstiumSchedule Schedule { get; init; }
}

sealed class OstiumSchedule
{
	[JsonProperty("id")]
	public int Id { get; init; }

	[JsonProperty("alwaysOpen")]
	public bool IsAlwaysOpen { get; init; }

	[JsonProperty("timezone")]
	public string TimeZone { get; init; }

	[JsonProperty("openingHours")]
	public string[] OpeningHours { get; init; }

	[JsonProperty("dayTradingHours")]
	public OstiumDayTradingHours DayTradingHours { get; init; }
}

sealed class OstiumDayTradingHours
{
	[JsonProperty("from")]
	public string From { get; init; }

	[JsonProperty("to")]
	public string To { get; init; }
}

sealed class OstiumTransaction
{
	public string To { get; init; }
	public string Data { get; init; }
	public BigInteger Value { get; init; }
}

sealed class OstiumLimitEvent
{
	public int PairIndex { get; init; }
	public int PositionIndex { get; init; }
	public DateTime Time { get; init; }
}

sealed class OstiumMarketEvent
{
	public int PairIndex { get; init; }
	public string OrderId { get; init; }
	public DateTime Time { get; init; }
}
