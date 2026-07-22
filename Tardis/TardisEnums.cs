namespace StockSharp.Tardis;

[JsonConverter(typeof(TardisEnumConverter<TardisInstrumentTypes>))]
enum TardisInstrumentTypes
{
	Unknown,

	[EnumMember(Value = "spot")]
	Spot,

	[EnumMember(Value = "perpetual")]
	Perpetual,

	[EnumMember(Value = "future")]
	Future,

	[EnumMember(Value = "option")]
	Option,

	[EnumMember(Value = "combo")]
	Combo,
}

[JsonConverter(typeof(TardisEnumConverter<TardisContractTypes>))]
enum TardisContractTypes
{
	Unknown,

	[EnumMember(Value = "move")]
	Move,

	[EnumMember(Value = "linear_future")]
	LinearFuture,

	[EnumMember(Value = "inverse_future")]
	InverseFuture,

	[EnumMember(Value = "quanto_future")]
	QuantoFuture,

	[EnumMember(Value = "linear_perpetual")]
	LinearPerpetual,

	[EnumMember(Value = "inverse_perpetual")]
	InversePerpetual,

	[EnumMember(Value = "quanto_perpetual")]
	QuantoPerpetual,

	[EnumMember(Value = "put_option")]
	PutOption,

	[EnumMember(Value = "call_option")]
	CallOption,

	[EnumMember(Value = "turbo_put_option")]
	TurboPutOption,

	[EnumMember(Value = "turbo_call_option")]
	TurboCallOption,

	[EnumMember(Value = "spread")]
	Spread,

	[EnumMember(Value = "interest_rate_swap")]
	InterestRateSwap,

	[EnumMember(Value = "repo")]
	Repo,

	[EnumMember(Value = "index")]
	Index,
}

[JsonConverter(typeof(TardisEnumConverter<TardisOptionTypes>))]
enum TardisOptionTypes
{
	Unknown,

	[EnumMember(Value = "call")]
	Call,

	[EnumMember(Value = "put")]
	Put,
}

[JsonConverter(typeof(TardisEnumConverter<TardisMessageTypes>))]
enum TardisMessageTypes
{
	Unknown,

	[EnumMember(Value = "trade")]
	Trade,

	[EnumMember(Value = "book_change")]
	BookChange,

	[EnumMember(Value = "book_ticker")]
	BookTicker,

	[EnumMember(Value = "derivative_ticker")]
	DerivativeTicker,

	[EnumMember(Value = "book_snapshot")]
	BookSnapshot,

	[EnumMember(Value = "trade_bar")]
	TradeBar,

	[EnumMember(Value = "liquidation")]
	Liquidation,

	[EnumMember(Value = "option_summary")]
	OptionSummary,

	[EnumMember(Value = "disconnect")]
	Disconnect,

	[EnumMember(Value = "error")]
	Error,
}

[JsonConverter(typeof(TardisEnumConverter<TardisSides>))]
enum TardisSides
{
	Unknown,

	[EnumMember(Value = "buy")]
	Buy,

	[EnumMember(Value = "sell")]
	Sell,
}

[JsonConverter(typeof(TardisEnumConverter<TardisBarKinds>))]
enum TardisBarKinds
{
	Unknown,

	[EnumMember(Value = "time")]
	Time,

	[EnumMember(Value = "volume")]
	Volume,

	[EnumMember(Value = "tick")]
	Tick,
}

enum TardisStreamKinds
{
	Unknown,
	Trades,
	Level1,
	MarketDepth,
	Candles,
}

sealed class TardisEnumConverter<TEnum> : JsonConverter<TEnum>
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
				$"Unknown Tardis {typeof(TEnum).Name} value cannot be serialized.");
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
