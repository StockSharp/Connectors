namespace StockSharp.Upstox.Native.Model;

enum UpstoxFeedModes
{
	[EnumMember(Value = "ltpc")]
	Ltpc,

	[EnumMember(Value = "full")]
	Full,

	[EnumMember(Value = "option_greeks")]
	OptionGreeks,

	[EnumMember(Value = "full_d30")]
	FullD30,
}
