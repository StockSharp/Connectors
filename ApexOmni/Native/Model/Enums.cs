namespace StockSharp.ApexOmni.Native.Model;

[JsonConverter(typeof(StringEnumConverter))]
enum ApexOmniNativeSides
{
	[EnumMember(Value = "BUY")]
	Buy,
	[EnumMember(Value = "SELL")]
	Sell,
}

[JsonConverter(typeof(StringEnumConverter))]
enum ApexOmniPositionSides
{
	[EnumMember(Value = "LONG")]
	Long,
	[EnumMember(Value = "SHORT")]
	Short,
}

[JsonConverter(typeof(StringEnumConverter))]
enum ApexOmniNativeOrderTypes
{
	[EnumMember(Value = "UNKNOWN_ORDER_TYPE")]
	Unknown,
	[EnumMember(Value = "LIMIT")]
	Limit,
	[EnumMember(Value = "MARKET")]
	Market,
	[EnumMember(Value = "STOP_LIMIT")]
	StopLimit,
	[EnumMember(Value = "STOP_MARKET")]
	StopMarket,
	[EnumMember(Value = "TAKE_PROFIT_LIMIT")]
	TakeProfitLimit,
	[EnumMember(Value = "TAKE_PROFIT_MARKET")]
	TakeProfitMarket,
}

[JsonConverter(typeof(StringEnumConverter))]
enum ApexOmniOrderStatuses
{
	[EnumMember(Value = "PENDING")]
	Pending,
	[EnumMember(Value = "OPEN")]
	Open,
	[EnumMember(Value = "FILLED")]
	Filled,
	[EnumMember(Value = "CANCELED")]
	Canceled,
	[EnumMember(Value = "EXPIRED")]
	Expired,
	[EnumMember(Value = "UNTRIGGERED")]
	Untriggered,
}

[JsonConverter(typeof(StringEnumConverter))]
enum ApexOmniTimeInForces
{
	[EnumMember(Value = "GOOD_TIL_CANCEL")]
	GoodTilCancel = 1,
	[EnumMember(Value = "FILL_OR_KILL")]
	FillOrKill = 2,
	[EnumMember(Value = "IMMEDIATE_OR_CANCEL")]
	ImmediateOrCancel = 3,
	[EnumMember(Value = "POST_ONLY")]
	PostOnly = 4,
}

[JsonConverter(typeof(StringEnumConverter))]
enum ApexOmniTriggerPriceTypes
{
	[EnumMember(Value = "UNKNOWN_PRICE_TYPE")]
	Unknown,
	[EnumMember(Value = "MARKET")]
	Market,
	[EnumMember(Value = "INDEX")]
	Index,
	[EnumMember(Value = "ORACLE")]
	Oracle,
}

[JsonConverter(typeof(StringEnumConverter))]
enum ApexOmniWebSocketTypes
{
	[EnumMember(Value = "snapshot")]
	Snapshot,
	[EnumMember(Value = "delta")]
	Delta,
}

[JsonConverter(typeof(StringEnumConverter))]
enum ApexOmniWebSocketOperations
{
	[EnumMember(Value = "subscribe")]
	Subscribe,
	[EnumMember(Value = "unsubscribe")]
	Unsubscribe,
	[EnumMember(Value = "ping")]
	Ping,
	[EnumMember(Value = "pong")]
	Pong,
	[EnumMember(Value = "login")]
	Login,
}

[JsonConverter(typeof(StringEnumConverter))]
enum ApexOmniPrivateLoginTypes
{
	[EnumMember(Value = "login")]
	Login,
}

[JsonConverter(typeof(StringEnumConverter))]
enum ApexOmniHttpMethods
{
	[EnumMember(Value = "GET")]
	Get,
}

enum ApexOmniInstrumentGroups
{
	Perpetual,
	Prelaunch,
	Prediction,
	Stock,
}
