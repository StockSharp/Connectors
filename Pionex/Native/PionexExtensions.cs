namespace StockSharp.Pionex.Native;

static class PionexExtensions
{
	public static readonly IReadOnlyDictionary<TimeSpan, string> TimeFrames =
		new Dictionary<TimeSpan, string>
		{
			[TimeSpan.FromMinutes(1)] = "1M",
			[TimeSpan.FromMinutes(5)] = "5M",
			[TimeSpan.FromMinutes(15)] = "15M",
			[TimeSpan.FromMinutes(30)] = "30M",
			[TimeSpan.FromHours(1)] = "60M",
			[TimeSpan.FromHours(4)] = "4H",
			[TimeSpan.FromHours(8)] = "8H",
			[TimeSpan.FromHours(12)] = "12H",
			[TimeSpan.FromDays(1)] = "1D",
		};

	public static string ToPionexInterval(this TimeSpan timeFrame)
		=> TimeFrames.TryGetValue(timeFrame, out var value)
			? value
			: throw new ArgumentOutOfRangeException(nameof(timeFrame), timeFrame,
				"Unsupported Pionex candle interval.");

	public static string ToBoardCode(this PionexSections section)
		=> section == PionexSections.Futures ? BoardCodes.PionexFutures : BoardCodes.Pionex;

	public static PionexSections ToSection(this string boardCode)
		=> boardCode.EqualsIgnoreCase(BoardCodes.PionexFutures)
			? PionexSections.Futures
			: PionexSections.Spot;

	public static SecurityId ToStockSharp(this string symbol, PionexSections section)
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

	public static OrderStates ToPionexOrderState(this string status)
		=> status?.ToUpperInvariant() switch
		{
			"OPEN" => OrderStates.Active,
			"CLOSED" => OrderStates.Done,
			"REJECTED" or "FAILED" => OrderStates.Failed,
			_ => OrderStates.None,
		};

	public static DateTime Align(this DateTime time, TimeSpan timeFrame)
	{
		time = time.ToUniversalTime();
		return new DateTime(time.Ticks / timeFrame.Ticks * timeFrame.Ticks, DateTimeKind.Utc);
	}

	public static string ToWire(this PionexPositionSides side)
		=> side switch
		{
			PionexPositionSides.Long => "LONG",
			PionexPositionSides.Short => "SHORT",
			_ => "BOTH",
		};

	public static string ToWire(this PionexOrderPolicies policy, OrderTypes orderType)
		=> policy switch
		{
			PionexOrderPolicies.ImmediateOrCancel => "IOC",
			PionexOrderPolicies.FillOrKill => "FOK",
			PionexOrderPolicies.PostOnly => "POSTONLY",
			_ => orderType == OrderTypes.Market ? "MARKET_QTY" : "LIMIT",
		};
}
