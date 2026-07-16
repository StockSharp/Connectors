namespace StockSharp.KoreaInvestment.Native.Model;

sealed class KisStreamRequest
{
	[JsonProperty("header")]
	public KisStreamRequestHeader Header { get; set; }

	[JsonProperty("body")]
	public KisStreamRequestBody Body { get; set; }
}

sealed class KisStreamRequestHeader
{
	[JsonProperty("approval_key")]
	public string ApprovalKey { get; set; }

	[JsonProperty("custtype")]
	public string CustomerType { get; set; } = "P";

	[JsonProperty("tr_type")]
	public string TransactionType { get; set; }

	[JsonProperty("content-type")]
	public string ContentType { get; set; } = "utf-8";
}

sealed class KisStreamRequestBody
{
	[JsonProperty("input")]
	public KisStreamRequestInput Input { get; set; }
}

sealed class KisStreamRequestInput
{
	[JsonProperty("tr_id")]
	public string TransactionId { get; set; }

	[JsonProperty("tr_key")]
	public string TransactionKey { get; set; }
}

sealed class KisStreamSystemResponse
{
	[JsonProperty("header")]
	public KisStreamResponseHeader Header { get; set; }

	[JsonProperty("body")]
	public KisStreamResponseBody Body { get; set; }
}

sealed class KisStreamResponseHeader
{
	[JsonProperty("tr_id")]
	public string TransactionId { get; set; }

	[JsonProperty("tr_key")]
	public string TransactionKey { get; set; }

	[JsonProperty("encrypt")]
	public string Encryption { get; set; }
}

sealed class KisStreamResponseBody
{
	[JsonProperty("rt_cd")]
	public string ReturnCode { get; set; }

	[JsonProperty("msg_cd")]
	public string MessageCode { get; set; }

	[JsonProperty("msg1")]
	public string Message { get; set; }

	[JsonProperty("output")]
	public KisStreamEncryption Output { get; set; }
}

sealed class KisStreamEncryption
{
	[JsonProperty("iv")]
	public string InitializationVector { get; set; }

	[JsonProperty("key")]
	public string Key { get; set; }
}

readonly record struct KisStreamSubscription(KisRealtimeChannels Channel, string Key);

abstract record KisRealtimeEvent(KisRealtimeChannels Channel, string Symbol, DateTime ServerTime);

sealed record KisRealtimeTrade(
	KisRealtimeChannels Channel,
	string Symbol,
	DateTime ServerTime,
	decimal Price,
	decimal Volume,
	decimal? OpenPrice,
	decimal? HighPrice,
	decimal? LowPrice,
	decimal? TotalVolume,
	decimal? Turnover,
	decimal? BidPrice,
	decimal? BidVolume,
	decimal? AskPrice,
	decimal? AskVolume,
	decimal? OpenInterest)
	: KisRealtimeEvent(Channel, Symbol, ServerTime);

sealed record KisRealtimeDepth(
	KisRealtimeChannels Channel,
	string Symbol,
	DateTime ServerTime,
	decimal[] BidPrices,
	decimal[] BidVolumes,
	decimal[] AskPrices,
	decimal[] AskVolumes)
	: KisRealtimeEvent(Channel, Symbol, ServerTime);

sealed record KisRealtimeOrderNotice(
	KisRealtimeChannels Channel,
	string Symbol,
	DateTime ServerTime,
	string AccountNumber,
	string OrderNumber,
	string OriginalOrderNumber,
	Sides Side,
	decimal OrderQuantity,
	decimal? OrderPrice,
	decimal FilledQuantity,
	decimal? FillPrice,
	bool IsExecution,
	bool IsRejected,
	bool IsAccepted,
	string Name)
	: KisRealtimeEvent(Channel, Symbol, ServerTime);

static class KisRealtimeParser
{
	public static int GetFieldCount(KisRealtimeChannels channel)
		=> channel switch
		{
			KisRealtimeChannels.DomesticTrade => 46,
			KisRealtimeChannels.DomesticDepth => 59,
			KisRealtimeChannels.DerivativeTrade => 50,
			KisRealtimeChannels.OptionTrade => 58,
			KisRealtimeChannels.DerivativeDepth or KisRealtimeChannels.OptionDepth => 38,
			KisRealtimeChannels.OverseasTrade => 25,
			KisRealtimeChannels.OverseasDepth => 16,
			KisRealtimeChannels.DomesticOrderNotice => 26,
			KisRealtimeChannels.DerivativeOrderNotice => 22,
			KisRealtimeChannels.OverseasOrderNotice => 25,
			_ => 0,
		};

	public static KisRealtimeEvent Parse(KisRealtimeChannels channel, string[] fields, KisSecurityInfo security)
		=> channel switch
		{
			KisRealtimeChannels.DomesticTrade => ParseDomesticTrade(channel, fields, security),
			KisRealtimeChannels.DomesticDepth => ParseDomesticDepth(channel, fields, security),
			KisRealtimeChannels.DerivativeTrade => ParseDerivativeTrade(channel, fields, security, false),
			KisRealtimeChannels.OptionTrade => ParseDerivativeTrade(channel, fields, security, true),
			KisRealtimeChannels.DerivativeDepth or KisRealtimeChannels.OptionDepth => ParseDerivativeDepth(channel, fields, security),
			KisRealtimeChannels.OverseasTrade => ParseOverseasTrade(channel, fields, security),
			KisRealtimeChannels.OverseasDepth => ParseOverseasDepth(channel, fields, security),
			KisRealtimeChannels.DomesticOrderNotice => ParseDomesticNotice(channel, fields, security),
			KisRealtimeChannels.DerivativeOrderNotice => ParseDerivativeNotice(channel, fields, security),
			KisRealtimeChannels.OverseasOrderNotice => ParseOverseasNotice(channel, fields, security),
			_ => throw new ArgumentOutOfRangeException(nameof(channel), channel, null),
		};

	private static KisRealtimeTrade ParseDomesticTrade(KisRealtimeChannels channel, string[] f, KisSecurityInfo security)
		=> new(channel, f[0], f[33].ToKisUtc(f[1], security), Value(f, 2), Value(f, 12),
			Nullable(f, 7), Nullable(f, 8), Nullable(f, 9), Nullable(f, 13), Nullable(f, 14),
			Nullable(f, 11), Nullable(f, 37), Nullable(f, 10), Nullable(f, 36), null);

	private static KisRealtimeDepth ParseDomesticDepth(KisRealtimeChannels channel, string[] f, KisSecurityInfo security)
		=> new(channel, f[0], string.Empty.ToKisUtc(f[1], security),
			Values(f, 13, 10), Values(f, 33, 10), Values(f, 3, 10), Values(f, 23, 10));

	private static KisRealtimeTrade ParseDerivativeTrade(KisRealtimeChannels channel, string[] f,
		KisSecurityInfo security, bool isOption)
	{
		var bid = isOption ? 42 : 35;
		var ask = isOption ? 41 : 34;
		var bidVolume = isOption ? 44 : 37;
		var askVolume = isOption ? 43 : 36;
		return new(channel, f[0], string.Empty.ToKisUtc(f[1], security), Value(f, isOption ? 2 : 5), Value(f, 9),
			Nullable(f, isOption ? 6 : 6), Nullable(f, isOption ? 7 : 7), Nullable(f, isOption ? 8 : 8),
			Nullable(f, 10), Nullable(f, 11), Nullable(f, bid), Nullable(f, bidVolume), Nullable(f, ask),
			Nullable(f, askVolume), Nullable(f, isOption ? 13 : 18));
	}

	private static KisRealtimeDepth ParseDerivativeDepth(KisRealtimeChannels channel, string[] f, KisSecurityInfo security)
		=> new(channel, f[0], string.Empty.ToKisUtc(f[1], security),
			Values(f, 7, 5), Values(f, 27, 5), Values(f, 2, 5), Values(f, 22, 5));

	private static KisRealtimeTrade ParseOverseasTrade(KisRealtimeChannels channel, string[] f, KisSecurityInfo security)
		=> new(channel, f[0], f[3].ToKisUtc(f[4], security), Value(f, 10), Value(f, 18),
			Nullable(f, 7), Nullable(f, 8), Nullable(f, 9), Nullable(f, 19), Nullable(f, 20),
			Nullable(f, 14), Nullable(f, 16), Nullable(f, 15), Nullable(f, 17), null);

	private static KisRealtimeDepth ParseOverseasDepth(KisRealtimeChannels channel, string[] f, KisSecurityInfo security)
		=> new(channel, f[0], f[2].ToKisUtc(f[3], security),
			[Value(f, 10)], [Value(f, 12)], [Value(f, 11)], [Value(f, 13)]);

	private static KisRealtimeOrderNotice ParseDomesticNotice(KisRealtimeChannels channel, string[] f, KisSecurityInfo security)
		=> Notice(channel, f, security, 8, 9, 10, 11, 13, 14, 12, 16, 25, 17);

	private static KisRealtimeOrderNotice ParseDerivativeNotice(KisRealtimeChannels channel, string[] f, KisSecurityInfo security)
		=> Notice(channel, f, security, 7, 8, 9, 10, 12, 13, 11, 15, 21, 17);

	private static KisRealtimeOrderNotice ParseOverseasNotice(KisRealtimeChannels channel, string[] f, KisSecurityInfo security)
		=> Notice(channel, f, security, 7, 8, 9, 10, 12, 13, 11, 15, 24, 17);

	private static KisRealtimeOrderNotice Notice(KisRealtimeChannels channel, string[] f, KisSecurityInfo security,
		int symbol, int filledQuantity, int fillPrice, int time, int execution, int accepted, int rejected,
		int orderQuantity, int orderPrice, int name)
		=> new(channel, f[symbol], string.Empty.ToKisUtc(f[time], security), f[1], f[2], f[3],
			f[4] == "02" ? Sides.Buy : Sides.Sell, Value(f, orderQuantity), Nullable(f, orderPrice),
			Value(f, filledQuantity), Nullable(f, fillPrice), f[execution] == "2" || Value(f, filledQuantity) > 0,
			f[rejected] == "Y", f[accepted] == "Y", f[name]);

	private static decimal Value(string[] fields, int index)
		=> Nullable(fields, index) ?? 0;

	private static decimal? Nullable(string[] fields, int index)
		=> index >= 0 && index < fields.Length ? fields[index].ToDecimal() : null;

	private static decimal[] Values(string[] fields, int start, int count)
		=> [.. Enumerable.Range(start, count).Select(index => Value(fields, index))];
}
