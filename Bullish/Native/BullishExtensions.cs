namespace StockSharp.Bullish.Native;

static class BullishExtensions
{
	public static readonly IReadOnlyDictionary<TimeSpan, string> TimeFrames =
		new Dictionary<TimeSpan, string>
		{
			[TimeSpan.FromMinutes(1)] = "1m",
			[TimeSpan.FromMinutes(5)] = "5m",
			[TimeSpan.FromMinutes(30)] = "30m",
			[TimeSpan.FromHours(1)] = "1h",
			[TimeSpan.FromHours(6)] = "6h",
			[TimeSpan.FromHours(12)] = "12h",
			[TimeSpan.FromDays(1)] = "1d",
		};

	public static string ToBullishBucket(this TimeSpan timeFrame)
		=> TimeFrames.TryGetValue(timeFrame, out var value)
			? value
			: throw new ArgumentOutOfRangeException(nameof(timeFrame), timeFrame,
				"Unsupported Bullish candle interval.");

	public static string ToBoardCode(this BullishSections section)
		=> section == BullishSections.Derivatives
			? BoardCodes.BullishDerivatives
			: BoardCodes.Bullish;

	public static BullishSections ToSection(this string boardCode)
		=> boardCode.EqualsIgnoreCase(BoardCodes.BullishDerivatives)
			? BullishSections.Derivatives
			: BullishSections.Spot;

	public static BullishSections ToSectionByMarketType(this string marketType)
		=> marketType.EqualsIgnoreCase("SPOT")
			? BullishSections.Spot
			: BullishSections.Derivatives;

	public static SecurityTypes ToSecurityType(this string marketType)
		=> marketType?.ToUpperInvariant() switch
		{
			"SPOT" => SecurityTypes.CryptoCurrency,
			"OPTION" => SecurityTypes.Option,
			_ => SecurityTypes.Future,
		};

	public static OptionTypes? ToOptionType(this string optionType)
		=> optionType?.ToUpperInvariant() switch
		{
			"CALL" => OptionTypes.Call,
			"PUT" => OptionTypes.Put,
			_ => null,
		};

	public static SecurityId ToStockSharp(this string symbol, BullishSections section)
		=> new()
		{
			SecurityCode = symbol?.ToUpperInvariant(),
			BoardCode = section.ToBoardCode(),
		};

	public static decimal? ToDecimal(this string value)
		=> decimal.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out var number)
			? number
			: null;

	public static string ToWire(this decimal value)
		=> value.ToString(CultureInfo.InvariantCulture);

	public static DateTime? ToUtcDateTime(this string value)
		=> DateTimeOffset.TryParse(value, CultureInfo.InvariantCulture,
			DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal, out var time)
			? time.UtcDateTime
			: null;

	public static DateTime ToUtcTime(this string timestamp, string dateTime = null)
	{
		if (long.TryParse(timestamp, NumberStyles.Integer, CultureInfo.InvariantCulture, out var milliseconds))
			return DateTimeOffset.FromUnixTimeMilliseconds(milliseconds).UtcDateTime;
		return dateTime.ToUtcDateTime() ?? DateTime.UtcNow;
	}

	public static string ToWireTime(this DateTime value)
		=> value.ToUniversalTime().ToString("yyyy-MM-dd'T'HH:mm:ss.fff'Z'", CultureInfo.InvariantCulture);

	public static decimal PrecisionToStep(this int precision)
		=> precision <= 0 ? 1m : 1m / (decimal)Math.Pow(10, precision);

	public static Sides ToStockSharpSide(this string side)
		=> side.EqualsIgnoreCase("BUY") ? Sides.Buy : Sides.Sell;

	public static OrderStates ToBullishOrderState(this string status)
		=> status?.ToUpperInvariant() switch
		{
			"OPEN" => OrderStates.Active,
			"CLOSED" or "CANCELLED" => OrderStates.Done,
			"REJECTED" => OrderStates.Failed,
			_ => OrderStates.None,
		};

	public static OrderTypes ToStockSharpOrderType(this string orderType)
		=> orderType?.ToUpperInvariant() switch
		{
			"MKT" or "MARKET" => OrderTypes.Market,
			"STOP_LIMIT" => OrderTypes.Conditional,
			_ => OrderTypes.Limit,
		};

	public static TimeInForce? ToStockSharpTimeInForce(this string value)
		=> value?.ToUpperInvariant() switch
		{
			"IOC" => TimeInForce.CancelBalance,
			"FOK" => TimeInForce.MatchOrCancel,
			"GTC" => TimeInForce.PutInQueue,
			_ => null,
		};

	public static DateTime Align(this DateTime time, TimeSpan timeFrame)
	{
		time = time.ToUniversalTime();
		return new DateTime(time.Ticks / timeFrame.Ticks * timeFrame.Ticks, DateTimeKind.Utc);
	}
}
