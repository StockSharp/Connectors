namespace StockSharp.CoinEx.Native;

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
		return side?.ToLowerInvariant() switch
		{
			"buy" => Sides.Buy,
			"sell" => Sides.Sell,
			_ => throw new ArgumentOutOfRangeException(nameof(side), side, LocalizedStrings.InvalidValue),
		};
	}

	public static string ToOrderType(this OrderRegisterMessage regMsg)
	{
		if (regMsg is null)
			throw new ArgumentNullException(nameof(regMsg));

		if (regMsg.PostOnly == true)
			return "maker_only";

		if (regMsg.OrderType == OrderTypes.Market)
			return "market";

		return regMsg.TimeInForce switch
		{
			null or TimeInForce.PutInQueue => "limit",
			TimeInForce.MatchOrCancel => "fok",
			TimeInForce.CancelBalance => "ioc",
			_ => throw new ArgumentOutOfRangeException(regMsg.TimeInForce.To<string>()),
		};
	}

	public static OrderTypes? ToOrderType(this string type, out bool? postOnly, out TimeInForce? tif)
	{
		postOnly = null;
		tif = null;

		type = type?.ToLowerInvariant();

		if (type.IsEmpty())
			return null;

		if (type == "maker_only")
		{
			postOnly = true;
			return null;
		}

		if (type == "fok")
		{
			tif = TimeInForce.MatchOrCancel;
			return null;
		}
		else if (type == "ioc")
		{
			tif = TimeInForce.CancelBalance;
			return null;
		}

		return type switch
		{
			"limit" => OrderTypes.Limit,
			"market" => OrderTypes.Market,
			_ => throw new ArgumentOutOfRangeException(nameof(type), type, LocalizedStrings.InvalidValue),
		};
	}

	public static OrderStates ToOrderState(this string status)
	{
		return status?.ToLowerInvariant() switch
		{
			"put" or "update" => OrderStates.Active,
			"finish" => OrderStates.Done,
			_ => throw new ArgumentOutOfRangeException(nameof(status), status, LocalizedStrings.InvalidValue),
		};
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
		{ TimeSpan.FromHours(12), "12hour" },
		{ TimeSpan.FromDays(1), "1day" },
		{ TimeSpan.FromDays(3), "3day" },
		{ TimeSpan.FromDays(7), "1week" },
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
		return securityId.SecurityCode.ToUpperInvariant();
	}

	public static SecurityId ToStockSharp(this string symbol, string boardCode)
	{
		return new()
		{
			SecurityCode = symbol.ToUpperInvariant(),
			BoardCode = boardCode,
		};
	}
}