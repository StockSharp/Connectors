namespace StockSharp.CoinGecko;

/// <summary>CoinGecko API tiers.</summary>
[DataContract]
public enum CoinGeckoApiTiers
{
	/// <summary>Demo API with a free Demo key.</summary>
	[EnumMember]
	Demo,

	/// <summary>Pro API with a paid Pro key.</summary>
	[EnumMember]
	Pro,
}

/// <summary>CoinGecko security kinds.</summary>
[DataContract]
public enum CoinGeckoSecurityKinds
{
	/// <summary>Aggregated CoinGecko coin.</summary>
	[EnumMember]
	Coin,

	/// <summary>GeckoTerminal on-chain liquidity pool.</summary>
	[EnumMember]
	OnchainPool,
}

[JsonConverter(typeof(CoinGeckoEnumConverter<CoinGeckoSocketCommands>))]
enum CoinGeckoSocketCommands
{
	[EnumMember(Value = "subscribe")]
	Subscribe,

	[EnumMember(Value = "message")]
	Message,

	[EnumMember(Value = "unsubscribe")]
	Unsubscribe,
}

[JsonConverter(typeof(CoinGeckoEnumConverter<CoinGeckoSocketActions>))]
enum CoinGeckoSocketActions
{
	[EnumMember(Value = "set_tokens")]
	SetTokens,

	[EnumMember(Value = "unset_tokens")]
	UnsetTokens,

	[EnumMember(Value = "set_pools")]
	SetPools,

	[EnumMember(Value = "unset_pools")]
	UnsetPools,
}

[JsonConverter(typeof(CoinGeckoEnumConverter<CoinGeckoSocketChannels>))]
enum CoinGeckoSocketChannels
{
	[EnumMember(Value = "CGSimplePrice")]
	CoinPrice,

	[EnumMember(Value = "OnchainSimpleTokenPrice")]
	OnchainTokenPrice,

	[EnumMember(Value = "OnchainTrade")]
	OnchainTrade,

	[EnumMember(Value = "OnchainOHLCV")]
	OnchainOhlcv,
}

[JsonConverter(typeof(CoinGeckoEnumConverter<CoinGeckoSocketChannelCodes>))]
enum CoinGeckoSocketChannelCodes
{
	[EnumMember(Value = "")]
	Unknown,

	[EnumMember(Value = "C1")]
	CoinPrice,

	[EnumMember(Value = "G1")]
	OnchainTokenPrice,

	[EnumMember(Value = "G2")]
	OnchainTrade,

	[EnumMember(Value = "G3")]
	OnchainOhlcv,
}

[JsonConverter(typeof(CoinGeckoEnumConverter<CoinGeckoSocketMessageTypes>))]
enum CoinGeckoSocketMessageTypes
{
	[EnumMember(Value = "")]
	Unknown,

	[EnumMember(Value = "welcome")]
	Welcome,

	[EnumMember(Value = "confirm_subscription")]
	ConfirmSubscription,

	[EnumMember(Value = "reject_subscription")]
	RejectSubscription,

	[EnumMember(Value = "disconnect")]
	Disconnect,
}

[JsonConverter(typeof(CoinGeckoEnumConverter<CoinGeckoSocketIntervals>))]
enum CoinGeckoSocketIntervals
{
	[EnumMember(Value = "1s")]
	Second1,

	[EnumMember(Value = "1m")]
	Minute1,

	[EnumMember(Value = "5m")]
	Minute5,

	[EnumMember(Value = "15m")]
	Minute15,

	[EnumMember(Value = "1h")]
	Hour1,

	[EnumMember(Value = "2h")]
	Hour2,

	[EnumMember(Value = "4h")]
	Hour4,

	[EnumMember(Value = "8h")]
	Hour8,

	[EnumMember(Value = "12h")]
	Hour12,

	[EnumMember(Value = "1d")]
	Day1,

}

[JsonConverter(typeof(CoinGeckoEnumConverter<CoinGeckoOnchainTokens>))]
enum CoinGeckoOnchainTokens
{
	[EnumMember(Value = "base")]
	Base,

	[EnumMember(Value = "quote")]
	Quote,
}

[JsonConverter(typeof(CoinGeckoEnumConverter<CoinGeckoOhlcvTimeframes>))]
enum CoinGeckoOhlcvTimeframes
{
	[EnumMember(Value = "second")]
	Second,

	[EnumMember(Value = "minute")]
	Minute,

	[EnumMember(Value = "hour")]
	Hour,

	[EnumMember(Value = "day")]
	Day,
}

[JsonConverter(typeof(CoinGeckoEnumConverter<CoinGeckoCoinIntervals>))]
enum CoinGeckoCoinIntervals
{
	[EnumMember(Value = "hourly")]
	Hourly,

	[EnumMember(Value = "daily")]
	Daily,
}

[JsonConverter(typeof(CoinGeckoEnumConverter<CoinGeckoRecentDays>))]
enum CoinGeckoRecentDays
{
	[EnumMember(Value = "1")]
	Day1,

	[EnumMember(Value = "7")]
	Day7,

	[EnumMember(Value = "14")]
	Day14,

	[EnumMember(Value = "30")]
	Day30,

	[EnumMember(Value = "90")]
	Day90,

	[EnumMember(Value = "180")]
	Day180,

	[EnumMember(Value = "365")]
	Day365,
}

[JsonConverter(typeof(CoinGeckoEnumConverter<CoinGeckoTradeKinds>))]
enum CoinGeckoTradeKinds
{
	[EnumMember(Value = "")]
	Unknown,

	[EnumMember(Value = "buy")]
	Buy,

	[EnumMember(Value = "sell")]
	Sell,
}

[JsonConverter(typeof(CoinGeckoEnumConverter<CoinGeckoSocketTradeSides>))]
enum CoinGeckoSocketTradeSides
{
	[EnumMember(Value = "")]
	Unknown,

	[EnumMember(Value = "b")]
	Buy,

	[EnumMember(Value = "s")]
	Sell,
}

[JsonConverter(typeof(CoinGeckoEnumConverter<CoinGeckoResourceTypes>))]
enum CoinGeckoResourceTypes
{
	[EnumMember(Value = "")]
	Unknown,

	[EnumMember(Value = "pool")]
	Pool,

	[EnumMember(Value = "token")]
	Token,

	[EnumMember(Value = "dex")]
	Dex,

	[EnumMember(Value = "trade")]
	Trade,

	[EnumMember(Value = "ohlcv_request_response")]
	OhlcvResponse,
}

sealed class CoinGeckoEnumConverter<TEnum> : JsonConverter<TEnum>
	where TEnum : struct, Enum
{
	private static readonly Dictionary<string, TEnum> _fromWire =
		Enum.GetValues<TEnum>().ToDictionary(GetWireValue, static value => value,
			StringComparer.OrdinalIgnoreCase);

	private static string GetWireValue(TEnum value)
	{
		var member = typeof(TEnum).GetMember(value.ToString())[0];
		return member.GetCustomAttribute<EnumMemberAttribute>()?.Value ??
			value.ToString();
	}

	internal static string ToWire(TEnum value) => GetWireValue(value);

	public override TEnum ReadJson(JsonReader reader, Type objectType,
		TEnum existingValue, bool hasExistingValue, JsonSerializer serializer)
	{
		_ = objectType;
		_ = existingValue;
		_ = hasExistingValue;
		_ = serializer;
		var value = reader.Value?.ToString();
		if (!value.IsEmpty() && _fromWire.TryGetValue(value, out var result))
			return result;
		if (_fromWire.TryGetValue(string.Empty, out result))
			return result;
		throw new JsonSerializationException(
			$"Unknown CoinGecko {typeof(TEnum).Name} value '{value}'.");
	}

	public override void WriteJson(JsonWriter writer, TEnum value,
		JsonSerializer serializer)
	{
		_ = serializer;
		writer.WriteValue(GetWireValue(value));
	}
}
