namespace StockSharp.CoinApi;

[JsonConverter(typeof(CoinApiEnumConverter<CoinApiSymbolTypes>))]
enum CoinApiSymbolTypes
{
	Unknown,

	[EnumMember(Value = "SPOT")]
	Spot,

	[EnumMember(Value = "FUTURES")]
	Futures,

	[EnumMember(Value = "OPTION")]
	Option,

	[EnumMember(Value = "PERPETUAL")]
	Perpetual,

	[EnumMember(Value = "DEPLOYER_PERPETUAL")]
	DeployerPerpetual,

	[EnumMember(Value = "INDEX")]
	Index,

	[EnumMember(Value = "CREDIT")]
	Credit,

	[EnumMember(Value = "CONTRACT")]
	Contract,

	[EnumMember(Value = "OPTION_COMBO")]
	OptionCombo,

	[EnumMember(Value = "FUTURE_COMBO")]
	FutureCombo,
}

[JsonConverter(typeof(CoinApiEnumConverter<CoinApiTakerSides>))]
enum CoinApiTakerSides
{
	Unknown,

	[EnumMember(Value = "BUY")]
	Buy,

	[EnumMember(Value = "SELL")]
	Sell,

	[EnumMember(Value = "BUY_ESTIMATED")]
	BuyEstimated,

	[EnumMember(Value = "SELL_ESTIMATED")]
	SellEstimated,
}

[JsonConverter(typeof(CoinApiEnumConverter<CoinApiSocketRequestTypes>))]
enum CoinApiSocketRequestTypes
{
	Unknown,

	[EnumMember(Value = "hello")]
	Hello,

	[EnumMember(Value = "subscribe")]
	Subscribe,

	[EnumMember(Value = "unsubscribe")]
	Unsubscribe,
}

[JsonConverter(typeof(CoinApiEnumConverter<CoinApiSocketDataTypes>))]
enum CoinApiSocketDataTypes
{
	Unknown,

	[EnumMember(Value = "trade")]
	Trade,

	[EnumMember(Value = "quote")]
	Quote,

	[EnumMember(Value = "book5")]
	Book5,

	[EnumMember(Value = "book20")]
	Book20,

	[EnumMember(Value = "book50")]
	Book50,

	[EnumMember(Value = "ohlcv")]
	Ohlcv,
}

[JsonConverter(typeof(CoinApiSocketMessageTypeConverter))]
enum CoinApiSocketMessageTypes
{
	Unknown,
	Trade,
	Quote,
	Book5,
	Book20,
	Book50,
	Ohlcv,
	Error,
	Reconnect,
	Heartbeat,
}

[JsonConverter(typeof(CoinApiEnumConverter<CoinApiPeriodIds>))]
enum CoinApiPeriodIds
{
	Unknown,

	[EnumMember(Value = "1SEC")]
	Second1,
	[EnumMember(Value = "2SEC")]
	Second2,
	[EnumMember(Value = "3SEC")]
	Second3,
	[EnumMember(Value = "4SEC")]
	Second4,
	[EnumMember(Value = "5SEC")]
	Second5,
	[EnumMember(Value = "6SEC")]
	Second6,
	[EnumMember(Value = "10SEC")]
	Second10,
	[EnumMember(Value = "15SEC")]
	Second15,
	[EnumMember(Value = "20SEC")]
	Second20,
	[EnumMember(Value = "30SEC")]
	Second30,

	[EnumMember(Value = "1MIN")]
	Minute1,
	[EnumMember(Value = "2MIN")]
	Minute2,
	[EnumMember(Value = "3MIN")]
	Minute3,
	[EnumMember(Value = "4MIN")]
	Minute4,
	[EnumMember(Value = "5MIN")]
	Minute5,
	[EnumMember(Value = "6MIN")]
	Minute6,
	[EnumMember(Value = "10MIN")]
	Minute10,
	[EnumMember(Value = "15MIN")]
	Minute15,
	[EnumMember(Value = "20MIN")]
	Minute20,
	[EnumMember(Value = "30MIN")]
	Minute30,

	[EnumMember(Value = "1HRS")]
	Hour1,
	[EnumMember(Value = "2HRS")]
	Hour2,
	[EnumMember(Value = "3HRS")]
	Hour3,
	[EnumMember(Value = "4HRS")]
	Hour4,
	[EnumMember(Value = "6HRS")]
	Hour6,
	[EnumMember(Value = "8HRS")]
	Hour8,
	[EnumMember(Value = "12HRS")]
	Hour12,

	[EnumMember(Value = "1DAY")]
	Day1,
	[EnumMember(Value = "2DAY")]
	Day2,
	[EnumMember(Value = "3DAY")]
	Day3,
	[EnumMember(Value = "5DAY")]
	Day5,
	[EnumMember(Value = "7DAY")]
	Day7,
	[EnumMember(Value = "10DAY")]
	Day10,

	[EnumMember(Value = "1MTH")]
	Month1,
	[EnumMember(Value = "2MTH")]
	Month2,
	[EnumMember(Value = "3MTH")]
	Month3,
	[EnumMember(Value = "4MTH")]
	Month4,
	[EnumMember(Value = "6MTH")]
	Month6,

	[EnumMember(Value = "1YRS")]
	Year1,
	[EnumMember(Value = "2YRS")]
	Year2,
	[EnumMember(Value = "3YRS")]
	Year3,
	[EnumMember(Value = "4YRS")]
	Year4,
	[EnumMember(Value = "5YRS")]
	Year5,
}

sealed class CoinApiEnumConverter<TEnum> : JsonConverter<TEnum>
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
				$"Unknown CoinAPI {typeof(TEnum).Name} value cannot be serialized.");
		writer.WriteValue(GetWireValue(value));
	}
}

sealed class CoinApiSocketMessageTypeConverter :
	JsonConverter<CoinApiSocketMessageTypes>
{
	public override CoinApiSocketMessageTypes ReadJson(JsonReader reader,
		Type objectType, CoinApiSocketMessageTypes existingValue,
		bool hasExistingValue, JsonSerializer serializer)
	{
		_ = objectType;
		_ = existingValue;
		_ = hasExistingValue;
		_ = serializer;
		return reader.Value?.ToString()?.ToLowerInvariant() switch
		{
			"trade" => CoinApiSocketMessageTypes.Trade,
			"quote" => CoinApiSocketMessageTypes.Quote,
			"book5" => CoinApiSocketMessageTypes.Book5,
			"book20" => CoinApiSocketMessageTypes.Book20,
			"book50" => CoinApiSocketMessageTypes.Book50,
			"ohlcv" => CoinApiSocketMessageTypes.Ohlcv,
			"error" => CoinApiSocketMessageTypes.Error,
			"reconnect" => CoinApiSocketMessageTypes.Reconnect,
			"heartbeat" or "hearbeat" => CoinApiSocketMessageTypes.Heartbeat,
			_ => CoinApiSocketMessageTypes.Unknown,
		};
	}

	public override void WriteJson(JsonWriter writer,
		CoinApiSocketMessageTypes value, JsonSerializer serializer)
		=> throw new NotSupportedException(
			"CoinAPI inbound message types are not serialized.");
}
