namespace StockSharp.DeltaExchangeIndia.Native;

static class DeltaExchangeIndiaExtensions
{
	public static readonly TimeSpan[] TimeFrames =
	[
		TimeSpan.FromMinutes(1),
		TimeSpan.FromMinutes(3),
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
	];

	public static string ToResolution(TimeSpan timeFrame)
		=> timeFrame == TimeSpan.FromMinutes(1) ? "1m" :
			timeFrame == TimeSpan.FromMinutes(3) ? "3m" :
			timeFrame == TimeSpan.FromMinutes(5) ? "5m" :
			timeFrame == TimeSpan.FromMinutes(15) ? "15m" :
			timeFrame == TimeSpan.FromMinutes(30) ? "30m" :
			timeFrame == TimeSpan.FromHours(1) ? "1h" :
			timeFrame == TimeSpan.FromHours(2) ? "2h" :
			timeFrame == TimeSpan.FromHours(4) ? "4h" :
			timeFrame == TimeSpan.FromHours(6) ? "6h" :
			timeFrame == TimeSpan.FromHours(12) ? "12h" :
			timeFrame == TimeSpan.FromDays(1) ? "1d" :
			timeFrame == TimeSpan.FromDays(7) ? "1w" :
			throw new ArgumentOutOfRangeException(
				nameof(timeFrame), timeFrame,
				"Unsupported Delta Exchange India candle time frame.");

	public static TimeSpan FromResolution(string resolution)
		=> resolution?.ToLowerInvariant() switch
		{
			"1m" => TimeSpan.FromMinutes(1),
			"3m" => TimeSpan.FromMinutes(3),
			"5m" => TimeSpan.FromMinutes(5),
			"15m" => TimeSpan.FromMinutes(15),
			"30m" => TimeSpan.FromMinutes(30),
			"1h" => TimeSpan.FromHours(1),
			"2h" => TimeSpan.FromHours(2),
			"4h" => TimeSpan.FromHours(4),
			"6h" => TimeSpan.FromHours(6),
			"12h" => TimeSpan.FromHours(12),
			"1d" => TimeSpan.FromDays(1),
			"1w" => TimeSpan.FromDays(7),
			_ => throw new ArgumentOutOfRangeException(
				nameof(resolution), resolution,
				"Unsupported Delta Exchange India candle resolution."),
		};

	public static string ToTimeInForce(TimeInForce value)
		=> value switch
		{
			TimeInForce.PutInQueue => "gtc",
			TimeInForce.CancelBalance => "ioc",
			TimeInForce.MatchOrCancel => "fok",
			_ => throw new ArgumentOutOfRangeException(
				nameof(value), value, LocalizedStrings.InvalidValue),
		};

	public static SecurityId ToStockSharp(this DeltaProduct product)
		=> new()
		{
			SecurityCode = product.Symbol.ToUpperInvariant(),
			BoardCode = BoardCodes.DeltaExchangeIndia,
		};
}
