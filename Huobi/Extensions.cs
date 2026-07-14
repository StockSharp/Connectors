namespace StockSharp.Huobi;

static class Extensions
{
	public static bool? ToInitiator(this string role)
	{
		if (role.IsEmpty())
			return null;

		return role switch
		{
			"taker" => true,
			"maker" => (bool?)false,
			_ => throw new ArgumentOutOfRangeException(nameof(role), role, LocalizedStrings.InvalidValue),
		};
	}

	public static string ToStopOperator(this bool? stopOperator)
	{
		return stopOperator is null ? null : (stopOperator.Value ? "gte" : "lte");
	}

	public static bool? ToStopOperator(this string stopOperator)
	{
		if (stopOperator.IsEmpty())
			return null;

		return stopOperator switch
		{
			"gte" => true,
			"lte" => (bool?)false,
			_ => throw new ArgumentOutOfRangeException(nameof(stopOperator), stopOperator, LocalizedStrings.InvalidValue),
		};
	}

	public static string ToNative(this OrderTypes type)
	{
		return type switch
		{
			OrderTypes.Limit => "limit",
			OrderTypes.Market => "market",
			_ => throw new ArgumentOutOfRangeException(nameof(type), type, LocalizedStrings.InvalidValue),
		};
	}

	public static OrderTypes ToOrderType(this string type)
	{
		return type.ToLowerInvariant() switch
		{
			"limit" => OrderTypes.Limit,
			"market" => OrderTypes.Market,
			_ => throw new ArgumentOutOfRangeException(nameof(type), type, LocalizedStrings.InvalidValue),
		};
	}

	public static OrderTypes ToOrderType(this string type, out Sides side, out TimeInForce? tif, out bool? isMaker)
	{
		if (type.IsEmpty())
			throw new ArgumentNullException(nameof(type));

		tif = null;
		isMaker = null;

		switch (type.ToLowerInvariant())
		{
			case "buy-limit":
				side = Sides.Buy;
				return OrderTypes.Limit;
			case "sell-limit":
				side = Sides.Sell;
				return OrderTypes.Limit;
			case "buy-limit-maker":
				side = Sides.Buy;
				isMaker = true;
				return OrderTypes.Limit;
			case "sell-limit-maker":
				side = Sides.Sell;
				isMaker = true;
				return OrderTypes.Limit;
			case "buy-ioc":
				side = Sides.Buy;
				tif = TimeInForce.CancelBalance;
				return OrderTypes.Limit;
			case "sell-ioc":
				side = Sides.Sell;
				tif = TimeInForce.CancelBalance;
				return OrderTypes.Limit;
			case "buy-limit-fok":
				side = Sides.Buy;
				tif = TimeInForce.MatchOrCancel;
				return OrderTypes.Limit;
			case "sell-limit-fok":
				side = Sides.Sell;
				tif = TimeInForce.MatchOrCancel;
				return OrderTypes.Limit;
			case "buy-market":
				side = Sides.Buy;
				return OrderTypes.Market;
			case "sell-market":
				side = Sides.Sell;
				return OrderTypes.Market;
			default:
				throw new ArgumentOutOfRangeException(nameof(type), type, LocalizedStrings.InvalidValue);
		}
	}

	public static OrderTypes? ToFutureOrderType(this string str, out TimeInForce? tif, out bool? opponent, out bool? postOnly, out int? optimal)
	{
		tif = null;
		opponent = null;
		postOnly = null;
		optimal = null;

		if (str.IsEmpty())
			return null;

		switch (str)
		{
			case "opponent":
				opponent = true;
				return null;
			case "post_only":
				postOnly = true;
				return null;
			case "limit":
				return OrderTypes.Limit;
			case "fok":
				tif = TimeInForce.MatchOrCancel;
				return null;
			case "ioc":
				tif = TimeInForce.CancelBalance;
				return null;
			default:
				throw new ArgumentOutOfRangeException(nameof(str), str, LocalizedStrings.InvalidValue);
		}
	}

	public static string ToFutureNative(this TimeInForce? tif, bool? opponent, bool? postOnly, int? optimal)
	{
		var prefix = optimal == null ? "" : $"optimal_{optimal}_";

		switch (tif)
		{
			case null:
			case TimeInForce.PutInQueue:
			{
				if (opponent == true)
					return "opponent";
				else if (postOnly == true)
					return "post_only";

				return optimal == null ? "limit" : $"optimal_{optimal}";
			}
			case TimeInForce.MatchOrCancel:
				return $"{prefix}fok";
			case TimeInForce.CancelBalance:
				return opponent == true ? "opponent_ioc" : $"{prefix}ioc";
			default:
				throw new ArgumentOutOfRangeException(nameof(tif), tif, LocalizedStrings.InvalidValue);
		}
	}

	public static string ToNative(this OrderTypes? type, Sides side, TimeInForce? tif, bool? isMaker)
	{
		switch (type)
		{
			case null:
			case OrderTypes.Limit:
			{
				return side switch
				{
					Sides.Buy => tif switch
					{
						TimeInForce.MatchOrCancel => "buy-limit-fok",
						TimeInForce.CancelBalance => "buy-ioc",
						_ => isMaker == true ? "buy-limit-maker" : "buy-limit",
					},
					Sides.Sell => tif switch
					{
						TimeInForce.MatchOrCancel => "sell-limit-fok",
						TimeInForce.CancelBalance => "sell-ioc",
						_ => isMaker == true ? "sell-limit-maker" : "sell-limit",
					},
					_ => throw new ArgumentOutOfRangeException(nameof(side), side, LocalizedStrings.InvalidValue),
				};
			}
			case OrderTypes.Market:
			{
				return side switch
				{
					Sides.Buy => "buy-market",
					Sides.Sell => "sell-market",
					_ => throw new ArgumentOutOfRangeException(nameof(side), side, LocalizedStrings.InvalidValue),
				};
			}
			default:
				throw new ArgumentOutOfRangeException(nameof(type), type, LocalizedStrings.InvalidValue);
		}
	}

	public static string ToOffset(this bool openClose)
	{
		return openClose ? "open" : "close";
	}

	public static bool? ToOpenClose(this string str)
	{
		if (str.IsEmpty())
			return null;

		return str switch
		{
			"open" => true,
			"close" => (bool?)false,
			_ => throw new ArgumentOutOfRangeException(nameof(str), str, LocalizedStrings.InvalidValue),
		};
	}

	public static string ToNative(this TimeInForce tif)
	{
		return tif switch
		{
			TimeInForce.PutInQueue => "gtc",
			TimeInForce.MatchOrCancel => "fok",
			TimeInForce.CancelBalance => "ioc",
			_ => throw new ArgumentOutOfRangeException(nameof(tif), tif, LocalizedStrings.InvalidValue),
		};
	}

	public static TimeInForce? ToTimeInForce(this string tif)
	{
		if (tif.IsEmpty())
			return null;

		return tif switch
		{
			"gtc" => (TimeInForce?)TimeInForce.PutInQueue,
			"fok" => (TimeInForce?)TimeInForce.MatchOrCancel,
			"ioc" => (TimeInForce?)TimeInForce.CancelBalance,
			_ => throw new ArgumentOutOfRangeException(nameof(tif), tif, LocalizedStrings.InvalidValue),
		};
	}

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

	public static readonly PairSet<TimeSpan, string> TimeFrames = new()
	{
		{ TimeSpan.FromMinutes(1), "1min" },
		{ TimeSpan.FromMinutes(5), "5min" },
		{ TimeSpan.FromMinutes(15), "15min" },
		{ TimeSpan.FromMinutes(30), "30min" },
		{ TimeSpan.FromHours(1), "60min" },
		{ TimeSpan.FromHours(4), "4hour" },
		{ TimeSpan.FromDays(1), "1day" },
		{ TimeSpan.FromDays(7), "1week" },
		{ TimeSpan.FromTicks(TimeHelper.TicksPerMonth), "1mon" },
		//{ TimeSpan.FromTicks(TimeHelper.TicksPerYear), "1year" }, // futures not supported
	};

	public static string ToNative(this TimeSpan timeFrame)
	{
		return TimeFrames.TryGetValue(timeFrame) ?? throw new ArgumentOutOfRangeException(nameof(timeFrame), timeFrame, LocalizedStrings.InvalidValue);
	}

	public static TimeSpan ToTimeFrame(this string name)
	{
		return TimeFrames.TryGetKey2(name)
			?? throw new ArgumentOutOfRangeException(nameof(name), name, LocalizedStrings.InvalidValue);
	}

	public static string ToSymbol(this SecurityId securityId)
	{
		return securityId.SecurityCode.Remove("/").ToLowerInvariant();
	}

	public static SecurityId ToStockSharp(this string symbol, bool formatted = false)
	{
		if (!formatted)
			symbol = symbol.Insert(3, "/");

		return new()
		{
			SecurityCode = symbol.ToUpperInvariant(),
			BoardCode = BoardCodes.Huobi,
		};
	}

	public static OrderStates ToFuturesOrderState(this int status)
	{
		return status switch
		{
			// Ready to submit the orders
			1 or 2
				=> OrderStates.Pending,

			// Have sumbmitted the orders
			3 or
			// Orders partially matched
			4 or
			// Orders cancelling
			11
				=> OrderStates.Active,

			// Orders cancelled with partially matched
			5 or
			// Orders fully matched
			6 or
			// Orders cancelled
			7
				=> OrderStates.Done,

			_ => throw new ArgumentOutOfRangeException(nameof(status), status, LocalizedStrings.InvalidValue),
		};
	}
}