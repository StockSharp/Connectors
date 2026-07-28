namespace StockSharp.Coincall.Native;

static class CoincallExtensions
{
	private static readonly Dictionary<TimeSpan, string> _timeFrames =
		new()
		{
			[TimeSpan.FromMinutes(1)] = "m1",
			[TimeSpan.FromMinutes(5)] = "m5",
			[TimeSpan.FromMinutes(15)] = "m15",
			[TimeSpan.FromMinutes(30)] = "m30",
			[TimeSpan.FromHours(1)] = "h1",
			[TimeSpan.FromHours(4)] = "h4",
			[TimeSpan.FromDays(1)] = "d1",
			[TimeSpan.FromDays(7)] = "w1",
		};

	public static IEnumerable<TimeSpan> TimeFrames
		=> _timeFrames.Keys;

	public static string ToPeriod(this TimeSpan timeFrame)
		=> _timeFrames.TryGetValue(timeFrame, out var period)
			? period
			: throw new NotSupportedException(
				$"Coincall does not support the {timeFrame} candle " +
					"time frame.");

	public static TimeSpan ToTimeFrame(this string period)
		=> _timeFrames.FirstOrDefault(pair =>
			pair.Value.EqualsIgnoreCase(period)).Key is { } value &&
			value > TimeSpan.Zero
				? value
				: throw new NotSupportedException(
					$"Unknown Coincall candle period '{period}'.");

	public static string ToBoardCode(
		this CoincallProductTypes productType)
		=> productType switch
		{
			CoincallProductTypes.Options =>
				BoardCodes.CoincallOptions,
			CoincallProductTypes.Futures =>
				BoardCodes.CoincallFutures,
			_ => throw new ArgumentOutOfRangeException(
				nameof(productType), productType, null),
		};

	public static SecurityId ToStockSharp(
		this CoincallInstrument instrument)
		=> new()
		{
			SecurityCode = instrument.Symbol,
			BoardCode = instrument.ProductType.ToBoardCode(),
		};

	public static string ToWire(this decimal value)
		=> value.ToString(CultureInfo.InvariantCulture);
}
