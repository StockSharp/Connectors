namespace StockSharp.Drift.Native.Model;

[DataContract]
[JsonConverter(typeof(StringEnumConverter))]
enum DriftMarketTypes
{
	[EnumMember(Value = "spot")]
	Spot,

	[EnumMember(Value = "perp")]
	Perpetual,
}

[DataContract]
[JsonConverter(typeof(StringEnumConverter))]
enum DriftOrderDirections
{
	[EnumMember(Value = "long")]
	Long,

	[EnumMember(Value = "short")]
	Short,
}

[DataContract]
[JsonConverter(typeof(StringEnumConverter))]
enum DriftApiOrderTypes
{
	[EnumMember(Value = "market")]
	Market,

	[EnumMember(Value = "limit")]
	Limit,
}

/// <summary>Supported Drift margin modes.</summary>
[DataContract]
[JsonConverter(typeof(StringEnumConverter))]
public enum DriftMarginModes
{
	/// <summary>Cross margin.</summary>
	[EnumMember(Value = "cross")]
	[Display(Name = "Cross")]
	Cross,

	/// <summary>Isolated margin.</summary>
	[EnumMember(Value = "isolated")]
	[Display(Name = "Isolated")]
	Isolated,
}

[DataContract]
[JsonConverter(typeof(StringEnumConverter))]
enum DriftDlobChannels
{
	[EnumMember(Value = "orderbook")]
	OrderBook,

	[EnumMember(Value = "trades")]
	Trades,
}

[DataContract]
[JsonConverter(typeof(StringEnumConverter))]
enum DriftDataChannels
{
	[EnumMember(Value = "markets")]
	Markets,

	[EnumMember(Value = "candle")]
	Candle,
}

sealed class DriftApiError
{
	[JsonProperty("message")]
	public string Message { get; init; }

	[JsonProperty("error")]
	public string Error { get; init; }

	[JsonProperty("statusCode")]
	public int? StatusCode { get; init; }
}

sealed class DriftApiException : InvalidOperationException
{
	public DriftApiException(string message)
		: base(message)
	{
	}

	public DriftApiException(string message, Exception innerException)
		: base(message, innerException)
	{
	}
}

sealed class DriftPageMeta
{
	[JsonProperty("nextPage")]
	public string NextPage { get; init; }
}

sealed class DriftPagedResponse<T>
{
	[JsonProperty("success")]
	public bool IsSuccess { get; init; }

	[JsonProperty("records")]
	public T[] Records { get; init; }

	[JsonProperty("meta")]
	public DriftPageMeta Meta { get; init; }
}
