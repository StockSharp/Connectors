namespace StockSharp.THORChain.Native.Model;

[JsonConverter(typeof(StringEnumConverter))]
enum THORChainPoolStatuses
{
	[EnumMember(Value = "available")]
	Available,

	[EnumMember(Value = "staged")]
	Staged,

	[EnumMember(Value = "suspended")]
	Suspended,
}

[JsonConverter(typeof(StringEnumConverter))]
enum THORChainActionTypes
{
	[EnumMember(Value = "swap")]
	Swap,

	[EnumMember(Value = "refund")]
	Refund,
}

[JsonConverter(typeof(StringEnumConverter))]
enum THORChainActionStatuses
{
	[EnumMember(Value = "pending")]
	Pending,

	[EnumMember(Value = "success")]
	Success,

	[EnumMember(Value = "refund")]
	Refund,
}

[JsonConverter(typeof(StringEnumConverter))]
enum THORChainBroadcastModes
{
	[EnumMember(Value = "BROADCAST_MODE_SYNC")]
	Sync,
}

[JsonConverter(typeof(StringEnumConverter))]
enum THORChainAccountTypes
{
	[EnumMember(Value = "/cosmos.auth.v1beta1.BaseAccount")]
	BaseAccount,
}

[JsonConverter(typeof(StringEnumConverter))]
enum THORChainTransactionTypes
{
	[EnumMember(Value = "swap")]
	Swap,
}

sealed class THORChainPool
{
	[JsonProperty("asset")]
	public string Asset { get; set; }

	[JsonProperty("status")]
	public THORChainPoolStatuses Status { get; set; }

	[JsonProperty("assetDepth")]
	public string AssetDepth { get; set; }

	[JsonProperty("runeDepth")]
	public string RuneDepth { get; set; }

	[JsonProperty("assetPrice")]
	public string AssetPrice { get; set; }

	[JsonProperty("assetPriceUSD")]
	public string AssetPriceUsd { get; set; }

	[JsonProperty("liquidityInUSD")]
	public string LiquidityUsd { get; set; }

	[JsonProperty("volume24h")]
	public string Volume24Hours { get; set; }

	[JsonProperty("nativeDecimal")]
	public string NativeDecimals { get; set; }
}

sealed class THORChainMarket
{
	public string Asset { get; init; }
	public string Chain { get; init; }
	public string Symbol { get; init; }
	public string Ticker { get; init; }
	public string SecurityCode { get; init; }
	public THORChainPool Pool { get; set; }
}

sealed class THORChainMarketDefinition
{
	public string Asset { get; init; }
	public string SecurityCode { get; init; }
}

sealed class THORChainTrade
{
	public string Id { get; init; }
	public string TransactionHash { get; init; }
	public DateTime Time { get; init; }
	public decimal Price { get; init; }
	public decimal Volume { get; init; }
	public decimal QuoteVolume { get; init; }
	public Sides Side { get; init; }
}

sealed class THORChainCandle
{
	public DateTime OpenTime { get; init; }
	public decimal Open { get; init; }
	public decimal High { get; init; }
	public decimal Low { get; init; }
	public decimal Close { get; init; }
	public decimal Volume { get; init; }
	public decimal Turnover { get; init; }
	public int TradeCount { get; init; }
}

sealed class THORChainApiException : InvalidOperationException
{
	public THORChainApiException(HttpStatusCode statusCode, int? code,
		string message)
		: base(message)
	{
		StatusCode = statusCode;
		Code = code;
	}

	public THORChainApiException(string message, Exception innerException)
		: base(message, innerException)
	{
	}

	public HttpStatusCode StatusCode { get; }
	public int? Code { get; }
}
