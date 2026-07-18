namespace StockSharp.Weex.Native;

static class WeexExtensions
{
	public static readonly IReadOnlyDictionary<TimeSpan, string> TimeFrames =
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
			[TimeSpan.FromHours(8)] = "8h",
			[TimeSpan.FromHours(12)] = "12h",
			[TimeSpan.FromDays(1)] = "1d",
			[TimeSpan.FromDays(7)] = "1w",
			[TimeSpan.FromDays(30)] = "1M",
		};

	public static string ToNative(this TimeSpan timeFrame)
		=> TimeFrames.TryGetValue(timeFrame, out var value)
			? value
			: throw new ArgumentOutOfRangeException(nameof(timeFrame), timeFrame, "Unsupported WEEX candle interval.");

	public static string ToBoardCode(this WeexSections section)
		=> section == WeexSections.Futures ? BoardCodes.WeexFutures : BoardCodes.Weex;

	public static WeexSections ToSection(this string boardCode)
		=> boardCode.EqualsIgnoreCase(BoardCodes.WeexFutures) ? WeexSections.Futures : WeexSections.Spot;

	public static SecurityId ToStockSharp(this string securityCode, WeexSections section)
		=> new()
		{
			SecurityCode = securityCode?.ToUpperInvariant(),
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

	public static Sides ToStockSharp(this WeexSides side)
		=> side == WeexSides.Buy ? Sides.Buy : Sides.Sell;

	public static WeexSides ToNative(this Sides side)
		=> side == Sides.Buy ? WeexSides.Buy : WeexSides.Sell;

	public static Sides? ToStockSharpNullable(this WeexPositionSides side)
		=> side switch
		{
			WeexPositionSides.Long => Sides.Buy,
			WeexPositionSides.Short => Sides.Sell,
			_ => null,
		};

	public static OrderStates ToStockSharp(this WeexOrderStatuses status)
		=> status switch
		{
			WeexOrderStatuses.New or WeexOrderStatuses.Pending or WeexOrderStatuses.Untriggered or
				WeexOrderStatuses.PartiallyFilled or WeexOrderStatuses.Canceling => OrderStates.Active,
			WeexOrderStatuses.Filled or WeexOrderStatuses.Canceled or WeexOrderStatuses.Expired => OrderStates.Done,
			WeexOrderStatuses.Rejected => OrderStates.Failed,
			_ => OrderStates.None,
		};

	public static decimal PrecisionToStep(this int precision)
		=> precision <= 0 ? 1m : 1m / (decimal)Math.Pow(10, precision);
}
