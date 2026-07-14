namespace StockSharp.Bitfinex.Native;

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
			"buy" or "bid" => Sides.Buy,
			"sell" or "ask" => Sides.Sell,
			_ => throw new ArgumentOutOfRangeException(nameof(side), side, LocalizedStrings.InvalidValue),
		};
	}

	public static OrderTypes? ToOrderType(this string type, out TimeInForce? tif, out bool? isExchange, out bool isTrailing)
	{
		tif = null;
		isExchange = false;
		isTrailing = false;

		if (type.IsEmpty())
		{
			return null;
			//throw new ArgumentNullException(nameof(type));
		}

		isExchange = type.StartsWithIgnoreCase("exchange");
		
		switch (type.Remove("exchange ", true).ToUpperInvariant())
		{
			case "LIMIT":
				return OrderTypes.Limit;
			case "MARKET":
				return OrderTypes.Market;
			case "STOP":
				return OrderTypes.Conditional;
			case "TRAILING STOP":
				isTrailing = true;
				return OrderTypes.Conditional;
			case "FOK":
				tif = TimeInForce.MatchOrCancel;
				return OrderTypes.Conditional;
			default:
				throw new ArgumentOutOfRangeException(nameof(type), type, LocalizedStrings.InvalidValue);
		}
	}

	public static string ToNative(this OrderTypes? type, TimeInForce? tif, bool isExchange, BitfinexOrderCondition condition)
	{
		var prefix = isExchange ? "exchange " : string.Empty;

		switch (type)
		{
			case null:
			case OrderTypes.Limit:
			{
				return tif switch
				{
					null or TimeInForce.PutInQueue => prefix + "limit",
					TimeInForce.MatchOrCancel => prefix + "fill-or-kill",
					_ => throw new ArgumentOutOfRangeException(nameof(tif), tif, null),
				};
			}
			case OrderTypes.Market:
				return prefix + "market";
			case OrderTypes.Conditional:
			{
				if (condition == null)
					throw new ArgumentNullException(nameof(condition));

				return prefix + (condition.IsTrailing ? "trailing-stop" : "stop");
			}
			default:
				throw new ArgumentOutOfRangeException(nameof(type), type, LocalizedStrings.InvalidValue);
		}
	}

	public static OrderStates ToOrderState(this string status)
	{
		if (status.IsEmpty())
			throw new ArgumentNullException(nameof(status));

		if (status.StartsWithIgnoreCase("EXECUTED"))
			return OrderStates.Done;
		else if (status.StartsWithIgnoreCase("PARTIALLY"))
			return OrderStates.Active;
		else if (status.StartsWithIgnoreCase("WIDTH CANCELED"))
			return OrderStates.Done;

		return status switch
		{
			"ACTIVE" or "PARTIALLY FILLED" => OrderStates.Active,
			"CANCELED" or "EXECUTED" => OrderStates.Done,
			_ => throw new ArgumentOutOfRangeException(nameof(status), status, LocalizedStrings.InvalidValue),
		};
	}

	public static readonly PairSet<TimeSpan, string> TimeFrames = new()
	{
		{ TimeSpan.FromMinutes(1), "1m" },
		{ TimeSpan.FromMinutes(5), "5m" },
		{ TimeSpan.FromMinutes(15), "15m" },
		{ TimeSpan.FromMinutes(30), "30m" },
		{ TimeSpan.FromHours(1), "1h" },
		{ TimeSpan.FromHours(3), "3h" },
		{ TimeSpan.FromHours(6), "6h" },
		{ TimeSpan.FromHours(12), "12h" },
		{ TimeSpan.FromDays(1), "1D" },
		{ TimeSpan.FromDays(7), "7D" },
		{ TimeSpan.FromDays(14), "14D" },
		{ TimeSpan.FromTicks(TimeHelper.TicksPerMonth), "1M" },
	};

	public static string ToNative(this TimeSpan timeFrame)
	{
		return TimeFrames.TryGetValue(timeFrame) ?? throw new ArgumentOutOfRangeException(nameof(timeFrame), timeFrame, LocalizedStrings.InvalidValue);
	}

	public static TimeSpan ToTimeFrame(this string name)
	{
		return TimeFrames.TryGetKey2(name) ?? throw new ArgumentOutOfRangeException(nameof(name), name, LocalizedStrings.InvalidValue);
	}

	public static string ToCurrency(this SecurityId securityId)
	{
		return "t" + securityId.SecurityCode?.Remove("/").ToUpperInvariant();
	}

	public static SecurityId ToStockSharp(this string currency)
	{
		if (currency.Length > 3)
		{
			if ((currency[0] == 't' || currency[0] == 'f') && currency.Length > 6)
				currency = currency[1..];

			if (currency.Length > 6)
				currency = currency.Insert(currency.Length - 3, "/");
			else if (currency[3] != '/')
				currency = currency.Insert(3, "/");
		}

		return new SecurityId
		{
			SecurityCode = currency.ToUpperInvariant(),
			BoardCode = BoardCodes.Bitfinex,
		};
	}

	public static string ToTif(this DateTime? tillDate)
	{
		return tillDate.EnsureToday()?.ToString("yyyy-MM-dd hh\\:mm\\:ss");
	}

	[Flags]
	private enum Flags : short
	{
		Hidden = 64,
		Close = 512,
		ReduceOnly = 1024,
		PostOnly = 4096,
		OneCancelOther = 16384,
	}

	public static bool IsHidden(this int flags) => flags.IsFlags(Flags.Hidden);
	public static bool IsClose(this int flags) => flags.IsFlags(Flags.Close);
	public static bool IsReduceOnly(this int flags) => flags.IsFlags(Flags.ReduceOnly);
	public static bool IsPostOnly(this int flags) => flags.IsFlags(Flags.PostOnly);
	public static bool IsOneCancelOther(this int flags) => flags.IsFlags(Flags.OneCancelOther);

	private static bool IsFlags(this int flags, Flags part) => ((Flags)flags).HasFlag(part);

	public static short? ToFlags(this BitfinexOrderCondition condition, OrderRegisterMessage regMsg)
	{
		Flags flags = 0;

		if (regMsg.VisibleVolume != null)
			flags |= Flags.Hidden;

		if (regMsg.PostOnly == true)
			flags |= Flags.PostOnly;
		
		if (condition?.Close == true)
			flags |= Flags.Close;

		if (regMsg.PositionEffect == OrderPositionEffects.CloseOnly)
			flags |= Flags.ReduceOnly;

		if (condition?.OneCancelOther == true)
			flags |= Flags.OneCancelOther;

		return flags == 0 ? null : (short?)flags;
	}
}