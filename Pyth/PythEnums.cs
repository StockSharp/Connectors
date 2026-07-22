namespace StockSharp.Pyth;

/// <summary>Pyth Pro price-delivery channels.</summary>
[JsonConverter(typeof(PythEnumConverter<PythChannels>))]
public enum PythChannels
{
	/// <summary>Unknown channel.</summary>
	Unknown,

	/// <summary>Updates are delivered as soon as they are available.</summary>
	[EnumMember(Value = "real_time")]
	RealTime,

	/// <summary>Updates are delivered every 50 milliseconds.</summary>
	[EnumMember(Value = "fixed_rate@50ms")]
	FixedRate50Milliseconds,

	/// <summary>Updates are delivered every 200 milliseconds.</summary>
	[EnumMember(Value = "fixed_rate@200ms")]
	FixedRate200Milliseconds,

	/// <summary>Updates are delivered every 1000 milliseconds.</summary>
	[EnumMember(Value = "fixed_rate@1000ms")]
	FixedRate1000Milliseconds,
}

[JsonConverter(typeof(PythEnumConverter<PythProperties>))]
enum PythProperties
{
	Unknown,

	[EnumMember(Value = "price")]
	Price,

	[EnumMember(Value = "bestBidPrice")]
	BestBidPrice,

	[EnumMember(Value = "bestAskPrice")]
	BestAskPrice,

	[EnumMember(Value = "exponent")]
	Exponent,

	[EnumMember(Value = "marketSession")]
	MarketSession,

	[EnumMember(Value = "feedUpdateTimestamp")]
	FeedUpdateTimestamp,
}

[JsonConverter(typeof(PythEnumConverter<PythFormats>))]
enum PythFormats
{
	Unknown,

	[EnumMember(Value = "evm")]
	Evm,

	[EnumMember(Value = "solana")]
	Solana,

	[EnumMember(Value = "leEcdsa")]
	LittleEndianEcdsa,

	[EnumMember(Value = "leUnsigned")]
	LittleEndianUnsigned,
}

[JsonConverter(typeof(PythEnumConverter<PythDeliveryFormats>))]
enum PythDeliveryFormats
{
	Unknown,

	[EnumMember(Value = "json")]
	Json,

	[EnumMember(Value = "binary")]
	Binary,
}

[JsonConverter(typeof(PythEnumConverter<PythMessageTypes>))]
enum PythMessageTypes
{
	Unknown,

	[EnumMember(Value = "subscribe")]
	Subscribe,

	[EnumMember(Value = "unsubscribe")]
	Unsubscribe,

	[EnumMember(Value = "subscribed")]
	Subscribed,

	[EnumMember(Value = "subscribedWithInvalidFeedIdsIgnored")]
	SubscribedWithInvalidFeedIdsIgnored,

	[EnumMember(Value = "unsubscribed")]
	Unsubscribed,

	[EnumMember(Value = "subscriptionError")]
	SubscriptionError,

	[EnumMember(Value = "streamUpdated")]
	StreamUpdated,

	[EnumMember(Value = "error")]
	Error,
}

[JsonConverter(typeof(PythEnumConverter<PythAssetTypes>))]
enum PythAssetTypes
{
	Unknown,

	[EnumMember(Value = "crypto")]
	Crypto,

	[EnumMember(Value = "equity")]
	Equity,

	[EnumMember(Value = "fx")]
	Fx,

	[EnumMember(Value = "commodity")]
	Commodity,

	[EnumMember(Value = "metal")]
	Metal,

	[EnumMember(Value = "rates")]
	Rates,

	[EnumMember(Value = "interest-rate")]
	InterestRate,

	[EnumMember(Value = "nav")]
	Nav,

	[EnumMember(Value = "kalshi")]
	Kalshi,

	[EnumMember(Value = "crypto-index")]
	CryptoIndex,

	[EnumMember(Value = "crypto-redemption-rate")]
	CryptoRedemptionRate,

	[EnumMember(Value = "funding-rate")]
	FundingRate,
}

[JsonConverter(typeof(PythEnumConverter<PythInstrumentTypes>))]
enum PythInstrumentTypes
{
	Unknown,

	[EnumMember(Value = "spot")]
	Spot,

	[EnumMember(Value = "rate")]
	Rate,

	[EnumMember(Value = "future")]
	Future,

	[EnumMember(Value = "index")]
	Index,

	[EnumMember(Value = "nav")]
	Nav,

	[EnumMember(Value = "perp")]
	Perpetual,
}

[JsonConverter(typeof(PythEnumConverter<PythSymbolStates>))]
enum PythSymbolStates
{
	Unknown,

	[EnumMember(Value = "stable")]
	Stable,

	[EnumMember(Value = "coming_soon")]
	ComingSoon,

	[EnumMember(Value = "inactive")]
	Inactive,
}

[JsonConverter(typeof(PythEnumConverter<PythMarketSessions>))]
enum PythMarketSessions
{
	Unknown,

	[EnumMember(Value = "regular")]
	Regular,

	[EnumMember(Value = "preMarket")]
	PreMarket,

	[EnumMember(Value = "postMarket")]
	PostMarket,

	[EnumMember(Value = "overNight")]
	OverNight,

	[EnumMember(Value = "closed")]
	Closed,
}

[JsonConverter(typeof(PythEnumConverter<PythHistoryStatuses>))]
enum PythHistoryStatuses
{
	Unknown,

	[EnumMember(Value = "ok")]
	Ok,

	[EnumMember(Value = "error")]
	Error,
}

sealed class PythEnumConverter<TEnum> : JsonConverter<TEnum>
	where TEnum : struct, Enum
{
	private static readonly (string Wire, TEnum Value)[] _values =
	[
		.. Enum.GetValues<TEnum>().Select(static value =>
			(GetWireValue(value), value)),
	];

	private static string GetWireValue(TEnum value)
	{
		var member = typeof(TEnum).GetMember(value.ToString())[0];
		return member.GetCustomAttribute<EnumMemberAttribute>()?.Value ??
			value.ToString();
	}

	internal static string ToWire(TEnum value)
	{
		if (EqualityComparer<TEnum>.Default.Equals(value, default))
			throw new ArgumentOutOfRangeException(nameof(value), value,
				$"Unknown Pyth {typeof(TEnum).Name} value cannot be serialized.");
		return GetWireValue(value);
	}

	public override TEnum ReadJson(JsonReader reader, Type objectType,
		TEnum existingValue, bool hasExistingValue, JsonSerializer serializer)
	{
		_ = objectType;
		_ = existingValue;
		_ = hasExistingValue;
		_ = serializer;
		var value = reader.Value?.ToString();
		return _values.FirstOrDefault(item =>
			item.Wire.EqualsIgnoreCase(value)).Value;
	}

	public override void WriteJson(JsonWriter writer, TEnum value,
		JsonSerializer serializer)
	{
		_ = serializer;
		writer.WriteValue(ToWire(value));
	}
}
