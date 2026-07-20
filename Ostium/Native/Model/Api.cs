namespace StockSharp.Ostium.Native.Model;

sealed class OstiumPricesResponse
{
	[JsonProperty("prices")]
	public OstiumPrice[] Prices { get; init; }

	[JsonProperty("stale")]
	public bool IsStale { get; init; }

	[JsonProperty("generatedAt")]
	public long GeneratedAt { get; init; }
}

sealed class OstiumOhlcRequest
{
	[JsonProperty("pair")]
	public string Pair { get; init; }

	[JsonProperty("fromTimestampSeconds")]
	public long FromTimestampSeconds { get; init; }

	[JsonProperty("toTimestampSeconds")]
	public long ToTimestampSeconds { get; init; }

	[JsonProperty("resolution")]
	public string Resolution { get; init; }
}

sealed class OstiumOhlcResponse
{
	[JsonProperty("data")]
	public OstiumCandle[] Data { get; init; }

	[JsonProperty("lastRequestTimestamp")]
	public long? LastRequestTimestamp { get; init; }
}

sealed class OstiumCandle
{
	[JsonProperty("from")]
	public string From { get; init; }

	[JsonProperty("to")]
	public string To { get; init; }

	[JsonProperty("time")]
	public long Time { get; init; }

	[JsonProperty("open")]
	public decimal Open { get; init; }

	[JsonProperty("high")]
	public decimal High { get; init; }

	[JsonProperty("low")]
	public decimal Low { get; init; }

	[JsonProperty("close")]
	public decimal Close { get; init; }
}

sealed class OstiumErrorResponse
{
	[JsonProperty("error")]
	public string Error { get; init; }
}

sealed class OstiumPriceSubscriptionRequest
{
	[JsonProperty("type")]
	public string Type { get; init; }

	[JsonProperty("pairs")]
	public string[] Pairs { get; init; }
}

sealed class OstiumPriceSocketHeader
{
	[JsonProperty("type")]
	public string Type { get; init; }
}

sealed class OstiumPriceSnapshot
{
	[JsonProperty("type")]
	public string Type { get; init; }

	[JsonProperty("data")]
	public OstiumPrice[] Data { get; init; }
}

sealed class OstiumPriceTick
{
	[JsonProperty("type")]
	public string Type { get; init; }

	[JsonProperty("data")]
	public OstiumPrice Data { get; init; }
}

sealed class OstiumPriceSocketError
{
	[JsonProperty("type")]
	public string Type { get; init; }

	[JsonProperty("message")]
	public string Message { get; init; }

	[JsonProperty("error")]
	public string Error { get; init; }
}
