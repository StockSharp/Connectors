namespace StockSharp.Upbit;

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
			"buy" or "bid" => Sides.Buy,
			"sell" or "ask" => Sides.Sell,
			_ => throw new ArgumentOutOfRangeException(nameof(side), side, LocalizedStrings.InvalidValue),
		};
	}

	public static Sides? ToOriginSide(this string side)
	{
		if (side.IsEmpty())
			return null;

		return side.ToLowerInvariant().ToSide();
	}

	public static readonly PairSet<TimeSpan, string> TimeFrames = new()
	{
		{ TimeSpan.FromMinutes(1), "1" },
		{ TimeSpan.FromMinutes(3), "3" },
		{ TimeSpan.FromMinutes(5), "5" },
		{ TimeSpan.FromMinutes(10), "10" },
		{ TimeSpan.FromMinutes(15), "15" },
		{ TimeSpan.FromMinutes(30), "30" },
		{ TimeSpan.FromHours(1), "60" },
		{ TimeSpan.FromHours(4), "240" },
		{ TimeSpan.FromDays(1), "days" },
		{ TimeSpan.FromDays(7), "weeks" },
		{ TimeSpan.FromTicks(TimeHelper.TicksPerMonth), "months" },
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
		return securityId.SecurityCode.Replace('/', '-').ToUpperInvariant();
	}

	public static SecurityId ToStockSharp(this string currency)
	{
		return new SecurityId
		{
			SecurityCode = currency.Replace('-', '/').ToUpperInvariant(),
			BoardCode = BoardCodes.Upbit,
		};
	}

	public static OrderStates? ToOrderState(this string state)
	{
		if (state.IsEmpty())
			return null;

		return state switch
		{
			"wait" => (OrderStates?)OrderStates.Active,
			"done" => (OrderStates?)OrderStates.Done,
			"cancel" => (OrderStates?)OrderStates.Done,
			_ => throw new ArgumentOutOfRangeException(nameof(state), state, LocalizedStrings.InvalidValue),
		};
	}
}