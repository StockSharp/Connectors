namespace StockSharp.Amberdata;

[JsonConverter(typeof(AmberdataEnumConverter<AmberdataSocketMethods>))]
enum AmberdataSocketMethods
{
	Unknown,

	[EnumMember(Value = "subscribe")]
	Subscribe,

	[EnumMember(Value = "unsubscribe")]
	Unsubscribe,

	[EnumMember(Value = "subscription")]
	Subscription,
}

[JsonConverter(typeof(AmberdataEnumConverter<AmberdataSocketChannels>))]
enum AmberdataSocketChannels
{
	Unknown,

	[EnumMember(Value = "market:spot:trades")]
	Trades,

	[EnumMember(Value = "market:spot:tickers:snapshots")]
	TickerSnapshots,

	[EnumMember(Value = "market:spot:order:snapshots")]
	OrderSnapshots,

	[EnumMember(Value = "market:spot:ohlcv")]
	Ohlcv,
}

[JsonConverter(typeof(AmberdataEnumConverter<AmberdataTimeIntervals>))]
enum AmberdataTimeIntervals
{
	Unknown,

	[EnumMember(Value = "minutes")]
	Minutes,

	[EnumMember(Value = "hours")]
	Hours,

	[EnumMember(Value = "days")]
	Days,
}

sealed class AmberdataEnumConverter<TEnum> : JsonConverter<TEnum>
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
		return !value.IsEmpty() && _fromWire.TryGetValue(value, out var result)
			? result
			: default;
	}

	public override void WriteJson(JsonWriter writer, TEnum value,
		JsonSerializer serializer)
	{
		_ = serializer;
		if (EqualityComparer<TEnum>.Default.Equals(value, default))
			throw new JsonSerializationException(
				$"Unknown Amberdata {typeof(TEnum).Name} value cannot be serialized.");
		writer.WriteValue(GetWireValue(value));
	}
}
