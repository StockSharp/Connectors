namespace StockSharp.CoinMetrics;

[JsonConverter(typeof(CoinMetricsEnumConverter<CoinMetricsMarketTypes>))]
enum CoinMetricsMarketTypes
{
	Unknown,

	[EnumMember(Value = "spot")]
	Spot,

	[EnumMember(Value = "future")]
	Future,

	[EnumMember(Value = "option")]
	Option,
}

[JsonConverter(typeof(CoinMetricsEnumConverter<CoinMetricsAssetClasses>))]
enum CoinMetricsAssetClasses
{
	Unknown,

	[EnumMember(Value = "digital")]
	Digital,

	[EnumMember(Value = "equity")]
	Equity,

	[EnumMember(Value = "fixed_income")]
	FixedIncome,
}

[JsonConverter(typeof(CoinMetricsEnumConverter<CoinMetricsMarketStatuses>))]
enum CoinMetricsMarketStatuses
{
	Unknown,

	[EnumMember(Value = "online")]
	Online,

	[EnumMember(Value = "offline")]
	Offline,
}

[JsonConverter(typeof(CoinMetricsEnumConverter<CoinMetricsOptionTypes>))]
enum CoinMetricsOptionTypes
{
	Unknown,

	[EnumMember(Value = "call")]
	Call,

	[EnumMember(Value = "put")]
	Put,
}

[JsonConverter(typeof(CoinMetricsEnumConverter<CoinMetricsTradeSides>))]
enum CoinMetricsTradeSides
{
	Unknown,

	[EnumMember(Value = "buy")]
	Buy,

	[EnumMember(Value = "sell")]
	Sell,
}

[JsonConverter(typeof(CoinMetricsEnumConverter<CoinMetricsBookMessageTypes>))]
enum CoinMetricsBookMessageTypes
{
	Unknown,

	[EnumMember(Value = "snapshot")]
	Snapshot,

	[EnumMember(Value = "update")]
	Update,
}

enum CoinMetricsStreamKinds
{
	Unknown,
	Trades,
	Quotes,
	OrderBooks,
	Candles,
}

[JsonConverter(typeof(CoinMetricsEnumConverter<CoinMetricsCandleFrequencies>))]
enum CoinMetricsCandleFrequencies
{
	Unknown,

	[EnumMember(Value = "1m")]
	OneMinute,

	[EnumMember(Value = "5m")]
	FiveMinutes,

	[EnumMember(Value = "10m")]
	TenMinutes,

	[EnumMember(Value = "15m")]
	FifteenMinutes,

	[EnumMember(Value = "30m")]
	ThirtyMinutes,

	[EnumMember(Value = "1h")]
	OneHour,

	[EnumMember(Value = "4h")]
	FourHours,

	[EnumMember(Value = "1d")]
	OneDay,
}

[JsonConverter(typeof(CoinMetricsEnumConverter<CoinMetricsBackfillModes>))]
enum CoinMetricsBackfillModes
{
	Unknown,

	[EnumMember(Value = "latest")]
	Latest,

	[EnumMember(Value = "none")]
	None,
}

[JsonConverter(typeof(CoinMetricsEnumConverter<CoinMetricsPagingDirections>))]
enum CoinMetricsPagingDirections
{
	Unknown,

	[EnumMember(Value = "start")]
	Start,

	[EnumMember(Value = "end")]
	End,
}

[JsonConverter(typeof(CoinMetricsEnumConverter<CoinMetricsGranularities>))]
enum CoinMetricsGranularities
{
	Unknown,

	[EnumMember(Value = "raw")]
	Raw,

	[EnumMember(Value = "1m")]
	OneMinute,

	[EnumMember(Value = "1h")]
	OneHour,

	[EnumMember(Value = "1d")]
	OneDay,
}

[JsonConverter(typeof(CoinMetricsEnumConverter<CoinMetricsBookDatasets>))]
enum CoinMetricsBookDatasets
{
	Unknown,

	[EnumMember(Value = "snapshots")]
	Snapshots,

	[EnumMember(Value = "updates")]
	Updates,
}

[JsonConverter(typeof(CoinMetricsEnumConverter<CoinMetricsBookDepthModes>))]
enum CoinMetricsBookDepthModes
{
	Unknown,

	[EnumMember(Value = "100")]
	Hundred,

	[EnumMember(Value = "full_book")]
	FullBook,
}

sealed class CoinMetricsEnumConverter<TEnum> : JsonConverter<TEnum>
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

	internal static string ToWire(TEnum value)
	{
		if (EqualityComparer<TEnum>.Default.Equals(value, default))
			throw new ArgumentOutOfRangeException(nameof(value), value,
				$"Unknown Coin Metrics {typeof(TEnum).Name} value cannot be serialized.");
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
		return !value.IsEmpty() && _fromWire.TryGetValue(value, out var result)
			? result
			: default;
	}

	public override void WriteJson(JsonWriter writer, TEnum value,
		JsonSerializer serializer)
	{
		_ = serializer;
		writer.WriteValue(ToWire(value));
	}
}
