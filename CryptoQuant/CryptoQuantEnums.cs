namespace StockSharp.CryptoQuant;

enum CryptoQuantInstrumentKinds
{
	Unknown,
	NativeAsset,
	Token,
}

[JsonConverter(typeof(CryptoQuantEnumConverter<CryptoQuantWindows>))]
enum CryptoQuantWindows
{
	Unknown,

	[EnumMember(Value = "min")]
	Minute,

	[EnumMember(Value = "hour")]
	Hour,

	[EnumMember(Value = "day")]
	Day,
}

[JsonConverter(typeof(CryptoQuantEnumConverter<CryptoQuantStatuses>))]
enum CryptoQuantStatuses
{
	Unknown,

	[EnumMember(Value = "success")]
	Success,

	[EnumMember(Value = "deprecated")]
	Deprecated,

	[EnumMember(Value = "bad_request")]
	BadRequest,

	[EnumMember(Value = "unauthorized")]
	Unauthorized,

	[EnumMember(Value = "forbidden")]
	Forbidden,

	[EnumMember(Value = "not_found")]
	NotFound,

	[EnumMember(Value = "not_allowed")]
	NotAllowed,

	[EnumMember(Value = "too_many_requests")]
	TooManyRequests,

	[EnumMember(Value = "internal_server_error")]
	InternalServerError,
}

sealed class CryptoQuantEnumConverter<TEnum> : JsonConverter<TEnum>
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
				$"Unknown CryptoQuant {typeof(TEnum).Name} value cannot be serialized.");
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
