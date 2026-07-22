namespace StockSharp.Glassnode;

/// <summary>Glassnode asset types.</summary>
[JsonConverter(typeof(GlassnodeEnumConverter<GlassnodeAssetTypes>))]
public enum GlassnodeAssetTypes
{
	/// <summary>Unknown value.</summary>
	Unknown,

	/// <summary>Native blockchain asset.</summary>
	[EnumMember(Value = "BLOCKCHAIN")]
	Blockchain,

	/// <summary>Token issued on a blockchain.</summary>
	[EnumMember(Value = "TOKEN")]
	Token,
}

/// <summary>Glassnode time-series intervals.</summary>
public enum GlassnodeIntervals
{
	/// <summary>Unknown value.</summary>
	Unknown,

	/// <summary>Ten minutes.</summary>
	[EnumMember(Value = "10m")]
	TenMinutes,

	/// <summary>One hour.</summary>
	[EnumMember(Value = "1h")]
	OneHour,

	/// <summary>One day.</summary>
	[EnumMember(Value = "24h")]
	OneDay,
}

sealed class GlassnodeEnumConverter<TEnum> : JsonConverter<TEnum>
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
				$"Unknown Glassnode {typeof(TEnum).Name} value cannot be serialized.");
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
