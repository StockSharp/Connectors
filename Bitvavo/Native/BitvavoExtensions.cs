namespace StockSharp.Bitvavo.Native;

static class BitvavoExtensions
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
		TimeSpan.FromHours(8),
		TimeSpan.FromHours(12),
		TimeSpan.FromDays(1),
		TimeSpan.FromDays(7),
		TimeSpan.FromDays(30),
	];

	public static readonly TimeSpan[] StreamingTimeFrames = [.. TimeFrames.Take(11)];

	public static string ToBitvavoInterval(this TimeSpan timeFrame)
		=> timeFrame == TimeSpan.FromMinutes(1) ? "1m"
			: timeFrame == TimeSpan.FromMinutes(5) ? "5m"
			: timeFrame == TimeSpan.FromMinutes(15) ? "15m"
			: timeFrame == TimeSpan.FromMinutes(30) ? "30m"
			: timeFrame == TimeSpan.FromHours(1) ? "1h"
			: timeFrame == TimeSpan.FromHours(2) ? "2h"
			: timeFrame == TimeSpan.FromHours(4) ? "4h"
			: timeFrame == TimeSpan.FromHours(6) ? "6h"
			: timeFrame == TimeSpan.FromHours(8) ? "8h"
			: timeFrame == TimeSpan.FromHours(12) ? "12h"
			: timeFrame == TimeSpan.FromDays(1) ? "1d"
			: timeFrame == TimeSpan.FromDays(7) ? "1W"
			: timeFrame == TimeSpan.FromDays(30) ? "1M"
			: throw new ArgumentOutOfRangeException(nameof(timeFrame), timeFrame,
				"Unsupported Bitvavo candle interval.");

	public static TimeSpan ToTimeFrame(this string interval)
		=> interval switch
		{
			"1m" => TimeSpan.FromMinutes(1),
			"5m" => TimeSpan.FromMinutes(5),
			"15m" => TimeSpan.FromMinutes(15),
			"30m" => TimeSpan.FromMinutes(30),
			"1h" => TimeSpan.FromHours(1),
			"2h" => TimeSpan.FromHours(2),
			"4h" => TimeSpan.FromHours(4),
			"6h" => TimeSpan.FromHours(6),
			"8h" => TimeSpan.FromHours(8),
			"12h" => TimeSpan.FromHours(12),
			"1d" => TimeSpan.FromDays(1),
			"1W" => TimeSpan.FromDays(7),
			"1M" => TimeSpan.FromDays(30),
			_ => throw new ArgumentOutOfRangeException(nameof(interval), interval,
				"Unsupported Bitvavo candle interval."),
		};

	public static SecurityId ToStockSharp(this string symbol)
		=> new()
		{
			SecurityCode = symbol.ThrowIfEmpty(nameof(symbol)).ToUpperInvariant(),
			BoardCode = BoardCodes.Bitvavo,
		};

	public static DateTime FromMilliseconds(this long value)
		=> DateTimeOffset.FromUnixTimeMilliseconds(value).UtcDateTime;

	public static DateTime FromNanoseconds(this long value)
		=> DateTime.SpecifyKind(DateTime.UnixEpoch.AddTicks(value / 100),
			DateTimeKind.Utc);

	public static long ToMilliseconds(this DateTime value)
		=> new DateTimeOffset(value.Kind == DateTimeKind.Utc
			? value
			: value.ToUniversalTime()).ToUnixTimeMilliseconds();

	public static long ToAlignedMilliseconds(this DateTime value, TimeSpan timeFrame)
	{
		var milliseconds = value.ToMilliseconds();
		var interval = timeFrame.Ticks / TimeSpan.TicksPerMillisecond;
		return interval <= 0 ? milliseconds : milliseconds / interval * interval;
	}

	public static string ToWire(this decimal value)
		=> value.ToString(CultureInfo.InvariantCulture);

	public static Sides ToStockSharp(this BitvavoSides side)
		=> side == BitvavoSides.Buy ? Sides.Buy : Sides.Sell;

	public static BitvavoSides ToBitvavo(this Sides side)
		=> side == Sides.Buy ? BitvavoSides.Buy : BitvavoSides.Sell;

	public static OrderTypes ToStockSharp(this BitvavoOrderTypes? type)
		=> type is BitvavoOrderTypes.Market or BitvavoOrderTypes.StopLoss or
			BitvavoOrderTypes.TakeProfit
			? OrderTypes.Market
			: OrderTypes.Limit;

	public static OrderStates ToStockSharp(this BitvavoOrderStatuses? status)
		=> status switch
		{
			BitvavoOrderStatuses.New or BitvavoOrderStatuses.AwaitingTrigger or
				BitvavoOrderStatuses.PartiallyFilled => OrderStates.Active,
			BitvavoOrderStatuses.Canceled or BitvavoOrderStatuses.Expired or
				BitvavoOrderStatuses.Filled => OrderStates.Done,
			_ => OrderStates.None,
		};

	public static BitvavoTimeInForces ToBitvavo(this TimeInForce? timeInForce)
		=> timeInForce switch
		{
			TimeInForce.CancelBalance => BitvavoTimeInForces.ImmediateOrCancel,
			TimeInForce.MatchOrCancel => BitvavoTimeInForces.FillOrKill,
			_ => BitvavoTimeInForces.GoodTillCanceled,
		};

	public static TimeInForce? ToStockSharp(this BitvavoTimeInForces? timeInForce)
		=> timeInForce switch
		{
			BitvavoTimeInForces.ImmediateOrCancel => TimeInForce.CancelBalance,
			BitvavoTimeInForces.FillOrKill => TimeInForce.MatchOrCancel,
			_ => null,
		};

	public static SecurityStates? ToStockSharp(this BitvavoMarketStatuses? status)
		=> status switch
		{
			BitvavoMarketStatuses.Trading or BitvavoMarketStatuses.Auction =>
				SecurityStates.Trading,
			BitvavoMarketStatuses.Halted or BitvavoMarketStatuses.AuctionMatching or
				BitvavoMarketStatuses.CancelOnly => SecurityStates.Stoped,
			_ => null,
		};
}
