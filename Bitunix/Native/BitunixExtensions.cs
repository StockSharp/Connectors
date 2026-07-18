namespace StockSharp.Bitunix.Native;

static class BitunixExtensions
{
	public static readonly TimeSpan[] TimeFrames =
	[
		TimeSpan.FromMinutes(1),
		TimeSpan.FromMinutes(5),
		TimeSpan.FromMinutes(15),
		TimeSpan.FromMinutes(30),
		TimeSpan.FromHours(1),
		TimeSpan.FromHours(2),
		TimeSpan.FromHours(4),
		TimeSpan.FromHours(6),
		TimeSpan.FromHours(12),
		TimeSpan.FromDays(1),
		TimeSpan.FromDays(7),
		TimeSpan.FromDays(30),
	];

	private static readonly IReadOnlyDictionary<TimeSpan, string> _spotIntervals =
		new Dictionary<TimeSpan, string>
		{
			[TimeSpan.FromMinutes(1)] = "1",
			[TimeSpan.FromMinutes(5)] = "5",
			[TimeSpan.FromMinutes(15)] = "15",
			[TimeSpan.FromMinutes(30)] = "30",
			[TimeSpan.FromHours(1)] = "60",
			[TimeSpan.FromHours(2)] = "120",
			[TimeSpan.FromHours(4)] = "240",
			[TimeSpan.FromHours(6)] = "360",
			[TimeSpan.FromHours(12)] = "720",
			[TimeSpan.FromDays(1)] = "D",
			[TimeSpan.FromDays(7)] = "W",
			[TimeSpan.FromDays(30)] = "M",
		};

	private static readonly IReadOnlyDictionary<TimeSpan, string> _futuresIntervals =
		new Dictionary<TimeSpan, string>
		{
			[TimeSpan.FromMinutes(1)] = "1m",
			[TimeSpan.FromMinutes(5)] = "5m",
			[TimeSpan.FromMinutes(15)] = "15m",
			[TimeSpan.FromMinutes(30)] = "30m",
			[TimeSpan.FromHours(1)] = "1h",
			[TimeSpan.FromHours(2)] = "2h",
			[TimeSpan.FromHours(4)] = "4h",
			[TimeSpan.FromHours(6)] = "6h",
			[TimeSpan.FromHours(12)] = "12h",
			[TimeSpan.FromDays(1)] = "1d",
			[TimeSpan.FromDays(7)] = "1w",
			[TimeSpan.FromDays(30)] = "1M",
		};

	private static readonly IReadOnlyDictionary<TimeSpan, string> _wsIntervals =
		new Dictionary<TimeSpan, string>
		{
			[TimeSpan.FromMinutes(1)] = "1min",
			[TimeSpan.FromMinutes(5)] = "5min",
			[TimeSpan.FromMinutes(15)] = "15min",
			[TimeSpan.FromMinutes(30)] = "30min",
			[TimeSpan.FromHours(1)] = "60min",
			[TimeSpan.FromHours(2)] = "2h",
			[TimeSpan.FromHours(4)] = "4h",
			[TimeSpan.FromHours(6)] = "6h",
			[TimeSpan.FromHours(12)] = "12h",
			[TimeSpan.FromDays(1)] = "1day",
			[TimeSpan.FromDays(7)] = "1week",
			[TimeSpan.FromDays(30)] = "1month",
		};

	public static string ToSpotInterval(this TimeSpan timeFrame)
		=> GetInterval(_spotIntervals, timeFrame);

	public static string ToFuturesInterval(this TimeSpan timeFrame)
		=> GetInterval(_futuresIntervals, timeFrame);

	public static string ToWsInterval(this TimeSpan timeFrame)
		=> GetInterval(_wsIntervals, timeFrame);

	public static TimeSpan FromWsInterval(this string interval)
	{
		interval = interval?.StartsWith("last_kline_", StringComparison.OrdinalIgnoreCase) == true
			? interval["last_kline_".Length..]
			: interval;
		foreach (var pair in _wsIntervals)
		{
			if (pair.Value.EqualsIgnoreCase(interval))
				return pair.Key;
		}
		throw new InvalidDataException($"Unsupported Bitunix candle interval '{interval}'.");
	}

	private static string GetInterval(IReadOnlyDictionary<TimeSpan, string> intervals,
		TimeSpan timeFrame)
		=> intervals.TryGetValue(timeFrame, out var value)
			? value
			: throw new ArgumentOutOfRangeException(nameof(timeFrame), timeFrame, LocalizedStrings.InvalidValue);

	public static SecurityId ToStockSharp(this string symbol, BitunixSections section)
		=> new()
		{
			SecurityCode = symbol.ThrowIfEmpty(nameof(symbol)).ToUpperInvariant(),
			BoardCode = section.ToBoardCode(),
		};

	public static string ToBoardCode(this BitunixSections section) => section switch
	{
		BitunixSections.Spot => BoardCodes.Bitunix,
		BitunixSections.Futures => BoardCodes.BitunixFutures,
		_ => throw new ArgumentOutOfRangeException(nameof(section), section, null),
	};

	public static BitunixSections ToSection(this string boardCode)
	{
		if (boardCode.EqualsIgnoreCase(BoardCodes.Bitunix))
			return BitunixSections.Spot;
		if (boardCode.EqualsIgnoreCase(BoardCodes.BitunixFutures))
			return BitunixSections.Futures;
		throw new InvalidOperationException($"Unsupported Bitunix board code '{boardCode}'.");
	}

	public static decimal PrecisionToStep(this int precision)
		=> precision <= 0 ? 1m : 1m / (decimal)Math.Pow(10, precision);

	public static string ToWire(this decimal value)
		=> value.ToString(CultureInfo.InvariantCulture);

	public static decimal? ToDecimal(this string value)
		=> decimal.TryParse(value, NumberStyles.Any, CultureInfo.InvariantCulture, out var result)
			? result
			: null;

	public static DateTime FromMilliseconds(this long value)
		=> DateTimeOffset.FromUnixTimeMilliseconds(value).UtcDateTime;

	public static DateTime Floor(this DateTime value, TimeSpan interval)
	{
		var ticks = value.ToUniversalTime().Ticks;
		return new DateTime(ticks - ticks % interval.Ticks, DateTimeKind.Utc);
	}

	public static Sides ToSide(this string side)
		=> side.EqualsIgnoreCase("BUY") || side == "2" ? Sides.Buy : Sides.Sell;

	public static OrderTypes ToOrderType(this string type)
		=> type.EqualsIgnoreCase("MARKET") || type == "2" ? OrderTypes.Market : OrderTypes.Limit;

	public static OrderStates ToSpotOrderState(this string status) => status switch
	{
		"1" or "3" => OrderStates.Active,
		"2" or "4" or "7" => OrderStates.Done,
		_ => OrderStates.None,
	};

	public static OrderStates ToFuturesOrderState(this string status)
		=> status?.ToUpperInvariant() switch
		{
			"INIT" or "NEW" or "PART_FILLED" => OrderStates.Active,
			"CANCELED" or "FILLED" or "PART_FILLED_CANCELED" => OrderStates.Done,
			_ => OrderStates.None,
		};

	public static TimeInForce? ToTimeInForce(this string effect)
		=> effect?.ToUpperInvariant() switch
		{
			"IOC" => TimeInForce.CancelBalance,
			"FOK" => TimeInForce.MatchOrCancel,
			_ => null,
		};
}
