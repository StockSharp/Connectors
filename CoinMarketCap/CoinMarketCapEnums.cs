namespace StockSharp.CoinMarketCap;

/// <summary>CoinMarketCap API access modes.</summary>
public enum CoinMarketCapAccessModes
{
	/// <summary>Public API without an API key.</summary>
	[Display(Name = "Keyless")]
	Keyless,

	/// <summary>Authenticated CoinMarketCap Pro API.</summary>
	[Display(Name = "API key")]
	ApiKey,
}

[JsonConverter(typeof(CoinMarketCapEnumConverter<CoinMarketCapTimePeriods>))]
enum CoinMarketCapTimePeriods
{
	[EnumMember(Value = "hourly")]
	Hourly,

	[EnumMember(Value = "daily")]
	Daily,
}

[JsonConverter(typeof(CoinMarketCapEnumConverter<CoinMarketCapSocketMethods>))]
enum CoinMarketCapSocketMethods
{
	[EnumMember(Value = "subscribe")]
	Subscribe,

	[EnumMember(Value = "unsubscribe")]
	Unsubscribe,

	[EnumMember(Value = "unsubscribe_all")]
	UnsubscribeAll,

	[EnumMember(Value = "ping")]
	Ping,
}

[JsonConverter(typeof(CoinMarketCapEnumConverter<CoinMarketCapSocketChannels>))]
enum CoinMarketCapSocketChannels
{
	[EnumMember(Value = "market@crypto_latest_price")]
	LatestPrice,
}

[JsonConverter(typeof(CoinMarketCapEnumConverter<CoinMarketCapSocketMessageTypes>))]
enum CoinMarketCapSocketMessageTypes
{
	[EnumMember(Value = "welcome")]
	Welcome,

	[EnumMember(Value = "ack")]
	Acknowledgement,

	[EnumMember(Value = "data")]
	Data,

	[EnumMember(Value = "error")]
	Error,

	[EnumMember(Value = "pong")]
	Pong,
}

sealed class CoinMarketCapEnumConverter<TEnum> : JsonConverter<TEnum>
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
		throw new JsonSerializationException(
			$"Unknown CoinMarketCap {typeof(TEnum).Name} value '{value}'.");
	}

	public override void WriteJson(JsonWriter writer, TEnum value,
		JsonSerializer serializer)
	{
		_ = serializer;
		writer.WriteValue(GetWireValue(value));
	}
}
