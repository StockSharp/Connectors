namespace StockSharp.OrderlyNetwork.Native;

static class OrderlyNetworkExtensions
{
	public static readonly TimeSpan[] TimeFrames =
	[
		TimeSpan.FromMinutes(1),
		TimeSpan.FromMinutes(5),
		TimeSpan.FromMinutes(15),
		TimeSpan.FromMinutes(30),
		TimeSpan.FromHours(1),
		TimeSpan.FromHours(4),
		TimeSpan.FromHours(12),
		TimeSpan.FromDays(1),
		TimeSpan.FromDays(7),
		TimeSpan.FromDays(30),
	];

	public static string ToOrderlyInterval(this TimeSpan timeFrame)
		=> timeFrame == TimeSpan.FromMinutes(1) ? "1m"
			: timeFrame == TimeSpan.FromMinutes(5) ? "5m"
			: timeFrame == TimeSpan.FromMinutes(15) ? "15m"
			: timeFrame == TimeSpan.FromMinutes(30) ? "30m"
			: timeFrame == TimeSpan.FromHours(1) ? "1h"
			: timeFrame == TimeSpan.FromHours(4) ? "4h"
			: timeFrame == TimeSpan.FromHours(12) ? "12h"
			: timeFrame == TimeSpan.FromDays(1) ? "1d"
			: timeFrame == TimeSpan.FromDays(7) ? "1w"
			: timeFrame == TimeSpan.FromDays(30) ? "1mon"
			: throw new ArgumentOutOfRangeException(nameof(timeFrame), timeFrame,
				"Unsupported Orderly Network candle interval.");

	public static TimeSpan ToOrderlyTimeFrame(this string interval)
		=> interval switch
		{
			"1m" => TimeSpan.FromMinutes(1),
			"5m" => TimeSpan.FromMinutes(5),
			"15m" => TimeSpan.FromMinutes(15),
			"30m" => TimeSpan.FromMinutes(30),
			"1h" => TimeSpan.FromHours(1),
			"4h" => TimeSpan.FromHours(4),
			"12h" => TimeSpan.FromHours(12),
			"1d" => TimeSpan.FromDays(1),
			"1w" => TimeSpan.FromDays(7),
			"1mon" => TimeSpan.FromDays(30),
			_ => throw new InvalidDataException(
				$"Unknown Orderly Network candle interval '{interval}'."),
		};

	public static SecurityId ToStockSharp(this string symbol)
		=> new()
		{
			SecurityCode = symbol.ThrowIfEmpty(nameof(symbol)).Trim(),
			BoardCode = BoardCodes.OrderlyNetwork,
		};

	public static DateTime FromOrderlyMilliseconds(this long milliseconds)
	{
		try
		{
			return DateTime.SpecifyKind(DateTime.UnixEpoch.AddMilliseconds(milliseconds),
				DateTimeKind.Utc);
		}
		catch (ArgumentOutOfRangeException error)
		{
			throw new InvalidDataException(
				"Orderly Network returned an invalid Unix timestamp.", error);
		}
	}

	public static long ToOrderlyMilliseconds(this DateTime value)
		=> checked((long)(value.EnsureOrderlyUtc() - DateTime.UnixEpoch)
			.TotalMilliseconds);

	public static DateTime EnsureOrderlyUtc(this DateTime value)
		=> value.Kind switch
		{
			DateTimeKind.Utc => value,
			DateTimeKind.Local => value.ToUniversalTime(),
			_ => DateTime.SpecifyKind(value, DateTimeKind.Utc),
		};

	public static string ToWire(this decimal value)
		=> value.ToString("0.############################",
			CultureInfo.InvariantCulture);

	public static OrderlyNetworkSides ToOrderly(this Sides side)
		=> side == Sides.Buy ? OrderlyNetworkSides.Buy : OrderlyNetworkSides.Sell;

	public static Sides ToStockSharp(this OrderlyNetworkSides side)
		=> side == OrderlyNetworkSides.Buy ? Sides.Buy : Sides.Sell;

	public static OrderStates ToStockSharp(this OrderlyNetworkOrderStatuses status)
		=> status switch
		{
			OrderlyNetworkOrderStatuses.New or
			OrderlyNetworkOrderStatuses.PartialFilled or
			OrderlyNetworkOrderStatuses.Incomplete or
			OrderlyNetworkOrderStatuses.PendingCancel => OrderStates.Active,
			OrderlyNetworkOrderStatuses.Filled or
			OrderlyNetworkOrderStatuses.Cancelled or
			OrderlyNetworkOrderStatuses.Completed or
			OrderlyNetworkOrderStatuses.Expired => OrderStates.Done,
			OrderlyNetworkOrderStatuses.Rejected => OrderStates.Failed,
			_ => OrderStates.None,
		};

	public static OrderTypes ToStockSharp(this OrderlyNetworkOrderTypes type)
		=> type == OrderlyNetworkOrderTypes.Market
			? OrderTypes.Market
			: OrderTypes.Limit;

	public static TimeInForce? ToTimeInForce(this OrderlyNetworkOrderTypes type)
		=> type switch
		{
			OrderlyNetworkOrderTypes.Ioc => TimeInForce.CancelBalance,
			OrderlyNetworkOrderTypes.Fok => TimeInForce.MatchOrCancel,
			_ => null,
		};

	public static OrderlyNetworkOrderTypes ToOrderly(this OrderTypes type,
		TimeInForce? timeInForce, bool isPostOnly)
	{
		if (type == OrderTypes.Market)
			return OrderlyNetworkOrderTypes.Market;
		if (isPostOnly)
			return OrderlyNetworkOrderTypes.PostOnly;
		return timeInForce switch
		{
			TimeInForce.CancelBalance => OrderlyNetworkOrderTypes.Ioc,
			TimeInForce.MatchOrCancel => OrderlyNetworkOrderTypes.Fok,
			_ => OrderlyNetworkOrderTypes.Limit,
		};
	}
}
