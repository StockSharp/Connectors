namespace StockSharp.LBank;

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
		return side switch
		{
			"buy" => Sides.Buy,
			"sell" => Sides.Sell,
			_ => throw new ArgumentOutOfRangeException(nameof(side), side, LocalizedStrings.InvalidValue),
		};
	}

	public static readonly PairSet<TimeSpan, Tuple<string, string>> TimeFrames = new()
	{
		{ TimeSpan.FromMinutes(1), Tuple.Create("1min", "minute1") },
		{ TimeSpan.FromMinutes(5), Tuple.Create("5min", "minute5") },
		{ TimeSpan.FromMinutes(15), Tuple.Create("15min", "minute15") },
		{ TimeSpan.FromMinutes(30), Tuple.Create("30min", "minute30") },
		{ TimeSpan.FromHours(1), Tuple.Create("1hr", "hour1") },
		{ TimeSpan.FromHours(4), Tuple.Create("4hr", "hour4") },
		//{ TimeSpan.FromHours(8), Tuple.Create("8hr", "hour8") },
		//{ TimeSpan.FromHours(12), Tuple.Create("12hr", "hour12") },
		{ TimeSpan.FromDays(1), Tuple.Create("day", "day1") },
		{ TimeSpan.FromDays(7), Tuple.Create("week", "week1") },
		{ TimeSpan.FromTicks(TimeHelper.TicksPerMonth), Tuple.Create("month", "month1") },
	};

	private static readonly Lazy<Dictionary<string, TimeSpan>> _timeFramesByName = new(() =>
	{
		var dict = new Dictionary<string, TimeSpan>(StringComparer.InvariantCultureIgnoreCase);

		foreach (var pair in TimeFrames)
		{
			dict.Add(pair.Value.Item1, pair.Key);
			dict.Add(pair.Value.Item2, pair.Key);
		}

		return dict;
	});

	public static string ToNative(this TimeSpan timeFrame, bool isSocket)
	{
		if (!TimeFrames.TryGetValue(timeFrame, out var tuple))
			throw new ArgumentOutOfRangeException(nameof(timeFrame), timeFrame, LocalizedStrings.InvalidValue);

		return isSocket ? tuple.Item1 : tuple.Item2;
	}

	public static TimeSpan ToTimeFrame(this string name)
	{
		if (!_timeFramesByName.Value.TryGetValue(name, out var timeFrame))
			throw new ArgumentOutOfRangeException(nameof(name), name, LocalizedStrings.InvalidValue);

		return timeFrame;
	}

	public static string ToCurrency(this SecurityId securityId)
	{
		return securityId.SecurityCode.Replace('/', '_').ToLowerInvariant();
	}

	public static SecurityId ToStockSharp(this string currency)
	{
		return new SecurityId
		{
			SecurityCode = currency.Replace('_', '/').ToUpperInvariant(),
			BoardCode = BoardCodes.LBank,
		};
	}

	public static decimal GetBalance(this Order order)
	{
		if (order == null)
			throw new ArgumentNullException(nameof(order));

		return (decimal)(order.Volume - order.DealVolume);
	}

	public static OrderStates? ToOrderState(this int status)
	{
		switch (status)
		{
			case 0: // on trading
			case 1: // filled partially
			case 4: // Cancelling
				return OrderStates.Active;

			case -1: // Cancelled
			case 2: // Filled totally
			case 3: // filled partially and cancelled
				return OrderStates.Done;

			default:
				throw new ArgumentOutOfRangeException(nameof(status), status, LocalizedStrings.InvalidValue);
		}
	}
}