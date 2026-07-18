namespace StockSharp.Phemex.Native;

static class PhemexExtensions
{
	public static readonly IReadOnlyDictionary<TimeSpan, string> TimeFrames =
		new Dictionary<TimeSpan, string>
		{
			[TimeSpan.FromMinutes(1)] = "60",
			[TimeSpan.FromMinutes(5)] = "300",
			[TimeSpan.FromMinutes(15)] = "900",
			[TimeSpan.FromMinutes(30)] = "1800",
			[TimeSpan.FromHours(1)] = "3600",
			[TimeSpan.FromHours(4)] = "14400",
			[TimeSpan.FromDays(1)] = "86400",
			[TimeSpan.FromDays(7)] = "604800",
			[TimeSpan.FromDays(30)] = "2592000",
			[TimeSpan.FromDays(90)] = "7776000",
			[TimeSpan.FromDays(360)] = "31104000",
		};

	public static string ToPhemexInterval(this TimeSpan timeFrame)
		=> TimeFrames.TryGetValue(timeFrame, out var value)
			? value
			: throw new ArgumentOutOfRangeException(nameof(timeFrame), timeFrame,
				"Unsupported Phemex candle interval.");

	public static string ToBoardCode(this PhemexSections section)
		=> section == PhemexSections.Futures ? BoardCodes.PhemexFutures : BoardCodes.Phemex;

	public static PhemexSections ToSection(this string boardCode)
		=> boardCode.EqualsIgnoreCase(BoardCodes.PhemexFutures)
			? PhemexSections.Futures
			: PhemexSections.Spot;

	public static SecurityId ToStockSharp(this string symbol, PhemexSections section)
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

	public static DateTime ToUtcTime(this long milliseconds)
		=> DateTimeOffset.FromUnixTimeMilliseconds(milliseconds).UtcDateTime;

	public static DateTime ToUtcTime(this string value)
		=> long.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var milliseconds)
			? milliseconds.ToUtcTime()
			: DateTime.UtcNow;

	public static long ToUnixMilliseconds(this DateTime value)
		=> new DateTimeOffset(value.ToUniversalTime()).ToUnixTimeMilliseconds();

	public static decimal PrecisionToStep(this int precision)
		=> precision <= 0 ? 1m : 1m / (decimal)Math.Pow(10, precision);

	public static Sides ToStockSharpSide(this string side)
		=> side.EqualsIgnoreCase("BUY") || side.EqualsIgnoreCase("LONG") || side == "1"
			? Sides.Buy
			: Sides.Sell;

	public static OrderStates ToPhemexOrderState(this string status)
		=> status?.ToUpperInvariant() switch
		{
			"CREATED" or "INIT" or "NEW" or "PARTIALLY_FILLED" or "UNTRIGGERED" => OrderStates.Active,
			"FILLED" or "CANCELED" or "CANCELLED" or "TRIGGERED" or "DEACTIVATED" => OrderStates.Done,
			"REJECTED" or "FAILED" => OrderStates.Failed,
			_ => OrderStates.None,
		};

	public static DateTime Align(this DateTime time, TimeSpan timeFrame)
	{
		time = time.ToUniversalTime();
		return new DateTime(time.Ticks / timeFrame.Ticks * timeFrame.Ticks, DateTimeKind.Utc);
	}

	public static string ToWire(this PhemexPositionSides side)
		=> side switch
		{
			PhemexPositionSides.Long => "Long",
			PhemexPositionSides.Short => "Short",
			_ => "Merged",
		};

	public static string ToPhemexTimeInForce(this PhemexOrderPolicies policy)
		=> policy switch
		{
			PhemexOrderPolicies.ImmediateOrCancel => "ImmediateOrCancel",
			PhemexOrderPolicies.FillOrKill => "FillOrKill",
			PhemexOrderPolicies.PostOnly => "PostOnly",
			_ => "GoodTillCancel",
		};
}
