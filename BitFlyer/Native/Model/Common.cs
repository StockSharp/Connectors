namespace StockSharp.BitFlyer.Native.Model;

enum BitFlyerMarketTypes
{
	[EnumMember(Value = "Spot")]
	Spot,

	[EnumMember(Value = "FX")]
	Fx,
}

enum BitFlyerSides
{
	[EnumMember(Value = "BUY")]
	Buy,

	[EnumMember(Value = "SELL")]
	Sell,
}

enum BitFlyerChildOrderTypes
{
	[EnumMember(Value = "LIMIT")]
	Limit,

	[EnumMember(Value = "MARKET")]
	Market,
}

enum BitFlyerTimeInForces
{
	[EnumMember(Value = "GTC")]
	GoodTillCanceled,

	[EnumMember(Value = "IOC")]
	ImmediateOrCancel,

	[EnumMember(Value = "FOK")]
	FillOrKill,
}

enum BitFlyerOrderStates
{
	[EnumMember(Value = "ACTIVE")]
	Active,

	[EnumMember(Value = "COMPLETED")]
	Completed,

	[EnumMember(Value = "CANCELED")]
	Canceled,

	[EnumMember(Value = "EXPIRED")]
	Expired,

	[EnumMember(Value = "REJECTED")]
	Rejected,
}

enum BitFlyerOrderMethods
{
	[EnumMember(Value = "SIMPLE")]
	Simple,

	[EnumMember(Value = "IFD")]
	IfDone,

	[EnumMember(Value = "OCO")]
	OneCancelsOther,

	[EnumMember(Value = "IFDOCO")]
	IfDoneOneCancelsOther,
}

enum BitFlyerConditionTypes
{
	[EnumMember(Value = "LIMIT")]
	Limit,

	[EnumMember(Value = "MARKET")]
	Market,

	[EnumMember(Value = "STOP")]
	Stop,

	[EnumMember(Value = "STOP_LIMIT")]
	StopLimit,

	[EnumMember(Value = "TRAIL")]
	Trail,
}

enum BitFlyerMarketStates
{
	[EnumMember(Value = "RUNNING")]
	Running,

	[EnumMember(Value = "CLOSED")]
	Closed,

	[EnumMember(Value = "STARTING")]
	Starting,

	[EnumMember(Value = "PREOPEN")]
	PreOpen,

	[EnumMember(Value = "CIRCUIT BREAK")]
	CircuitBreak,

	[EnumMember(Value = "MATURED")]
	Matured,
}

enum BitFlyerChildEventTypes
{
	[EnumMember(Value = "ORDER")]
	Order,

	[EnumMember(Value = "ORDER_FAILED")]
	OrderFailed,

	[EnumMember(Value = "CANCEL")]
	Cancel,

	[EnumMember(Value = "CANCEL_FAILED")]
	CancelFailed,

	[EnumMember(Value = "EXECUTION")]
	Execution,

	[EnumMember(Value = "EXPIRE")]
	Expire,
}

enum BitFlyerParentEventTypes
{
	[EnumMember(Value = "ORDER")]
	Order,

	[EnumMember(Value = "ORDER_FAILED")]
	OrderFailed,

	[EnumMember(Value = "CANCEL")]
	Cancel,

	[EnumMember(Value = "TRIGGER")]
	Trigger,

	[EnumMember(Value = "COMPLETE")]
	Complete,

	[EnumMember(Value = "EXPIRE")]
	Expire,
}

enum BitFlyerParentOrderTypes
{
	[EnumMember(Value = "SIMPLE")]
	Simple,

	[EnumMember(Value = "LIMIT")]
	Limit,

	[EnumMember(Value = "MARKET")]
	Market,

	[EnumMember(Value = "STOP")]
	Stop,

	[EnumMember(Value = "STOP_LIMIT")]
	StopLimit,

	[EnumMember(Value = "TRAIL")]
	Trail,

	[EnumMember(Value = "IFD")]
	IfDone,

	[EnumMember(Value = "OCO")]
	OneCancelsOther,

	[EnumMember(Value = "IFDOCO")]
	IfDoneOneCancelsOther,
}

enum BitFlyerRpcMethods
{
	[EnumMember(Value = "auth")]
	Authenticate,

	[EnumMember(Value = "subscribe")]
	Subscribe,

	[EnumMember(Value = "unsubscribe")]
	Unsubscribe,

	[EnumMember(Value = "channelMessage")]
	ChannelMessage,
}

sealed class BitFlyerApiError
{
	[JsonProperty("status")]
	public int Status { get; set; }

	[JsonProperty("error_message")]
	public string Message { get; set; }
}

sealed class BitFlyerApiException : InvalidOperationException
{
	public BitFlyerApiException(int status, string message)
		: base(message)
	{
		Status = status;
	}

	public int Status { get; }
}

sealed class BitFlyerNullableSideConverter : JsonConverter<BitFlyerSides?>
{
	public override BitFlyerSides? ReadJson(JsonReader reader, Type objectType,
		BitFlyerSides? existingValue, bool hasExistingValue,
		JsonSerializer serializer)
	{
		_ = objectType;
		_ = existingValue;
		_ = hasExistingValue;
		_ = serializer;
		if (reader.TokenType is JsonToken.Null or JsonToken.Undefined)
			return null;
		if (reader.TokenType != JsonToken.String)
			throw new JsonSerializationException(
				"bitFlyer side must be a JSON string.");
		return ((string)reader.Value) switch
		{
			"BUY" => BitFlyerSides.Buy,
			"SELL" => BitFlyerSides.Sell,
			"" or null => null,
			var value => throw new JsonSerializationException(
				$"Unknown bitFlyer side '{value}'."),
		};
	}

	public override void WriteJson(JsonWriter writer, BitFlyerSides? value,
		JsonSerializer serializer)
	{
		_ = serializer;
		if (value is null)
			writer.WriteNull();
		else
			writer.WriteValue(value == BitFlyerSides.Buy ? "BUY" : "SELL");
	}
}
