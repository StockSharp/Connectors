namespace StockSharp.Kucoin.Native.Futures;

using StockSharp.Kucoin.Native.Futures.Model;

static class Extensions
{
	public static string ToNative(this Sides side)
	{
		return side switch
		{
			Sides.Buy => "buy",
			Sides.Sell => "sell",
			_ => throw new ArgumentOutOfRangeException(nameof(side), side, LocalizedStrings.InvalidValue),
		};
	}

	public static Sides ToSide(this string side)
	{
		return (side?.ToLowerInvariant()) switch
		{
			"buy" => Sides.Buy,
			"sell" => Sides.Sell,
			_ => throw new ArgumentOutOfRangeException(nameof(side), side, LocalizedStrings.InvalidValue),
		};
	}

	public static string ToNative(this OrderTypes? type)
	{
		return type switch
		{
			null => null,
			OrderTypes.Limit => "limit",
			OrderTypes.Market => "market",
			OrderTypes.Conditional => "stop",// TODO
			_ => throw new ArgumentOutOfRangeException(nameof(type), type, LocalizedStrings.InvalidValue),
		};
	}

	public static OrderTypes? ToOrderType(this string type)
	{
		if (type.IsEmpty())
			return null;

		return type.ToLowerInvariant() switch
		{
			"limit" => (OrderTypes?)OrderTypes.Limit,
			"market" => (OrderTypes?)OrderTypes.Market,
			"limit_stop" or "market_stop" => (OrderTypes?)OrderTypes.Conditional,
			_ => throw new ArgumentOutOfRangeException(nameof(type), type, LocalizedStrings.InvalidValue),
		};
	}

	public static string ToNative(this TimeInForce? tif, DateTime? tillDate, out long? cancelAfter)
	{
		cancelAfter = null;

		switch (tif)
		{
			case null:
				return null;
			case TimeInForce.PutInQueue:
			{
				if (tillDate == null)
					return "GTC";

				cancelAfter = (long)(tillDate.Value - DateTime.UtcNow).TotalSeconds;
				return "GTT";
			}
			case TimeInForce.MatchOrCancel:
				return "FOK";
			case TimeInForce.CancelBalance:
				return "IOC";
			default:
				throw new ArgumentOutOfRangeException(nameof(tif), tif, LocalizedStrings.InvalidValue);
		}
	}

	public static TimeInForce? ToTimeInForce(this string tif, DateTime createdAt, long? cancelAfter, out DateTime? tillDate)
	{
		tillDate = null;

		if (tif.IsEmpty())
			return null;

		switch (tif.ToUpperInvariant())
		{
			case "GTC":
				return TimeInForce.PutInQueue;
			case "GTT":
				tillDate = createdAt.AddSeconds(cancelAfter ?? 0);
				return TimeInForce.PutInQueue;
			case "FOK":
				return TimeInForce.MatchOrCancel;
			case "IOC":
				return TimeInForce.CancelBalance;
			default:
				throw new ArgumentOutOfRangeException(nameof(tif), tif, LocalizedStrings.InvalidValue);
		}
	}

	public static readonly PairSet<TimeSpan, string> TimeFrames = new()
	{
		{ TimeSpan.FromMinutes(1), "1min" },
		{ TimeSpan.FromMinutes(3), "3min" },
		{ TimeSpan.FromMinutes(5), "5min" },
		{ TimeSpan.FromMinutes(15), "15min" },
		{ TimeSpan.FromMinutes(30), "30min" },
		{ TimeSpan.FromHours(1), "1hour" },
		{ TimeSpan.FromHours(2), "2hour" },
		{ TimeSpan.FromHours(4), "4hour" },
		{ TimeSpan.FromHours(6), "6hour" },
		{ TimeSpan.FromHours(8), "8hour" },
		{ TimeSpan.FromHours(12), "12hour" },
		{ TimeSpan.FromDays(1), "1day" },
		{ TimeSpan.FromDays(7), "1week" },
	};

	public static string ToNative(this TimeSpan timeFrame)
		=> TimeFrames.TryGetValue(timeFrame) ?? throw new ArgumentOutOfRangeException(nameof(timeFrame), timeFrame, LocalizedStrings.InvalidValue);

	public static TimeSpan ToTimeFrame(this string name)
		=> TimeFrames.TryGetKey2(name) ?? throw new ArgumentOutOfRangeException(nameof(name), name, LocalizedStrings.InvalidValue);

	public static decimal GetBalance(this Order order)
	{
		if (order == null)
			throw new ArgumentNullException(nameof(order));

		return (decimal)(order.Size - order.DealSize).Value;
	}

	public static OrderStates? ToOrderState(this string status)
		=> status.ToLowerInvariant() switch
		{
			"done" => OrderStates.Done,
			_ => OrderStates.Active,
		};
}