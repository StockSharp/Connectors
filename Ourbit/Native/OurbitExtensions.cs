namespace StockSharp.Ourbit.Native;

static class OurbitExtensions
{
	public static readonly IReadOnlyDictionary<TimeSpan, string> TimeFrames =
		new Dictionary<TimeSpan, string>
		{
			[TimeSpan.FromMinutes(1)] = "Min1",
			[TimeSpan.FromMinutes(5)] = "Min5",
			[TimeSpan.FromMinutes(15)] = "Min15",
			[TimeSpan.FromMinutes(30)] = "Min30",
			[TimeSpan.FromHours(1)] = "Min60",
			[TimeSpan.FromHours(4)] = "Hour4",
			[TimeSpan.FromDays(1)] = "Day1",
			[TimeSpan.FromDays(7)] = "Week1",
			[TimeSpan.FromDays(30)] = "Month1",
		};

	private static readonly IReadOnlyDictionary<TimeSpan, string> _spotIntervals =
		new Dictionary<TimeSpan, string>
		{
			[TimeSpan.FromMinutes(1)] = "1m",
			[TimeSpan.FromMinutes(5)] = "5m",
			[TimeSpan.FromMinutes(15)] = "15m",
			[TimeSpan.FromMinutes(30)] = "30m",
			[TimeSpan.FromHours(1)] = "60m",
			[TimeSpan.FromHours(4)] = "4h",
			[TimeSpan.FromDays(1)] = "1d",
			[TimeSpan.FromDays(7)] = "1W",
			[TimeSpan.FromDays(30)] = "1M",
		};

	public static string ToSpotInterval(this TimeSpan timeFrame)
		=> _spotIntervals.TryGetValue(timeFrame, out var value)
			? value
			: throw new ArgumentOutOfRangeException(nameof(timeFrame), timeFrame, LocalizedStrings.InvalidValue);

	public static string ToWsInterval(this TimeSpan timeFrame)
		=> TimeFrames.TryGetValue(timeFrame, out var value)
			? value
			: throw new ArgumentOutOfRangeException(nameof(timeFrame), timeFrame, LocalizedStrings.InvalidValue);

	public static TimeSpan FromWsInterval(this string interval)
		=> TimeFrames.FirstOrDefault(pair => pair.Value.EqualsIgnoreCase(interval)).Key is { } value && value != default
			? value
			: throw new InvalidDataException($"Unsupported Ourbit candle interval '{interval}'.");

	public static SecurityId ToStockSharp(this string symbol, OurbitSections section)
		=> new()
		{
			SecurityCode = symbol.ThrowIfEmpty(nameof(symbol)).ToUpperInvariant(),
			BoardCode = section.ToBoardCode(),
		};

	public static decimal PrecisionToStep(this int precision)
		=> precision <= 0 ? 1m : 1m / (decimal)Math.Pow(10, precision);

	public static string ToBoardCode(this OurbitSections section) => section switch
	{
		OurbitSections.Spot => BoardCodes.Ourbit,
		OurbitSections.Futures => BoardCodes.OurbitFutures,
		_ => throw new ArgumentOutOfRangeException(nameof(section), section, null),
	};

	public static OurbitSections ToSection(this string boardCode)
	{
		if (boardCode.EqualsIgnoreCase(BoardCodes.Ourbit))
			return OurbitSections.Spot;
		if (boardCode.EqualsIgnoreCase(BoardCodes.OurbitFutures))
			return OurbitSections.Futures;
		throw new InvalidOperationException($"Unsupported Ourbit board code '{boardCode}'.");
	}

	public static string ToFuturesSymbol(this string symbol)
	{
		symbol = symbol.ThrowIfEmpty(nameof(symbol)).ToUpperInvariant();
		if (symbol.Contains('_'))
			return symbol;
		foreach (var quote in new[] { "USDT", "USDC", "USD" })
		{
			if (symbol.EndsWith(quote, StringComparison.Ordinal) && symbol.Length > quote.Length)
				return symbol[..^quote.Length] + "_" + quote;
		}
		return symbol;
	}

	public static string FromFuturesSymbol(this string symbol)
		=> symbol?.Replace("_", string.Empty, StringComparison.Ordinal).ToUpperInvariant();

	public static DateTime FromMilliseconds(this long value)
		=> DateTimeOffset.FromUnixTimeMilliseconds(value).UtcDateTime;

	public static DateTime FromSeconds(this long value)
		=> DateTimeOffset.FromUnixTimeSeconds(value).UtcDateTime;

	public static string ToWire(this decimal value)
		=> value.ToString(CultureInfo.InvariantCulture);

	public static decimal? ToDecimal(this string value)
		=> decimal.TryParse(value, NumberStyles.Any, CultureInfo.InvariantCulture, out var result)
			? result
			: null;

	public static OrderStates ToOrderState(this string status) => status?.ToUpperInvariant() switch
	{
		"NEW" or "PARTIALLY_FILLED" => OrderStates.Active,
		"FILLED" => OrderStates.Done,
		"CANCELED" or "PARTIALLY_CANCELED" or "REJECTED" or "EXPIRED" => OrderStates.Done,
		_ => OrderStates.None,
	};

	public static OrderStates ToOrderState(this int state) => state switch
	{
		1 or 2 => OrderStates.Active,
		3 or 4 or 5 => OrderStates.Done,
		_ => OrderStates.None,
	};

	public static OrderTypes ToOrderType(this string type)
		=> type.EqualsIgnoreCase("MARKET") ? OrderTypes.Market : OrderTypes.Limit;

	public static OrderTypes ToOrderType(this int type)
		=> type == 5 ? OrderTypes.Market : OrderTypes.Limit;

	public static Sides ToSide(this int side) => side is 1 or 2 ? Sides.Buy : Sides.Sell;

	public static TimeInForce? ToTimeInForce(this int type) => type switch
	{
		3 => TimeInForce.CancelBalance,
		4 => TimeInForce.MatchOrCancel,
		_ => null,
	};
}
