namespace StockSharp.Backpack.Native.Model;

[JsonConverter(typeof(StringEnumConverter))]
enum BackpackMarketDataEvents
{
	[EnumMember(Value = "bookTicker")]
	BookTicker,
	[EnumMember(Value = "depth")]
	Depth,
	[EnumMember(Value = "trade")]
	Trade,
	[EnumMember(Value = "ticker")]
	Ticker,
	[EnumMember(Value = "kline")]
	Kline,
	[EnumMember(Value = "markPrice")]
	MarkPrice,
}
