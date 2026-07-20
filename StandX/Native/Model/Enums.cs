namespace StockSharp.StandX.Native.Model;

[JsonConverter(typeof(StringEnumConverter))]
enum StandXApiSides
{
	[EnumMember(Value = "buy")]
	Buy,

	[EnumMember(Value = "sell")]
	Sell,
}

[JsonConverter(typeof(StringEnumConverter))]
enum StandXApiOrderTypes
{
	[EnumMember(Value = "limit")]
	Limit,

	[EnumMember(Value = "market")]
	Market,
}

[JsonConverter(typeof(StringEnumConverter))]
enum StandXOrderStatuses
{
	[EnumMember(Value = "new")]
	New,

	[EnumMember(Value = "open")]
	Open,

	[EnumMember(Value = "canceled")]
	Canceled,

	[EnumMember(Value = "filled")]
	Filled,

	[EnumMember(Value = "rejected")]
	Rejected,

	[EnumMember(Value = "untriggered")]
	Untriggered,
}

[JsonConverter(typeof(StringEnumConverter))]
enum StandXTimeInForces
{
	[EnumMember(Value = "gtc")]
	GoodTillCanceled,

	[EnumMember(Value = "ioc")]
	ImmediateOrCancel,

	[EnumMember(Value = "alo")]
	AddLiquidityOnly,
}

[JsonConverter(typeof(StringEnumConverter))]
enum StandXApiMarginModes
{
	[EnumMember(Value = "cross")]
	Cross,

	[EnumMember(Value = "isolated")]
	Isolated,
}

[JsonConverter(typeof(StringEnumConverter))]
enum StandXSymbolStatuses
{
	[EnumMember(Value = "trading")]
	Trading,
}

[JsonConverter(typeof(StringEnumConverter))]
enum StandXChannels
{
	[EnumMember(Value = "auth")]
	Auth,

	[EnumMember(Value = "price")]
	Price,

	[EnumMember(Value = "depth_book")]
	DepthBook,

	[EnumMember(Value = "public_trade")]
	PublicTrade,

	[EnumMember(Value = "order")]
	Order,

	[EnumMember(Value = "position")]
	Position,

	[EnumMember(Value = "balance")]
	Balance,

	[EnumMember(Value = "trade")]
	Trade,
}

[JsonConverter(typeof(StringEnumConverter))]
enum StandXOrderSocketMethods
{
	[EnumMember(Value = "auth:login")]
	Login,

	[EnumMember(Value = "order:new")]
	NewOrder,

	[EnumMember(Value = "order:cancel")]
	CancelOrder,
}
