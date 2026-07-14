namespace StockSharp.Deribit.Native;

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

    public static string ToNative(this TimeInForce? tif)
    {
		return tif switch
		{
			null or TimeInForce.PutInQueue => "good_til_cancelled",
			TimeInForce.CancelBalance => "immediate_or_cancel",
			TimeInForce.MatchOrCancel => "fill_or_kill",
			_ => throw new ArgumentOutOfRangeException(nameof(tif), tif, LocalizedStrings.InvalidValue),
		};
	}

    public static TimeInForce ToTimeInForce(this string tif)
    {
		return tif switch
		{
			null or "" or "good_til_cancelled" => TimeInForce.PutInQueue,
			"immediate_or_cancel" => TimeInForce.CancelBalance,
			"fill_or_kill" => TimeInForce.MatchOrCancel,
			_ => throw new ArgumentOutOfRangeException(nameof(tif), tif, LocalizedStrings.InvalidValue),
		};
	}

    public static string ToNative(this SecurityTypes type)
    {
		return type switch
		{
			SecurityTypes.Future => "futures",
			SecurityTypes.Option => "options",
			_ => throw new ArgumentOutOfRangeException(nameof(type), type, LocalizedStrings.InvalidValue),
		};
	}

    public static string ToNative(this OrderTypes? type, decimal? stopPrice)
    {
		return type switch
		{
			null or OrderTypes.Limit => "limit",
			OrderTypes.Market => "market",
			OrderTypes.Conditional => stopPrice == null ? "stop_market" : "stop_limit",
			_ => throw new ArgumentOutOfRangeException(nameof(type), type, LocalizedStrings.InvalidValue),
		};
	}

    public static OrderTypes? ToOrderType(this string type)
    {
        if (type.IsEmpty())
            return null;

		return type switch
		{
			"limit" => (OrderTypes?)OrderTypes.Limit,
			"market" => (OrderTypes?)OrderTypes.Market,
			"stop_limit" or "stop_market" => (OrderTypes?)OrderTypes.Conditional,
			_ => throw new ArgumentOutOfRangeException(nameof(type), type, LocalizedStrings.InvalidValue),
		};
	}

    public static string ToNative(this DeribitOrderAdvancedTypes? type)
    {
		return type switch
		{
			null => null,
			DeribitOrderAdvancedTypes.ImpliedVolatility => "implv",
			DeribitOrderAdvancedTypes.Usd => "usd",
			_ => throw new ArgumentOutOfRangeException(nameof(type), type, LocalizedStrings.InvalidValue),
		};
	}

    public static DeribitOrderAdvancedTypes? ToAdvancedType(this string type)
    {
        if (type.IsEmpty())
            return null;

		return type.ToLowerInvariant() switch
		{
			"false" => null,
			"implv" => (DeribitOrderAdvancedTypes?)DeribitOrderAdvancedTypes.ImpliedVolatility,
			"usd" => (DeribitOrderAdvancedTypes?)DeribitOrderAdvancedTypes.Usd,
			_ => throw new ArgumentOutOfRangeException(nameof(type), type, LocalizedStrings.InvalidValue),
		};
	}

    public static SecurityTypes? ToSecurityType(this string type, ILogReceiver logs)
    {
        switch (type?.ToLowerInvariant())
        {
            case "future":
                return SecurityTypes.Future;
            case "option":
                return SecurityTypes.Option;
            case "future_combo":
            case "option_combo":
                return SecurityTypes.MultiLeg;
            case "spot":
                return SecurityTypes.CryptoCurrency;
            default:
                {
                    if (logs is null)
                        throw new ArgumentNullException(nameof(logs));

                    logs.AddErrorLog("Type '{0}' is unknown.", type);
                    return null;
                }
        }
    }

    public static OptionTypes ToOptionType(this string type)
    {
		return type switch
		{
			"call" => OptionTypes.Call,
			"put" => OptionTypes.Put,
			_ => throw new ArgumentOutOfRangeException(nameof(type), type, LocalizedStrings.InvalidValue),
		};
	}

    public static string ToCurrency(this SecurityId securityId)
    {
        return securityId.SecurityCode.ToUpperInvariant();
    }

    public static SecurityId ToStockSharp(this string symbol)
    {
        return new SecurityId
        {
            SecurityCode = symbol.ToUpperInvariant(),
            BoardCode = BoardCodes.Deribit,
        };
    }

    public static decimal GetBalance(this Model.Order order)
    {
        if (order == null)
            throw new ArgumentNullException(nameof(order));

        return (decimal)(order.Quantity - (order.FilledQuantity ?? 0));
    }

    public static OrderStates ToOrderState(this string state)
    {
		return state switch
		{
			"cancelled" or "filled" => OrderStates.Done,
			"open" or "untriggered" => OrderStates.Active,
			"rejected" => OrderStates.Failed,
			_ => throw new ArgumentOutOfRangeException(nameof(state), state, LocalizedStrings.InvalidValue),
		};
	}

    public static string ToNative(this DeribitOrderTriggers? trigger)
    {
		return trigger switch
		{
			DeribitOrderTriggers.Index => "index_price",
			DeribitOrderTriggers.Mark => "mark_price",
			DeribitOrderTriggers.Last => "last_price",
			null => null,
			_ => throw new ArgumentOutOfRangeException(nameof(trigger), trigger, LocalizedStrings.InvalidValue),
		};
	}

    public static DeribitOrderTriggers? ToTrigger(this string execInst)
    {
        if (execInst.IsEmpty())
            return null;

		return execInst.ToLowerInvariant() switch
		{
			"index_price" => (DeribitOrderTriggers?)DeribitOrderTriggers.Index,
			"mark_price" => (DeribitOrderTriggers?)DeribitOrderTriggers.Mark,
			"last_price" => (DeribitOrderTriggers?)DeribitOrderTriggers.Last,
			_ => throw new ArgumentOutOfRangeException(nameof(execInst), execInst, LocalizedStrings.InvalidValue),
		};
	}

    public static SecurityStates? ToSecurityState(this string state)
    {
        if (state.IsEmpty())
            return null;

		return state.ToLowerInvariant() switch
		{
			"open" => (SecurityStates?)SecurityStates.Trading,
			"closed" => (SecurityStates?)SecurityStates.Stoped,
			_ => throw new ArgumentOutOfRangeException(nameof(state), state, LocalizedStrings.InvalidValue),
		};
	}

    public static OrderStates ToWithdrawState(this string state)
    {
		return state.ToLowerInvariant() switch
		{
			"unconfirmed" => OrderStates.Pending,
			"confirmed" => OrderStates.Active,
			"cancelled" or "completed" => OrderStates.Done,
			"interrupted" or "rejected" => OrderStates.Failed,
			_ => throw new ArgumentOutOfRangeException(nameof(state), state, LocalizedStrings.InvalidValue),
		};
	}

    public static readonly PairSet<TimeSpan, string> TimeFrames = new()
    {
        { TimeSpan.FromMinutes(1), "1" },
        { TimeSpan.FromMinutes(3), "3" },
        { TimeSpan.FromMinutes(5), "5" },
        { TimeSpan.FromMinutes(10), "10" },
        { TimeSpan.FromMinutes(15), "15" },
        { TimeSpan.FromMinutes(30), "30" },
        { TimeSpan.FromMinutes(60), "60" },
        { TimeSpan.FromMinutes(120), "120" },
        { TimeSpan.FromMinutes(180), "180" },
        { TimeSpan.FromMinutes(360), "360" },
        { TimeSpan.FromMinutes(720), "720" },
        { TimeSpan.FromDays(1), "1D" },
    };

	public static string ToNative(this TimeSpan timeFrame)
		=> TimeFrames.TryGetValue(timeFrame) ?? throw new ArgumentOutOfRangeException(nameof(timeFrame), timeFrame, LocalizedStrings.InvalidValue);

	public static TimeSpan ToTimeFrame(this string name)
		=> TimeFrames.TryGetKey2(name) ?? throw new ArgumentOutOfRangeException(nameof(name), name, LocalizedStrings.InvalidValue);
}