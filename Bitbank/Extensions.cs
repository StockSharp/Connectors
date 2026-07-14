namespace StockSharp.Bitbank;

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
			"bid" or "buy" => Sides.Buy,
			"ask" or "sell" => Sides.Sell,
			_ => throw new ArgumentOutOfRangeException(nameof(side), side, LocalizedStrings.InvalidValue),
		};
	}

	public static string ToNative(this OrderTypes? type)
	{
		return type switch
		{
			null or OrderTypes.Limit => "limit",
			OrderTypes.Market => "market",
			_ => throw new ArgumentOutOfRangeException(nameof(type), type, LocalizedStrings.InvalidValue),
		};
	}

	public static OrderTypes ToOrderType(this string type)
	{
		return type switch
		{
			"limit" => OrderTypes.Limit,
			"market" => OrderTypes.Market,
			_ => throw new ArgumentOutOfRangeException(nameof(type), type, LocalizedStrings.InvalidValue),
		};
	}

	public static readonly PairSet<TimeSpan, string> TimeFrames = new()
	{
		{ TimeSpan.FromMinutes(1), "1min" },
		{ TimeSpan.FromMinutes(5), "5min" },
		{ TimeSpan.FromMinutes(15), "15min" },
		{ TimeSpan.FromMinutes(30), "30min" },
		{ TimeSpan.FromHours(1), "1hour" },
		{ TimeSpan.FromHours(4), "4hour" },
		{ TimeSpan.FromHours(8), "8hour" },
		{ TimeSpan.FromHours(12), "12hour" },
		{ TimeSpan.FromDays(1), "1day" },
		{ TimeSpan.FromDays(7), "1week" },
		{ TimeSpan.FromTicks(TimeHelper.TicksPerMonth), "1month" },
	};

	public static string ToNative(this TimeSpan timeFrame)
	{
		return TimeFrames.TryGetValue(timeFrame) ?? throw new ArgumentOutOfRangeException(nameof(timeFrame), timeFrame, LocalizedStrings.InvalidValue);
	}

	public static TimeSpan ToTimeFrame(this string name)
	{
		return TimeFrames.TryGetKey2(name) ?? throw new ArgumentOutOfRangeException(nameof(name), name, LocalizedStrings.InvalidValue);
	}

	public static string ToSymbol(this SecurityId securityId)
	{
		return securityId.SecurityCode.ToLowerInvariant();
	}

	public static SecurityId ToStockSharp(this string symbol)
	{
		return new()
		{
			SecurityCode = symbol.ToUpperInvariant(),
			BoardCode = BoardCodes.Bitbank,
		};
	}

	public static OrderStates ToOrderState(this string status)
	{
		return (status?.ToUpperInvariant()) switch
		{
			"CANCELED_UNFILLED" or "CANCELED_PARTIALLY_FILLED" => OrderStates.Done,
			"UNFILLED" or "PARTIALLY_FILLED" => OrderStates.Active,
			"FULLY_FILLED" => OrderStates.Done,
			_ => throw new ArgumentOutOfRangeException(nameof(status), status, LocalizedStrings.InvalidValue),
		};
	}
}