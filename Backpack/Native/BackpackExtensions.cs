namespace StockSharp.Backpack.Native;

static class BackpackExtensions
{
	public static readonly TimeSpan[] TimeFrames =
	[
		TimeSpan.FromSeconds(1),
		TimeSpan.FromMinutes(1),
		TimeSpan.FromMinutes(3),
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
		TimeSpan.FromDays(3),
		TimeSpan.FromDays(7),
		TimeSpan.FromDays(30),
	];

	public static string ToBackpackInterval(this TimeSpan timeFrame)
		=> timeFrame == TimeSpan.FromSeconds(1) ? "1s"
			: timeFrame == TimeSpan.FromMinutes(1) ? "1m"
			: timeFrame == TimeSpan.FromMinutes(3) ? "3m"
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
			: timeFrame == TimeSpan.FromDays(3) ? "3d"
			: timeFrame == TimeSpan.FromDays(7) ? "1w"
			: timeFrame == TimeSpan.FromDays(30) ? "1month"
			: throw new ArgumentOutOfRangeException(nameof(timeFrame), timeFrame,
				"Unsupported Backpack Exchange candle interval.");

	public static TimeSpan ToTimeFrame(this string interval)
		=> interval switch
		{
			"1s" => TimeSpan.FromSeconds(1),
			"1m" => TimeSpan.FromMinutes(1),
			"3m" => TimeSpan.FromMinutes(3),
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
			"3d" => TimeSpan.FromDays(3),
			"1w" => TimeSpan.FromDays(7),
			"1month" => TimeSpan.FromDays(30),
			_ => throw new InvalidDataException(
				$"Unknown Backpack Exchange candle interval '{interval}'."),
		};

	public static SecurityId ToStockSharp(this string symbol)
	{
		symbol = symbol.ThrowIfEmpty(nameof(symbol)).ToUpperInvariant();
		return new SecurityId
		{
			SecurityCode = symbol,
			BoardCode = symbol.EndsWith("_PERP", StringComparison.Ordinal) ||
				symbol.EndsWith("_IPERP", StringComparison.Ordinal)
				? BoardCodes.BackpackFutures
				: BoardCodes.Backpack,
		};
	}

	public static DateTime ToUtcMilliseconds(this long milliseconds)
		=> DateTimeOffset.FromUnixTimeMilliseconds(milliseconds).UtcDateTime;

	public static DateTime ToUtcMicroseconds(this long microseconds)
		=> DateTimeOffset.FromUnixTimeMilliseconds(microseconds / 1000).UtcDateTime;

	public static DateTime ToBackpackTime(this long timestamp)
		=> timestamp >= 100000000000000L
			? timestamp.ToUtcMicroseconds()
			: timestamp >= 100000000000L
				? timestamp.ToUtcMilliseconds()
				: DateTimeOffset.FromUnixTimeSeconds(timestamp).UtcDateTime;

	public static long ToUnixMilliseconds(this DateTime value)
		=> new DateTimeOffset(value.Kind == DateTimeKind.Utc
			? value
			: value.ToUniversalTime()).ToUnixTimeMilliseconds();

	public static long ToUnixSeconds(this DateTime value)
		=> new DateTimeOffset(value.Kind == DateTimeKind.Utc
			? value
			: value.ToUniversalTime()).ToUnixTimeSeconds();

	public static DateTime ToBackpackTime(this string value)
	{
		if (value.IsEmpty())
			return DateTime.MinValue;
		if (long.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture,
			out var numeric))
			return numeric.ToBackpackTime();
		return DateTime.Parse(value, CultureInfo.InvariantCulture,
			DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal);
	}

	public static string ToWire(this decimal value)
		=> value.ToString("0.############################", CultureInfo.InvariantCulture);

	public static string ToWire(this bool value) => value ? "true" : "false";

	public static string ToWire(this BackpackMarketTypes marketType)
		=> marketType switch
		{
			BackpackMarketTypes.Spot => "SPOT",
			BackpackMarketTypes.Perpetual => "PERP",
			BackpackMarketTypes.InversePerpetual => "IPERP",
			BackpackMarketTypes.Dated => "DATED",
			BackpackMarketTypes.Prediction => "PREDICTION",
			BackpackMarketTypes.RequestForQuote => "RFQ",
			_ => throw new ArgumentOutOfRangeException(nameof(marketType), marketType, null),
		};

	public static string ToWire(this BackpackSelfTradePreventions prevention)
		=> prevention switch
		{
			BackpackSelfTradePreventions.RejectTaker => "RejectTaker",
			BackpackSelfTradePreventions.RejectMaker => "RejectMaker",
			BackpackSelfTradePreventions.RejectBoth => "RejectBoth",
			_ => throw new ArgumentOutOfRangeException(nameof(prevention), prevention, null),
		};

	public static string ToWire(this BackpackTimeInForces timeInForce)
		=> timeInForce switch
		{
			BackpackTimeInForces.GoodTillCancelled => "GTC",
			BackpackTimeInForces.ImmediateOrCancel => "IOC",
			BackpackTimeInForces.FillOrKill => "FOK",
			_ => throw new ArgumentOutOfRangeException(nameof(timeInForce), timeInForce, null),
		};

	public static BackpackSides ToBackpack(this Sides side)
		=> side == Sides.Buy ? BackpackSides.Bid : BackpackSides.Ask;

	public static Sides ToStockSharp(this BackpackSides side)
		=> side == BackpackSides.Bid ? Sides.Buy : Sides.Sell;

	public static BackpackTimeInForces ToBackpack(this TimeInForce? timeInForce)
		=> timeInForce switch
		{
			TimeInForce.CancelBalance => BackpackTimeInForces.ImmediateOrCancel,
			TimeInForce.MatchOrCancel => BackpackTimeInForces.FillOrKill,
			_ => BackpackTimeInForces.GoodTillCancelled,
		};

	public static TimeInForce? ToStockSharp(this BackpackTimeInForces timeInForce)
		=> timeInForce switch
		{
			BackpackTimeInForces.ImmediateOrCancel => TimeInForce.CancelBalance,
			BackpackTimeInForces.FillOrKill => TimeInForce.MatchOrCancel,
			_ => null,
		};

	public static OrderStates ToStockSharp(this BackpackOrderStatuses status)
		=> status switch
		{
			BackpackOrderStatuses.New or BackpackOrderStatuses.PartiallyFilled or
				BackpackOrderStatuses.TriggerPending => OrderStates.Active,
			BackpackOrderStatuses.Filled or BackpackOrderStatuses.Cancelled or
				BackpackOrderStatuses.Expired => OrderStates.Done,
			BackpackOrderStatuses.TriggerFailed => OrderStates.Failed,
			_ => OrderStates.None,
		};

	public static OrderTypes ToStockSharp(this BackpackOrderTypes type)
		=> type == BackpackOrderTypes.Market ? OrderTypes.Market : OrderTypes.Limit;
}
