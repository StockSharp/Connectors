namespace StockSharp.Xt.Native;

static class XtExtensions
{
	public static readonly IReadOnlyDictionary<TimeSpan, string> TimeFrames =
		new Dictionary<TimeSpan, string>
		{
			[TimeSpan.FromMinutes(1)] = "1m",
			[TimeSpan.FromMinutes(3)] = "3m",
			[TimeSpan.FromMinutes(5)] = "5m",
			[TimeSpan.FromMinutes(15)] = "15m",
			[TimeSpan.FromMinutes(30)] = "30m",
			[TimeSpan.FromHours(1)] = "1h",
			[TimeSpan.FromHours(2)] = "2h",
			[TimeSpan.FromHours(4)] = "4h",
			[TimeSpan.FromHours(6)] = "6h",
			[TimeSpan.FromHours(8)] = "8h",
			[TimeSpan.FromHours(12)] = "12h",
			[TimeSpan.FromDays(1)] = "1d",
			[TimeSpan.FromDays(3)] = "3d",
			[TimeSpan.FromDays(7)] = "1w",
			[TimeSpan.FromDays(30)] = "1M",
		};

	public static string ToXtInterval(this TimeSpan timeFrame)
		=> TimeFrames.TryGetValue(timeFrame, out var value)
			? value
			: throw new ArgumentOutOfRangeException(nameof(timeFrame), timeFrame,
				"Unsupported XT.COM candle interval.");

	public static string ToBoardCode(this XtSections section)
		=> section == XtSections.Futures ? BoardCodes.XtFutures : BoardCodes.Xt;

	public static XtSections ToSection(this string boardCode)
		=> boardCode.EqualsIgnoreCase(BoardCodes.XtFutures)
			? XtSections.Futures
			: XtSections.Spot;

	public static SecurityId ToStockSharp(this string symbol, XtSections section)
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
		=> side.EqualsIgnoreCase("BUY") || side.EqualsIgnoreCase("LONG")
			? Sides.Buy
			: Sides.Sell;

	public static OrderStates ToXtOrderState(this string status)
		=> status?.ToUpperInvariant() switch
		{
			"NEW" or "PARTIALLY_FILLED" or "PARTIALLY_CANCELED" => OrderStates.Active,
			"FILLED" or "CANCELED" or "EXPIRED" => OrderStates.Done,
			"REJECTED" or "FAILED" => OrderStates.Failed,
			_ => OrderStates.None,
		};

	public static DateTime Align(this DateTime time, TimeSpan timeFrame)
	{
		time = time.ToUniversalTime();
		return new DateTime(time.Ticks / timeFrame.Ticks * timeFrame.Ticks, DateTimeKind.Utc);
	}

	public static string ToWire(this XtPositionSides side)
		=> side switch
		{
			XtPositionSides.Long => "LONG",
			XtPositionSides.Short => "SHORT",
			_ => throw new ArgumentOutOfRangeException(nameof(side), side,
				"XT.COM futures orders require an explicit long or short position side."),
		};

	public static string ToWire(this XtOrderPolicies policy)
		=> policy switch
		{
			XtOrderPolicies.ImmediateOrCancel => "IOC",
			XtOrderPolicies.FillOrKill => "FOK",
			XtOrderPolicies.PostOnly => "GTX",
			_ => "GTC",
		};
}
